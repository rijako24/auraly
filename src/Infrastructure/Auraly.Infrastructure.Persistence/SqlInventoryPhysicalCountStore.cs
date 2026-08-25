using System.Data;
using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryPhysicalCountStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider) : IInventoryPhysicalCountStore
{
    public async Task<InventoryPhysicalCountDetail> CreateAsync(InventoryUserIdentity user, CreateInventoryPhysicalCountRequest request, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var now = timeProvider.GetUtcNow();
            const string header = """
                IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses w INNER JOIN dbo.Businesses b ON b.BusinessId=w.BusinessId WHERE w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId AND b.TenantId=@TenantId AND w.IsActive=1)
                  THROW 51201,'Warehouse is outside the authenticated business.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.BusinessReasons WHERE BusinessId=@BusinessId AND ReasonType=N'StockCount' AND Code=@ReasonCode AND IsActive=1)
                  THROW 51201,'The stock count reason is not active.',1;
                INSERT dbo.InventoryPhysicalCounts
                  (InventoryPhysicalCountId,BusinessId,WarehouseId,ScopeType,ReasonCode,Notes,BaseInventorySequence,Status,CreatedByUserId,CreatedAt)
                SELECT @CountId,@BusinessId,@WarehouseId,@ScopeType,@ReasonCode,@Notes,COALESCE(LastCompletedSequence,0),N'Draft',@UserId,@Now
                FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId;
                IF @@ROWCOUNT=0 THROW 51201,'The business processing cursor is unavailable.',1;
                """;
            await using (var command = new SqlCommand(header, connection, transaction))
            {
                command.Parameters.AddWithValue("@CountId", request.InventoryPhysicalCountId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                command.Parameters.AddWithValue("@ScopeType", request.ScopeType);
                command.Parameters.AddWithValue("@ReasonCode", request.ReasonCode);
                command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(token);
            }

            foreach (var list in request.Lists)
            {
                const string insertList = """
                    IF @AssignedUserId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.AppUsers WHERE UserId=@AssignedUserId AND TenantId=@TenantId AND IsActive=1)
                      THROW 51201,'Assigned user is outside the authenticated tenant.',1;
                    INSERT dbo.InventoryPhysicalCountLists(InventoryPhysicalCountListId,InventoryPhysicalCountId,Name,AssignedUserId,Status)
                    VALUES(@ListId,@CountId,@Name,@AssignedUserId,N'Pending');
                    """;
                await using (var command = new SqlCommand(insertList, connection, transaction))
                {
                    command.Parameters.AddWithValue("@ListId", list.ListId); command.Parameters.AddWithValue("@CountId", request.InventoryPhysicalCountId);
                    command.Parameters.AddWithValue("@Name", list.Name); command.Parameters.AddWithValue("@AssignedUserId", (object?)list.AssignedUserId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@TenantId", user.TenantId);
                    await command.ExecuteNonQueryAsync(token);
                }
                foreach (var productId in list.ProductIds)
                {
                    const string insertLine = """
                        IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1 AND ManageStock=1)
                          THROW 51201,'A selected product is not inventory enabled.',1;
                        IF EXISTS(
                          SELECT 1 FROM dbo.InventoryPhysicalCountLines line
                          INNER JOIN dbo.InventoryPhysicalCounts count ON count.InventoryPhysicalCountId=line.InventoryPhysicalCountId
                          WHERE count.BusinessId=@BusinessId AND count.WarehouseId=@WarehouseId AND line.ProductId=@ProductId
                            AND count.Status IN (N'Draft',N'PreCounting',N'Counting',N'Review',N'Closing'))
                          THROW 51202,'A selected product already belongs to another active physical count.',1;
                        INSERT dbo.InventoryPhysicalCountLines
                          (InventoryPhysicalCountId,InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase)
                        SELECT @CountId,@ListId,p.ProductId,COALESCE(p.ProductCode,p.Sku,p.Reference,N''),p.Name,COALESCE(b.QuantityOnHand,0)
                        FROM dbo.Products p
                        LEFT JOIN dbo.InventoryBalances b ON b.BusinessId=p.BusinessId AND b.WarehouseId=@WarehouseId AND b.ProductId=p.ProductId
                        WHERE p.ProductId=@ProductId AND p.BusinessId=@BusinessId;
                        """;
                    await using var command = new SqlCommand(insertLine, connection, transaction);
                    command.Parameters.AddWithValue("@CountId", request.InventoryPhysicalCountId); command.Parameters.AddWithValue("@ListId", list.ListId);
                    command.Parameters.AddWithValue("@ProductId", productId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                    await command.ExecuteNonQueryAsync(token);
                }
            }

            if (request.ScopeType == "General")
            {
                const string validateGeneralScope = """
                    IF EXISTS(
                      SELECT 1
                      FROM dbo.Products product
                      LEFT JOIN dbo.ProductLinks link ON link.BusinessId=product.BusinessId
                        AND link.ChildProductId=product.ProductId AND link.SharesInventory=1 AND link.IsActive=1
                      WHERE product.BusinessId=@BusinessId AND product.IsActive=1 AND product.ManageStock=1
                        AND link.ProductLinkId IS NULL
                        AND NOT EXISTS(
                          SELECT 1 FROM dbo.InventoryPhysicalCountLines line
                          WHERE line.InventoryPhysicalCountId=@CountId AND line.ProductId=product.ProductId))
                      THROW 51201,'A general physical count must include every inventory product.',1;
                    """;
                await using var command = new SqlCommand(validateGeneralScope, connection, transaction);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@CountId", request.InventoryPhysicalCountId);
                await command.ExecuteNonQueryAsync(token);
            }
            await transaction.CommitAsync(token);
            return (await GetAsync(user, request.InventoryPhysicalCountId, token))!;
        }
        catch (SqlException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw Translate(exception);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<InventoryPhysicalCountPage> ListAsync(InventoryUserIdentity user, InventoryPhysicalCountQuery query, CancellationToken token)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.InventoryPhysicalCounts c INNER JOIN dbo.Warehouses w ON w.WarehouseId=c.WarehouseId
            WHERE c.BusinessId=@BusinessId AND (@WarehouseId IS NULL OR c.WarehouseId=@WarehouseId)
              AND (@Status IS NULL OR c.Status=@Status)
              AND (@Search IS NULL OR w.Name LIKE @Pattern OR c.ReasonCode LIKE @Pattern OR c.FinalDocumentNumber LIKE @Pattern);
            SELECT c.InventoryPhysicalCountId,c.WarehouseId,w.Name,c.ScopeType,c.ReasonCode,c.Status,
              (SELECT COUNT(*) FROM dbo.InventoryPhysicalCountLists l WHERE l.InventoryPhysicalCountId=c.InventoryPhysicalCountId),
              COUNT(line.ProductId),COUNT(line.PreCountQuantity),COUNT(line.CountedQuantity),
              SUM(CASE WHEN line.ApprovedDifference<>0 THEN 1 ELSE 0 END),c.CreatedAt,c.FinalDocumentNumber
            FROM dbo.InventoryPhysicalCounts c INNER JOIN dbo.Warehouses w ON w.WarehouseId=c.WarehouseId
            LEFT JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountId=c.InventoryPhysicalCountId
            WHERE c.BusinessId=@BusinessId AND (@WarehouseId IS NULL OR c.WarehouseId=@WarehouseId)
              AND (@Status IS NULL OR c.Status=@Status)
              AND (@Search IS NULL OR w.Name LIKE @Pattern OR c.ReasonCode LIKE @Pattern OR c.FinalDocumentNumber LIKE @Pattern)
            GROUP BY c.InventoryPhysicalCountId,c.WarehouseId,w.Name,c.ScopeType,c.ReasonCode,c.Status,c.CreatedAt,c.FinalDocumentNumber
            ORDER BY c.CreatedAt DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create(); await connection.OpenAsync(token); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@WarehouseId", (object?)query.WarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value); command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Pattern", query.Search is null ? DBNull.Value : $"%{query.Search}%"); command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize); command.Parameters.AddWithValue("@PageSize", query.PageSize);
        await using var reader = await command.ExecuteReaderAsync(token); await reader.ReadAsync(token); var total = reader.GetInt32(0); await reader.NextResultAsync(token);
        var items = new List<InventoryPhysicalCountItem>();
        while (await reader.ReadAsync(token)) items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.IsDBNull(10) ? 0 : reader.GetInt32(10), reader.GetDateTimeOffset(11), reader.IsDBNull(12) ? null : reader.GetString(12)));
        return new(items, query.Page, query.PageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (decimal)query.PageSize));
    }

    public async Task<InventoryPhysicalCountDetail?> GetAsync(InventoryUserIdentity user, Guid countId, CancellationToken token)
    {
        const string sql = """
            SELECT c.InventoryPhysicalCountId,c.WarehouseId,w.Name,c.ScopeType,c.ReasonCode,c.Notes,c.BaseInventorySequence,c.Status,c.CreatedByUserId,c.CreatedAt,c.StartedAt,c.ReviewStartedAt,c.ClosedAt,c.FinalInventoryOperationId,c.FinalDocumentNumber
            FROM dbo.InventoryPhysicalCounts c INNER JOIN dbo.Warehouses w ON w.WarehouseId=c.WarehouseId INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
            WHERE c.InventoryPhysicalCountId=@CountId AND c.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            SELECT InventoryPhysicalCountListId,Name,AssignedUserId,Status,PreCountSubmittedAt,CountSubmittedAt
            FROM dbo.InventoryPhysicalCountLists WHERE InventoryPhysicalCountId=@CountId ORDER BY Name;
            SELECT InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase,PreCountQuantity,CountedQuantity,ExpectedQuantityAtCount,ApprovedDifference,IsExcluded,ExclusionReason
            FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountId=@CountId ORDER BY ProductNameSnapshot,ProductId;
            """;
        await using var connection = connections.Create(); await connection.OpenAsync(token); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CountId", countId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
        await using var reader = await command.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) return null;
        var header = new Header(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6), reader.GetString(7), reader.GetGuid(8), reader.GetDateTimeOffset(9), NullableDate(reader,10), NullableDate(reader,11), NullableDate(reader,12), reader.IsDBNull(13)?null:reader.GetGuid(13), reader.IsDBNull(14)?null:reader.GetString(14));
        await reader.NextResultAsync(token); var lists = new List<ListState>();
        while (await reader.ReadAsync(token)) lists.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2)?null:reader.GetGuid(2), reader.GetString(3), NullableDate(reader,4), NullableDate(reader,5)));
        await reader.NextResultAsync(token); var lines = new Dictionary<Guid,List<InventoryPhysicalCountLine>>();
        while (await reader.ReadAsync(token))
        {
            var listId=reader.GetGuid(0); if(!lines.TryGetValue(listId,out var values)){values=[];lines[listId]=values;}
            values.Add(new(reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetDecimal(4),NullableDecimal(reader,5),NullableDecimal(reader,6),NullableDecimal(reader,7),NullableDecimal(reader,8),reader.GetBoolean(9),reader.IsDBNull(10)?null:reader.GetString(10)));
        }
        return new(header.Id,header.WarehouseId,header.WarehouseName,header.Scope,header.Reason,header.Notes,header.BaseSequence,header.Status,header.CreatedBy,header.CreatedAt,header.StartedAt,header.ReviewAt,header.ClosedAt,header.FinalId,header.FinalNumber,
            lists.Select(list=>new InventoryPhysicalCountList(list.Id,list.Name,list.UserId,list.Status,list.PreSubmitted,list.CountSubmitted,lines.GetValueOrDefault(list.Id)??[])).ToArray());
    }

    public Task<InventoryPhysicalCountDetail> StartAsync(InventoryUserIdentity user, Guid countId, CancellationToken token) =>
        MutateAsync(user,countId,"""
            UPDATE dbo.InventoryPhysicalCounts SET Status=N'PreCounting',StartedAt=COALESCE(StartedAt,@Now)
            WHERE InventoryPhysicalCountId=@CountId AND BusinessId=@BusinessId AND Status=N'Draft';
            IF @@ROWCOUNT=0 THROW 51202,'Only a draft physical count can be started.',1;
            UPDATE dbo.InventoryPhysicalCountLists SET Status=N'PreCounting' WHERE InventoryPhysicalCountId=@CountId;
            """,token);

    public async Task<InventoryPhysicalCountDetail> SaveCaptureAsync(InventoryUserIdentity user, Guid countId, Guid listId, bool isFinalCount, SaveInventoryPhysicalCountCaptureRequest request, CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            const string state="""
                SELECT c.Status,l.Status FROM dbo.InventoryPhysicalCounts c WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.InventoryPhysicalCountLists l ON l.InventoryPhysicalCountId=c.InventoryPhysicalCountId
                WHERE c.InventoryPhysicalCountId=@CountId AND l.InventoryPhysicalCountListId=@ListId AND c.BusinessId=@BusinessId;
                """;
            string countStatus,listStatus;
            await using(var command=new SqlCommand(state,connection,transaction)){command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@ListId",listId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new InventoryValidationException("Physical count list was not found.");countStatus=reader.GetString(0);listStatus=reader.GetString(1);}
            if(!isFinalCount&&countStatus!="PreCounting"||isFinalCount&&countStatus!="Counting")throw new InventoryConflictException(isFinalCount?"The physical count is not in count stage.":"The physical count is not in pre-count stage.");
            if(listStatus=="Counted"||!isFinalCount&&listStatus=="PreCounted")throw new InventoryConflictException("The submitted list is read-only.");
            var sequence=0L;
            if(isFinalCount){await using var sequenceCommand=new SqlCommand("SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId;",connection,transaction);sequenceCommand.Parameters.AddWithValue("@BusinessId",user.BusinessId);sequence=Convert.ToInt64(await sequenceCommand.ExecuteScalarAsync(token));}
            foreach(var line in request.Lines)
            {
                var sql=isFinalCount?"""
                    UPDATE line SET CountedQuantity=@Quantity,CountedByUserId=@UserId,CountedAt=@Now,CountedAtProcessingSequence=@Sequence,
                      ExpectedQuantityAtCount=line.SystemQuantityAtBase+COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=@BusinessId AND m.WarehouseId=count.WarehouseId AND m.ProductId=line.ProductId AND m.ProcessingSequence>count.BaseInventorySequence AND m.ProcessingSequence<=@Sequence),0),
                      ApprovedDifference=@Quantity-(line.SystemQuantityAtBase+COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=@BusinessId AND m.WarehouseId=count.WarehouseId AND m.ProductId=line.ProductId AND m.ProcessingSequence>count.BaseInventorySequence AND m.ProcessingSequence<=@Sequence),0))
                    FROM dbo.InventoryPhysicalCountLines line INNER JOIN dbo.InventoryPhysicalCounts count ON count.InventoryPhysicalCountId=line.InventoryPhysicalCountId
                    WHERE line.InventoryPhysicalCountId=@CountId AND line.InventoryPhysicalCountListId=@ListId AND line.ProductId=@ProductId AND line.PreCountQuantity IS NOT NULL;
                    """:"""
                    UPDATE dbo.InventoryPhysicalCountLines SET PreCountQuantity=@Quantity,PreCountedByUserId=@UserId,PreCountedAt=@Now
                    WHERE InventoryPhysicalCountId=@CountId AND InventoryPhysicalCountListId=@ListId AND ProductId=@ProductId;
                    """;
                await using var command=new SqlCommand(sql,connection,transaction);command.Parameters.AddWithValue("@Quantity",line.Quantity);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());command.Parameters.AddWithValue("@Sequence",sequence);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@ListId",listId);command.Parameters.AddWithValue("@ProductId",line.ProductId);if(await command.ExecuteNonQueryAsync(token)==0)throw new InventoryValidationException("A captured product does not belong to the list or lacks its pre-count.");
            }
            if(request.Submit)
            {
                var missingColumn=isFinalCount?"CountedQuantity":"PreCountQuantity";var submittedStatus=isFinalCount?"Counted":"PreCounted";var dateColumn=isFinalCount?"CountSubmittedAt":"PreCountSubmittedAt";
                var submit=$"""
                    IF EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountListId=@ListId AND IsExcluded=0 AND {missingColumn} IS NULL)
                      THROW 51202,'Every product in the list must be captured before submission.',1;
                    UPDATE dbo.InventoryPhysicalCountLists SET Status=N'{submittedStatus}',{dateColumn}=@Now WHERE InventoryPhysicalCountListId=@ListId;
                    """;
                await using(var command=new SqlCommand(submit,connection,transaction)){command.Parameters.AddWithValue("@ListId",listId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());await command.ExecuteNonQueryAsync(token);}
                if(!isFinalCount)
                {
                    const string advance="""
                        IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLists WHERE InventoryPhysicalCountId=@CountId AND Status<>N'PreCounted')
                        BEGIN UPDATE dbo.InventoryPhysicalCounts SET Status=N'Counting' WHERE InventoryPhysicalCountId=@CountId; UPDATE dbo.InventoryPhysicalCountLists SET Status=N'Counting' WHERE InventoryPhysicalCountId=@CountId; END;
                        """;
                    await using var command=new SqlCommand(advance,connection,transaction);command.Parameters.AddWithValue("@CountId",countId);await command.ExecuteNonQueryAsync(token);
                }
                else
                {
                    const string advance="""
                        IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLists WHERE InventoryPhysicalCountId=@CountId AND Status<>N'Counted')
                          UPDATE dbo.InventoryPhysicalCounts SET Status=N'Review',ReviewStartedAt=@Now WHERE InventoryPhysicalCountId=@CountId;
                        """;
                    await using var command=new SqlCommand(advance,connection,transaction);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());await command.ExecuteNonQueryAsync(token);
                }
            }
            await transaction.CommitAsync(token);return(await GetAsync(user,countId,token))!;
        }
        catch(SqlException exception){await transaction.RollbackAsync(CancellationToken.None);throw Translate(exception);}catch{await transaction.RollbackAsync(CancellationToken.None);throw;}
    }

    public async Task<InventoryPhysicalCountClosePreparation> PrepareCloseAsync(InventoryUserIdentity user, Guid countId, CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            const string sql="""
                DECLARE @CurrentSequence BIGINT=(SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId);
                UPDATE dbo.InventoryPhysicalCounts SET Status=N'Closing',FinalInventoryOperationId=COALESCE(FinalInventoryOperationId,NEWID())
                WHERE InventoryPhysicalCountId=@CountId AND BusinessId=@BusinessId AND Status IN (N'Review',N'Closing');
                IF @@ROWCOUNT=0 THROW 51202,'Only a reviewed physical count can be closed.',1;
                IF EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountId=@CountId AND IsExcluded=0 AND CountedQuantity IS NULL)
                  THROW 51202,'Every included product must have a final count.',1;
                SELECT c.BusinessId,c.WarehouseId,c.ReasonCode,c.Notes,c.FinalInventoryOperationId,line.ProductId,
                  line.PreCountQuantity,
                  line.CountedQuantity+COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=c.BusinessId AND m.WarehouseId=c.WarehouseId AND m.ProductId=line.ProductId AND m.ProcessingSequence>line.CountedAtProcessingSequence AND m.ProcessingSequence<=@CurrentSequence),0)
                FROM dbo.InventoryPhysicalCounts c INNER JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountId=c.InventoryPhysicalCountId
                WHERE c.InventoryPhysicalCountId=@CountId AND c.BusinessId=@BusinessId AND line.IsExcluded=0 ORDER BY line.ProductCodeSnapshot,line.ProductId;
                """;
            await using var command=new SqlCommand(sql,connection,transaction);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);await using var reader=await command.ExecuteReaderAsync(token);
            Guid business=Guid.Empty,warehouse=Guid.Empty,final=Guid.Empty;string reason="";string?notes=null;var lines=new List<InventoryPhysicalCountCloseLine>();
            while(await reader.ReadAsync(token)){if(business==Guid.Empty){business=reader.GetGuid(0);warehouse=reader.GetGuid(1);reason=reader.GetString(2);notes=reader.IsDBNull(3)?null:reader.GetString(3);final=reader.GetGuid(4);}lines.Add(new(reader.GetGuid(5),reader.GetDecimal(6),reader.GetDecimal(7)));}
            if(lines.Count==0)throw new InventoryValidationException("The physical count has no included products.");await reader.DisposeAsync();await transaction.CommitAsync(token);return new(countId,business,warehouse,reason,notes,final,lines);
        }
        catch(SqlException exception){await transaction.RollbackAsync(CancellationToken.None);throw Translate(exception);}catch{await transaction.RollbackAsync(CancellationToken.None);throw;}
    }

    public async Task<InventoryPhysicalCountDetail> RecordCloseAcceptanceAsync(InventoryUserIdentity user, Guid countId, InventoryOperationAcceptance acceptance, CancellationToken token)
    {
        const string sql="""
            UPDATE dbo.InventoryPhysicalCounts SET FinalDocumentNumber=@Number
            WHERE InventoryPhysicalCountId=@CountId AND BusinessId=@BusinessId AND Status=N'Closing' AND FinalInventoryOperationId=@DocumentId;
            IF @@ROWCOUNT=0 AND NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCounts WHERE InventoryPhysicalCountId=@CountId AND BusinessId=@BusinessId AND Status=N'Closed' AND FinalInventoryOperationId=@DocumentId)
              THROW 51202,'Physical count acceptance state is inconsistent.',1;
            """;
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Number",acceptance.DocumentNumber);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@DocumentId",acceptance.DocumentId);try{await command.ExecuteNonQueryAsync(token);}catch(SqlException exception){throw Translate(exception);}return(await GetAsync(user,countId,token))!;
    }

    private async Task<InventoryPhysicalCountDetail> MutateAsync(InventoryUserIdentity user,Guid countId,string sql,CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());try{await command.ExecuteNonQueryAsync(token);}catch(SqlException exception){throw Translate(exception);}return(await GetAsync(user,countId,token))!;
    }
    private static Exception Translate(SqlException exception)=>exception.Number==51202?new InventoryConflictException(exception.Message):exception.Number==51201?new InventoryValidationException(exception.Message):exception;
    private static DateTimeOffset? NullableDate(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDateTimeOffset(ordinal);
    private static decimal? NullableDecimal(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDecimal(ordinal);
    private sealed record Header(Guid Id,Guid WarehouseId,string WarehouseName,string Scope,string Reason,string? Notes,long BaseSequence,string Status,Guid CreatedBy,DateTimeOffset CreatedAt,DateTimeOffset? StartedAt,DateTimeOffset? ReviewAt,DateTimeOffset? ClosedAt,Guid? FinalId,string? FinalNumber);
    private sealed record ListState(Guid Id,string Name,Guid? UserId,string Status,DateTimeOffset? PreSubmitted,DateTimeOffset? CountSubmitted);
}
