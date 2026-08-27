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
            var draftId = Guid.NewGuid();
            const string header = """
                IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses w INNER JOIN dbo.Businesses b ON b.BusinessId=w.BusinessId WHERE w.WarehouseId=@WarehouseId AND w.BusinessId=@BusinessId AND b.TenantId=@TenantId AND w.IsActive=1)
                  THROW 51201,'Warehouse is outside the authenticated business.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.BusinessReasons WHERE BusinessId=@BusinessId AND ReasonType=N'StockCount' AND Code=@ReasonCode AND IsActive=1)
                  THROW 51201,'The stock count reason is not active.',1;
                INSERT dbo.InventoryPhysicalCounts
                  (InventoryPhysicalCountId,BusinessId,WarehouseId,ScopeType,ReasonCode,Notes,BaseInventorySequence,Status,CreatedByUserId,CreatedAt,StartedAt)
                SELECT @CountId,@BusinessId,@WarehouseId,@ScopeType,@ReasonCode,@Notes,COALESCE(LastCompletedSequence,0),N'Open',@UserId,@Now,@Now
                FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId;
                IF @@ROWCOUNT=0 THROW 51201,'The business processing cursor is unavailable.',1;
                INSERT dbo.InventoryPhysicalCountLists
                  (InventoryPhysicalCountListId,InventoryPhysicalCountId,Name,AssignedUserId,Status,Version,CreatedAt,UpdatedAt)
                VALUES(@DraftId,@CountId,@DraftName,@UserId,N'InProgress',1,@Now,@Now);
                """;
            await using (var command = new SqlCommand(header, connection, transaction))
            {
                command.Parameters.AddWithValue("@CountId", request.InventoryPhysicalCountId);
                command.Parameters.AddWithValue("@DraftId", draftId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                command.Parameters.AddWithValue("@ScopeType", request.ScopeType);
                command.Parameters.AddWithValue("@ReasonCode", request.ReasonCode);
                command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@DraftName", request.InitialDraftName);
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(token);
            }

            if (request.ScopeType == "General")
            {
                const string general = """
                    IF EXISTS(
                      SELECT 1 FROM dbo.Products p
                      LEFT JOIN dbo.ProductLinks link ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId AND link.SharesInventory=1 AND link.IsActive=1
                      WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND p.ManageStock=1 AND link.ProductLinkId IS NULL
                        AND EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLines line INNER JOIN dbo.InventoryPhysicalCounts count ON count.InventoryPhysicalCountId=line.InventoryPhysicalCountId
                          WHERE count.BusinessId=@BusinessId AND count.WarehouseId=@WarehouseId AND line.ProductId=p.ProductId
                            AND count.Status IN (N'Open',N'Reconciling',N'Closing',N'Draft',N'PreCounting',N'Counting',N'Review') AND count.InventoryPhysicalCountId<>@CountId))
                      THROW 51202,'A product already belongs to another active physical count.',1;
                    INSERT dbo.InventoryPhysicalCountLines
                      (InventoryPhysicalCountId,InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase)
                    SELECT @CountId,@DraftId,p.ProductId,COALESCE(p.ProductCode,p.Sku,p.Reference,N''),p.Name,COALESCE(balance.QuantityOnHand,0)
                    FROM dbo.Products p
                    LEFT JOIN dbo.ProductLinks link ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId AND link.SharesInventory=1 AND link.IsActive=1
                    LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=p.BusinessId AND balance.WarehouseId=@WarehouseId AND balance.ProductId=p.ProductId
                    WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND p.ManageStock=1 AND link.ProductLinkId IS NULL;
                    """;
                await ExecuteAsync(connection, transaction, general, token,
                    P("@CountId", request.InventoryPhysicalCountId), P("@DraftId", draftId), P("@BusinessId", user.BusinessId), P("@WarehouseId", request.WarehouseId));
            }
            else
            {
                foreach (var productId in request.ProductIds)
                    await InsertDraftLineAsync(connection, transaction, request.InventoryPhysicalCountId, draftId, productId, user.BusinessId, request.WarehouseId, true, token);
            }

            await transaction.CommitAsync(token);
            return (await GetAsync(user, request.InventoryPhysicalCountId, token))!;
        }
        catch (SqlException exception)
        {
            await RollbackIfActiveAsync(transaction);
            throw Translate(exception);
        }
        catch { await RollbackIfActiveAsync(transaction); throw; }
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
              (SELECT COUNT(*) FROM dbo.InventoryPhysicalCountLists d WHERE d.InventoryPhysicalCountId=c.InventoryPhysicalCountId AND d.Status<>N'Discarded'),
              (SELECT COUNT(DISTINCT scope.ProductId) FROM dbo.InventoryPhysicalCountLines scope WHERE scope.InventoryPhysicalCountId=c.InventoryPhysicalCountId),
              (SELECT COUNT(DISTINCT counted.ProductId) FROM dbo.InventoryPhysicalCountLines counted WHERE counted.InventoryPhysicalCountId=c.InventoryPhysicalCountId AND counted.PreCountQuantity IS NOT NULL),
              (SELECT COUNT(DISTINCT verified.ProductId) FROM dbo.InventoryPhysicalCountLines verified WHERE verified.InventoryPhysicalCountId=c.InventoryPhysicalCountId AND verified.CountedQuantity IS NOT NULL),
              (SELECT COUNT(DISTINCT scope.ProductId) FROM dbo.InventoryPhysicalCountLines scope WHERE scope.InventoryPhysicalCountId=c.InventoryPhysicalCountId AND NOT EXISTS(
                SELECT 1 FROM dbo.InventoryPhysicalCountLines counted WHERE counted.InventoryPhysicalCountId=c.InventoryPhysicalCountId AND counted.ProductId=scope.ProductId AND counted.PreCountQuantity IS NOT NULL)),
              c.CreatedAt,c.FinalDocumentNumber
            FROM dbo.InventoryPhysicalCounts c INNER JOIN dbo.Warehouses w ON w.WarehouseId=c.WarehouseId
            WHERE c.BusinessId=@BusinessId AND (@WarehouseId IS NULL OR c.WarehouseId=@WarehouseId)
              AND (@Status IS NULL OR c.Status=@Status)
              AND (@Search IS NULL OR w.Name LIKE @Pattern OR c.ReasonCode LIKE @Pattern OR c.FinalDocumentNumber LIKE @Pattern)
            ORDER BY c.CreatedAt DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", (object?)query.WarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Pattern", query.Search is null ? DBNull.Value : $"%{query.Search}%");
        command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(token);
        var items = new List<InventoryPhysicalCountItem>();
        while (await reader.ReadAsync(token))
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), NormalizeCountStatus(reader.GetString(5)),
                reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetDateTimeOffset(11), reader.IsDBNull(12) ? null : reader.GetString(12)));
        return new(items, query.Page, query.PageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (decimal)query.PageSize));
    }

    public async Task<InventoryPhysicalCountDraftPage> ListDraftsAsync(InventoryUserIdentity user, InventoryPhysicalCountDraftQuery query, CancellationToken token)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.InventoryPhysicalCountLists draft
            INNER JOIN dbo.InventoryPhysicalCounts count ON count.InventoryPhysicalCountId=draft.InventoryPhysicalCountId
            INNER JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=count.WarehouseId
            WHERE count.BusinessId=@BusinessId
              AND count.Status IN (N'Open',N'Reconciling',N'Draft',N'PreCounting',N'Counting',N'Review')
              AND draft.Status<>N'Discarded'
              AND (@WarehouseId IS NULL OR count.WarehouseId=@WarehouseId)
              AND (@From IS NULL OR draft.UpdatedAt>=@From)
              AND (@To IS NULL OR draft.UpdatedAt<@To)
              AND (@Search IS NULL OR draft.Name LIKE @Pattern OR warehouse.Name LIKE @Pattern OR EXISTS(
                SELECT 1 FROM dbo.InventoryPhysicalCountLines matched
                WHERE matched.InventoryPhysicalCountListId=draft.InventoryPhysicalCountListId
                  AND (matched.ProductNameSnapshot LIKE @Pattern OR matched.ProductCodeSnapshot LIKE @Pattern)));
            SELECT count.InventoryPhysicalCountId,draft.InventoryPhysicalCountListId,draft.Name,count.WarehouseId,warehouse.Name,count.ScopeType,
              COALESCE(draft.AssignedUserId,count.CreatedByUserId),draft.Status,draft.Version,COUNT(line.ProductId),COUNT(line.PreCountQuantity),draft.UpdatedAt
            FROM dbo.InventoryPhysicalCounts count INNER JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=count.WarehouseId
            INNER JOIN dbo.InventoryPhysicalCountLists draft ON draft.InventoryPhysicalCountId=count.InventoryPhysicalCountId
            LEFT JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountListId=draft.InventoryPhysicalCountListId
            WHERE count.BusinessId=@BusinessId AND count.Status IN (N'Open',N'Reconciling',N'Draft',N'PreCounting',N'Counting',N'Review') AND draft.Status<>N'Discarded'
              AND (@WarehouseId IS NULL OR count.WarehouseId=@WarehouseId)
              AND (@From IS NULL OR draft.UpdatedAt>=@From)
              AND (@To IS NULL OR draft.UpdatedAt<@To)
              AND (@Search IS NULL OR draft.Name LIKE @Pattern OR warehouse.Name LIKE @Pattern OR EXISTS(
                SELECT 1 FROM dbo.InventoryPhysicalCountLines matched
                WHERE matched.InventoryPhysicalCountListId=draft.InventoryPhysicalCountListId
                  AND (matched.ProductNameSnapshot LIKE @Pattern OR matched.ProductCodeSnapshot LIKE @Pattern)))
            GROUP BY count.InventoryPhysicalCountId,draft.InventoryPhysicalCountListId,draft.Name,count.WarehouseId,warehouse.Name,count.ScopeType,draft.AssignedUserId,count.CreatedByUserId,draft.Status,draft.Version,draft.UpdatedAt
            ORDER BY draft.UpdatedAt DESC,draft.InventoryPhysicalCountListId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", (object?)query.WarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@From", (object?)query.From ?? DBNull.Value);
        command.Parameters.AddWithValue("@To", (object?)query.To ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Pattern", query.Search is null ? DBNull.Value : $"%{query.Search}%");
        command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(token);
        var items = new List<InventoryPhysicalCountDraftSummary>();
        while (await reader.ReadAsync(token))
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetGuid(6), NormalizeDraftStatus(reader.GetString(7)), reader.GetInt64(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetDateTimeOffset(11)));
        return new(items, query.Page, query.PageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (decimal)query.PageSize));
    }

    public async Task<InventoryPhysicalCountDetail?> GetAsync(InventoryUserIdentity user, Guid countId, CancellationToken token)
    {
        const string sql = """
            SELECT c.InventoryPhysicalCountId,c.WarehouseId,w.Name,c.ScopeType,c.ReasonCode,c.Notes,c.BaseInventorySequence,c.Status,c.CreatedByUserId,c.CreatedAt,c.StartedAt,c.ReviewStartedAt,c.ClosedAt,c.FinalInventoryOperationId,c.FinalDocumentNumber
            FROM dbo.InventoryPhysicalCounts c INNER JOIN dbo.Warehouses w ON w.WarehouseId=c.WarehouseId INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
            WHERE c.InventoryPhysicalCountId=@CountId AND c.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            SELECT InventoryPhysicalCountListId,Name,COALESCE(AssignedUserId,@FallbackUserId),Status,Version,CreatedAt,UpdatedAt,
                   CASE WHEN CountSubmittedAt IS NULL THEN N'Count' ELSE N'Recount' END
            FROM dbo.InventoryPhysicalCountLists WHERE InventoryPhysicalCountId=@CountId AND Status<>N'Discarded' ORDER BY CreatedAt,Name;
            SELECT InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase,PreCountQuantity,CountedQuantity,PendingReason,PreCountedAt,CountedAt
            FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountId=@CountId ORDER BY ProductNameSnapshot,ProductId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CountId", countId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@FallbackUserId", user.UserId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        var header = new Header(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6), NormalizeCountStatus(reader.GetString(7)), reader.GetGuid(8), reader.GetDateTimeOffset(9), NullableDate(reader,10), NullableDate(reader,11), NullableDate(reader,12), reader.IsDBNull(13)?null:reader.GetGuid(13), reader.IsDBNull(14)?null:reader.GetString(14));
        await reader.NextResultAsync(token);
        var drafts = new List<DraftState>();
        while (await reader.ReadAsync(token))
            drafts.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), NormalizeDraftStatus(reader.GetString(3)), reader.GetInt64(4), reader.GetDateTimeOffset(5), reader.GetDateTimeOffset(6), reader.GetString(7)));
        await reader.NextResultAsync(token);
        var lines = new Dictionary<Guid,List<InventoryPhysicalCountDraftLine>>();
        while (await reader.ReadAsync(token))
        {
            var draftId=reader.GetGuid(0);
            if(!lines.TryGetValue(draftId,out var values)){values=[];lines[draftId]=values;}
            values.Add(new(reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetDecimal(4),NullableDecimal(reader,5),NullableDecimal(reader,6),reader.IsDBNull(7)?null:reader.GetString(7),NullableDate(reader,8),NullableDate(reader,9)));
        }
        return new(header.Id,header.WarehouseId,header.WarehouseName,header.Scope,header.Reason,header.Notes,header.BaseSequence,header.Status,header.CreatedBy,header.CreatedAt,header.StartedAt,header.ReviewAt,header.ClosedAt,header.FinalId,header.FinalNumber,
            drafts.Select(draft=>new InventoryPhysicalCountDraft(draft.Id,draft.Name,draft.UserId,draft.Status,draft.Version,draft.CreatedAt,draft.UpdatedAt,draft.CaptureStage,lines.GetValueOrDefault(draft.Id)??[])).ToArray());
    }

    public async Task<InventoryPhysicalCountDetail> CreateDraftAsync(InventoryUserIdentity user, Guid countId, CreateInventoryPhysicalCountDraftRequest request, CancellationToken token)
    {
        await using var connection=connections.Create();
        await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var now=timeProvider.GetUtcNow();
            const string insert="""
                IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCounts WHERE InventoryPhysicalCountId=@CountId AND BusinessId=@BusinessId AND Status IN (N'Open',N'Reconciling'))
                  THROW 51202,'Drafts can only be added to an open physical count.',1;
                INSERT dbo.InventoryPhysicalCountLists(InventoryPhysicalCountListId,InventoryPhysicalCountId,Name,AssignedUserId,Status,Version,CreatedAt,UpdatedAt)
                VALUES(@DraftId,@CountId,@Name,@UserId,N'InProgress',1,@Now,@Now);
                """;
            await ExecuteAsync(connection,transaction,insert,token,P("@CountId",countId),P("@BusinessId",user.BusinessId),P("@DraftId",request.DraftId),P("@Name",request.Name),P("@UserId",user.UserId),P("@Now",now));
            foreach(var productId in request.ProductIds)
                await InsertDraftLineAsync(connection,transaction,countId,request.DraftId,productId,user.BusinessId,Guid.Empty,false,token);
            await transaction.CommitAsync(token);
            return (await GetAsync(user,countId,token))!;
        }
        catch(SqlException exception){await RollbackIfActiveAsync(transaction);throw Translate(exception);}catch{await RollbackIfActiveAsync(transaction);throw;}
    }

    public async Task<InventoryPhysicalCountDetail> SaveDraftAsync(InventoryUserIdentity user, Guid countId, Guid draftId, SaveInventoryPhysicalCountDraftRequest request, CancellationToken token)
    {
        await using var connection=connections.Create();
        await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            const string lockSql="""
                IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLists draft WITH(UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.InventoryPhysicalCounts count ON count.InventoryPhysicalCountId=draft.InventoryPhysicalCountId
                  WHERE draft.InventoryPhysicalCountListId=@DraftId AND draft.InventoryPhysicalCountId=@CountId AND count.BusinessId=@BusinessId
                    AND count.Status IN (N'Open',N'Reconciling') AND draft.AssignedUserId=@UserId)
                  THROW 51201,'Only the owner can edit this draft.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLists WHERE InventoryPhysicalCountListId=@DraftId AND Version=@Version)
                  THROW 51202,'The draft changed. Reload it before saving.',1;
                IF EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountReconciliationDrafts selected
                  INNER JOIN dbo.InventoryPhysicalCountReconciliations reconciliation ON reconciliation.InventoryPhysicalCountReconciliationId=selected.InventoryPhysicalCountReconciliationId
                  WHERE selected.InventoryPhysicalCountListId=@DraftId AND reconciliation.Status=N'Active')
                  THROW 51202,'This draft belongs to an active reconciliation. Prepare a new draft instead.',1;
                DECLARE @Sequence BIGINT=(SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId);
                SELECT count.WarehouseId,draft.CountSubmittedAt,@Sequence
                FROM dbo.InventoryPhysicalCountLists draft
                INNER JOIN dbo.InventoryPhysicalCounts count ON count.InventoryPhysicalCountId=draft.InventoryPhysicalCountId
                WHERE draft.InventoryPhysicalCountListId=@DraftId;
                """;
            long sequence;
            Guid warehouseId;
            DateTimeOffset? recountStartedAt;
            await using(var command=new SqlCommand(lockSql,connection,transaction))
            {
                command.Parameters.AddWithValue("@DraftId",draftId);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@Version",request.Version);
                await using var reader=await command.ExecuteReaderAsync(token);
                await reader.ReadAsync(token);
                warehouseId=reader.GetGuid(0);
                recountStartedAt=reader.IsDBNull(1)?null:reader.GetDateTimeOffset(1);
                sequence=reader.GetInt64(2);
            }
            if(recountStartedAt is not null && request.CaptureStage!="Recount")
                throw new InventoryConflictException("The product scope is locked because recounting already started.");
            var now=timeProvider.GetUtcNow();
            foreach(var line in request.Lines)
            {
                if(request.CaptureStage=="Count")
                {
                    const string existsSql="SELECT COUNT(*) FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountListId=@DraftId AND ProductId=@ProductId;";
                    await using var existsCommand=new SqlCommand(existsSql,connection,transaction);
                    existsCommand.Parameters.AddWithValue("@DraftId",draftId);
                    existsCommand.Parameters.AddWithValue("@ProductId",line.ProductId);
                    if(Convert.ToInt32(await existsCommand.ExecuteScalarAsync(token))==0)
                        await InsertDraftLineAsync(connection,transaction,countId,draftId,line.ProductId,user.BusinessId,warehouseId,true,token);
                }
                const string update="""
                    UPDATE dbo.InventoryPhysicalCountLines SET
                      PreCountedByUserId=CASE WHEN @Initial IS NULL THEN NULL ELSE @UserId END,
                      PreCountedAt=CASE WHEN @Initial IS NULL THEN NULL WHEN PreCountQuantity=@Initial THEN PreCountedAt ELSE @Now END,
                      PreCountedAtProcessingSequence=CASE WHEN @Initial IS NULL THEN NULL WHEN PreCountQuantity=@Initial THEN PreCountedAtProcessingSequence ELSE @Sequence END,
                      PreCountQuantity=@Initial,
                      CountedByUserId=CASE WHEN @Verification IS NULL THEN NULL ELSE @UserId END,
                      CountedAt=CASE WHEN @Verification IS NULL THEN NULL WHEN CountedQuantity=@Verification THEN CountedAt ELSE @Now END,
                      CountedAtProcessingSequence=CASE WHEN @Verification IS NULL THEN NULL WHEN CountedQuantity=@Verification THEN CountedAtProcessingSequence ELSE @Sequence END,
                      CountedQuantity=@Verification,
                      PendingReason=CASE WHEN @Initial IS NULL THEN @PendingReason ELSE NULL END
                    WHERE InventoryPhysicalCountListId=@DraftId AND ProductId=@ProductId;
                    IF @@ROWCOUNT=0 THROW 51201,'A product does not belong to this draft.',1;
                    """;
                await ExecuteAsync(connection,transaction,update,token,P("@Initial",line.InitialQuantity),P("@Verification",line.VerificationQuantity),P("@PendingReason",line.PendingReason),P("@UserId",user.UserId),P("@Now",now),P("@Sequence",sequence),P("@DraftId",draftId),P("@ProductId",line.ProductId));
            }
            const string validateCompleteScope="""
                IF (SELECT COUNT(*) FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountListId=@DraftId)<>@LineCount
                  THROW 51201,'Every draft product must be included when saving.',1;
                """;
            await ExecuteAsync(connection,transaction,validateCompleteScope,token,P("@DraftId",draftId),P("@LineCount",request.Lines.Count));
            const string finish="""
                UPDATE dbo.InventoryPhysicalCountLists SET Name=@Name,Status=@Status,Version=Version+1,UpdatedAt=@Now,
                  PreCountSubmittedAt=CASE WHEN @Status=N'Ready' THEN @Now ELSE NULL END,
                  CountSubmittedAt=CASE WHEN @CaptureStage=N'Recount' THEN COALESCE(CountSubmittedAt,@Now) ELSE CountSubmittedAt END
                WHERE InventoryPhysicalCountListId=@DraftId;
                """;
            await ExecuteAsync(connection,transaction,finish,token,P("@Name",request.Name),P("@Status",request.ReadyForReconciliation?"Ready":"InProgress"),P("@CaptureStage",request.CaptureStage),P("@Now",now),P("@DraftId",draftId));
            await transaction.CommitAsync(token);
            return (await GetAsync(user,countId,token))!;
        }
        catch(SqlException exception){await RollbackIfActiveAsync(transaction);throw Translate(exception);}catch{await RollbackIfActiveAsync(transaction);throw;}
    }

    public async Task<InventoryReconciliationDetail> PrepareReconciliationAsync(InventoryUserIdentity user, Guid countId, PrepareInventoryReconciliationRequest request, CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            const string validate="""
                IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCounts WITH(UPDLOCK,HOLDLOCK) WHERE InventoryPhysicalCountId=@CountId AND BusinessId=@BusinessId AND Status IN (N'Open',N'Reconciling'))
                  THROW 51202,'Only an open physical count can be reconciled.',1;
                """;
            await ExecuteAsync(connection,transaction,validate,token,P("@CountId",countId),P("@BusinessId",user.BusinessId));
            foreach(var draft in request.Drafts)
            {
                const string validateDraft="""
                    IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLists WHERE InventoryPhysicalCountListId=@DraftId AND InventoryPhysicalCountId=@CountId AND Status=N'Ready' AND Version=@Version)
                      THROW 51202,'A selected draft is not ready or changed. Reload before reconciling.',1;
                    """;
                await ExecuteAsync(connection,transaction,validateDraft,token,P("@DraftId",draft.DraftId),P("@CountId",countId),P("@Version",draft.Version));
            }
            var reconciliationId=Guid.NewGuid();var now=timeProvider.GetUtcNow();
            const string create="""
                DECLARE @Sequence BIGINT=(SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId);
                UPDATE dbo.InventoryPhysicalCountReconciliations SET Status=N'Superseded' WHERE InventoryPhysicalCountId=@CountId AND Status=N'Active';
                INSERT dbo.InventoryPhysicalCountReconciliations(InventoryPhysicalCountReconciliationId,InventoryPhysicalCountId,SnapshotInventorySequence,Status,CreatedByUserId,CreatedAt,CountedProductCount,UncountedProductCount)
                VALUES(@ReconciliationId,@CountId,@Sequence,N'Active',@UserId,@Now,0,0);
                UPDATE dbo.InventoryPhysicalCounts SET Status=N'Reconciling',ReviewStartedAt=@Now WHERE InventoryPhysicalCountId=@CountId;
                """;
            await ExecuteAsync(connection,transaction,create,token,P("@ReconciliationId",reconciliationId),P("@CountId",countId),P("@BusinessId",user.BusinessId),P("@UserId",user.UserId),P("@Now",now));
            foreach(var draft in request.Drafts)
            {
                const string selectDraft="INSERT dbo.InventoryPhysicalCountReconciliationDrafts(InventoryPhysicalCountReconciliationId,InventoryPhysicalCountListId,DraftVersion) VALUES(@ReconciliationId,@DraftId,@Version);";
                await ExecuteAsync(connection,transaction,selectDraft,token,P("@ReconciliationId",reconciliationId),P("@DraftId",draft.DraftId),P("@Version",draft.Version));
            }
            const string totals="""
                UPDATE reconciliation SET
                  CountedProductCount=(SELECT COUNT(DISTINCT line.ProductId) FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND line.PreCountQuantity IS NOT NULL),
                  UncountedProductCount=(SELECT COUNT(*) FROM (
                    SELECT line.ProductId FROM dbo.InventoryPhysicalCountLines line WHERE line.InventoryPhysicalCountId=@CountId GROUP BY line.ProductId
                    UNION
                    SELECT product.ProductId FROM dbo.InventoryPhysicalCounts countHeader
                    INNER JOIN dbo.Products product ON product.BusinessId=countHeader.BusinessId AND product.IsActive=1 AND product.ManageStock=1
                    LEFT JOIN dbo.ProductLinks link ON link.BusinessId=product.BusinessId AND link.ChildProductId=product.ProductId AND link.SharesInventory=1 AND link.IsActive=1
                    WHERE countHeader.InventoryPhysicalCountId=@CountId AND countHeader.ScopeType=N'General' AND link.ProductLinkId IS NULL
                  ) scope WHERE NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND line.ProductId=scope.ProductId AND line.PreCountQuantity IS NOT NULL))
                FROM dbo.InventoryPhysicalCountReconciliations reconciliation WHERE reconciliation.InventoryPhysicalCountReconciliationId=@ReconciliationId;
                """;
            await ExecuteAsync(connection,transaction,totals,token,P("@ReconciliationId",reconciliationId),P("@CountId",countId));
            await transaction.CommitAsync(token);
            return (await GetReconciliationAsync(user,countId,token))!;
        }
        catch(SqlException exception){await RollbackIfActiveAsync(transaction);throw Translate(exception);}catch{await RollbackIfActiveAsync(transaction);throw;}
    }

    public async Task<InventoryReconciliationDetail?> GetReconciliationAsync(InventoryUserIdentity user, Guid countId, CancellationToken token)
    {
        const string sql="""
            SELECT TOP(1) r.InventoryPhysicalCountReconciliationId,r.SnapshotInventorySequence,r.Status,r.CreatedAt,r.CreatedByUserId,c.WarehouseId,c.BaseInventorySequence,
              CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLists draft ON draft.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId WHERE selected.InventoryPhysicalCountReconciliationId=r.InventoryPhysicalCountReconciliationId AND selected.DraftVersion<>draft.Version) THEN 1 ELSE 0 END AS BIT),
              r.CountedApplicationStatus,r.CountedDocumentId,r.CountedDocumentNumber,r.UncountedApplicationStatus,r.UncountedDocumentId,r.UncountedDocumentNumber
            FROM dbo.InventoryPhysicalCountReconciliations r INNER JOIN dbo.InventoryPhysicalCounts c ON c.InventoryPhysicalCountId=r.InventoryPhysicalCountId
            WHERE r.InventoryPhysicalCountId=@CountId AND c.BusinessId=@BusinessId AND r.Status IN (N'Active',N'Applied')
            ORDER BY CASE r.Status WHEN N'Active' THEN 0 ELSE 1 END,r.CreatedAt DESC;
            """;
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var command=new SqlCommand(sql,connection);
        command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
        await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;
        var header=new ReconciliationHeader(reader.GetGuid(0),reader.GetInt64(1),reader.GetString(2),reader.GetDateTimeOffset(3),reader.GetGuid(4),reader.GetGuid(5),reader.GetInt64(6),reader.GetBoolean(7),reader.IsDBNull(8)?null:reader.GetString(8),reader.IsDBNull(9)?null:reader.GetGuid(9),reader.IsDBNull(10)?null:reader.GetString(10),reader.IsDBNull(11)?null:reader.GetString(11),reader.IsDBNull(12)?null:reader.GetGuid(12),reader.IsDBNull(13)?null:reader.GetString(13));
        await reader.DisposeAsync();
        return await ReadReconciliationAsync(connection,user,countId,header,token);
    }

    private async Task<InventoryReconciliationDetail> ReadReconciliationAsync(SqlConnection connection,InventoryUserIdentity user,Guid countId,ReconciliationHeader header,CancellationToken token)
    {
        const string sql="""
            SELECT draft.InventoryPhysicalCountListId,draft.Name,COALESCE(draft.AssignedUserId,@UserId),selected.DraftVersion,
              SUM(CASE WHEN line.PreCountQuantity IS NOT NULL THEN 1 ELSE 0 END),SUM(CASE WHEN line.PreCountQuantity IS NULL THEN 1 ELSE 0 END)
            FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLists draft ON draft.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId
            LEFT JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountListId=draft.InventoryPhysicalCountListId
            WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId
            GROUP BY draft.InventoryPhysicalCountListId,draft.Name,draft.AssignedUserId,selected.DraftVersion ORDER BY draft.Name;
            ;WITH reconciliationScope AS (
              SELECT scope.ProductId,MAX(scope.ProductCodeSnapshot) ProductCodeSnapshot,MAX(scope.ProductNameSnapshot) ProductNameSnapshot,MAX(scope.SystemQuantityAtBase) SystemQuantityAtBase
              FROM dbo.InventoryPhysicalCountLines scope WHERE scope.InventoryPhysicalCountId=@CountId GROUP BY scope.ProductId
              UNION ALL
              SELECT product.ProductId,COALESCE(product.ProductCode,product.Sku,product.Reference,N''),product.Name,
                COALESCE((SELECT SUM(movement.QuantityChange) FROM dbo.InventoryMovements movement WHERE movement.BusinessId=@BusinessId AND movement.WarehouseId=@WarehouseId AND movement.ProductId=product.ProductId AND movement.ProcessingSequence<=@BaseSequence),0)
              FROM dbo.InventoryPhysicalCounts countHeader
              INNER JOIN dbo.Products product ON product.BusinessId=countHeader.BusinessId AND product.IsActive=1 AND product.ManageStock=1
              LEFT JOIN dbo.ProductLinks link ON link.BusinessId=product.BusinessId AND link.ChildProductId=product.ProductId AND link.SharesInventory=1 AND link.IsActive=1
              WHERE countHeader.InventoryPhysicalCountId=@CountId AND countHeader.ScopeType=N'General' AND link.ProductLinkId IS NULL
                AND NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLines existing WHERE existing.InventoryPhysicalCountId=@CountId AND existing.ProductId=product.ProductId)
            )
            SELECT scope.ProductId,MAX(scope.ProductCodeSnapshot),MAX(scope.ProductNameSnapshot),
              MAX(scope.SystemQuantityAtBase)+COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=@BusinessId AND m.WarehouseId=@WarehouseId AND m.ProductId=scope.ProductId AND m.ProcessingSequence>@BaseSequence AND m.ProcessingSequence<=@SnapshotSequence),0),
              CASE WHEN @IncludeCosts=1 THEN price.CostBasisAmount END,
              CASE WHEN @IncludeCosts=1 THEN balance.AverageUnitCost END
            FROM reconciliationScope scope
            LEFT JOIN dbo.ProductPrices price ON price.BusinessId=@BusinessId AND price.ProductId=scope.ProductId AND price.IsActive=1
            LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId AND balance.ProductId=scope.ProductId
            GROUP BY scope.ProductId,price.CostBasisAmount,balance.AverageUnitCost;
            SELECT line.ProductId,draft.InventoryPhysicalCountListId,draft.Name,COALESCE(draft.AssignedUserId,@UserId),line.PreCountQuantity,line.CountedQuantity
            FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLists draft ON draft.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId
            INNER JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountListId=draft.InventoryPhysicalCountListId
            WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND line.PreCountQuantity IS NOT NULL;
            """;
        await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@UserId",user.UserId);command.Parameters.AddWithValue("@WarehouseId",header.WarehouseId);command.Parameters.AddWithValue("@BaseSequence",header.BaseSequence);command.Parameters.AddWithValue("@SnapshotSequence",header.Snapshot);command.Parameters.AddWithValue("@ReconciliationId",header.Id);command.Parameters.AddWithValue("@IncludeCosts",user.Permissions.Contains(InventoryPermissionCodes.ReadCosts));
        await using var reader=await command.ExecuteReaderAsync(token);
        var drafts=new List<InventoryReconciliationDraft>();
        while(await reader.ReadAsync(token))drafts.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetGuid(2),reader.GetInt64(3),reader.IsDBNull(4)?0:reader.GetInt32(4),reader.IsDBNull(5)?0:reader.GetInt32(5)));
        await reader.NextResultAsync(token);var products=new Dictionary<Guid,ProductState>();
        while(await reader.ReadAsync(token))products[reader.GetGuid(0)]=new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),NullableDecimal(reader,4),NullableDecimal(reader,5));
        await reader.NextResultAsync(token);var sources=new Dictionary<Guid,List<SourceState>>();
        while(await reader.ReadAsync(token)){var productId=reader.GetGuid(0);if(!sources.TryGetValue(productId,out var list)){list=[];sources[productId]=list;}list.Add(new(reader.GetGuid(1),reader.GetString(2),reader.GetGuid(3),reader.GetDecimal(4),NullableDecimal(reader,5)));}
        var resultProducts=products.Values.OrderBy(product=>product.Name).Select(product=>BuildProduct(product,sources.GetValueOrDefault(product.Id)??[])).ToArray();
        return new(header.Id,countId,header.Snapshot,header.Status,header.CreatedAt,header.CreatedBy,header.Stale,header.CountedStatus,header.CountedDocumentId,header.CountedDocumentNumber,header.UncountedStatus,header.UncountedDocumentId,header.UncountedDocumentNumber,drafts,resultProducts);
    }

    public async Task<InventoryPhysicalCountDetail> SaveReconciliationDraftAsync(InventoryUserIdentity user,Guid countId,Guid reconciliationId,SaveInventoryReconciliationDraftRequest request,CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var now=timeProvider.GetUtcNow();
            const string sql="""
                IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountReconciliations r INNER JOIN dbo.InventoryPhysicalCounts c ON c.InventoryPhysicalCountId=r.InventoryPhysicalCountId WHERE r.InventoryPhysicalCountReconciliationId=@ReconciliationId AND r.InventoryPhysicalCountId=@CountId AND r.Status=N'Active' AND c.BusinessId=@BusinessId)
                  THROW 51202,'The active inventory reconciliation was not found.',1;
                DECLARE @Sequence BIGINT=(SELECT LastCompletedSequence FROM dbo.BusinessProcessingCursors WHERE BusinessId=@BusinessId);
                INSERT dbo.InventoryPhysicalCountLists(InventoryPhysicalCountListId,InventoryPhysicalCountId,Name,AssignedUserId,Status,Version,CreatedAt,UpdatedAt)
                VALUES(@DraftId,@CountId,@Name,@UserId,CASE WHEN @Section=N'Counted' THEN N'Ready' ELSE N'InProgress' END,1,@Now,@Now);
                IF @Section=N'Counted'
                BEGIN
                  INSERT dbo.InventoryPhysicalCountLines(InventoryPhysicalCountId,InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase,PreCountQuantity,PreCountedByUserId,PreCountedAt,PreCountedAtProcessingSequence)
                  SELECT @CountId,@DraftId,line.ProductId,MAX(line.ProductCodeSnapshot),MAX(line.ProductNameSnapshot),MAX(line.SystemQuantityAtBase),SUM(COALESCE(line.CountedQuantity,line.PreCountQuantity)),@UserId,@Now,@Sequence
                  FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId
                  WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND line.PreCountQuantity IS NOT NULL GROUP BY line.ProductId;
                END
                ELSE
                BEGIN
                  ;WITH reconciliationScope AS (
                    SELECT scope.ProductId,MAX(scope.ProductCodeSnapshot) ProductCodeSnapshot,MAX(scope.ProductNameSnapshot) ProductNameSnapshot,MAX(scope.SystemQuantityAtBase) SystemQuantityAtBase
                    FROM dbo.InventoryPhysicalCountLines scope WHERE scope.InventoryPhysicalCountId=@CountId GROUP BY scope.ProductId
                    UNION ALL
                    SELECT product.ProductId,COALESCE(product.ProductCode,product.Sku,product.Reference,N''),product.Name,
                      COALESCE((SELECT SUM(movement.QuantityChange) FROM dbo.InventoryMovements movement WHERE movement.BusinessId=@BusinessId AND movement.WarehouseId=countHeader.WarehouseId AND movement.ProductId=product.ProductId AND movement.ProcessingSequence<=countHeader.BaseInventorySequence),0)
                    FROM dbo.InventoryPhysicalCounts countHeader
                    INNER JOIN dbo.Products product ON product.BusinessId=countHeader.BusinessId AND product.IsActive=1 AND product.ManageStock=1
                    LEFT JOIN dbo.ProductLinks link ON link.BusinessId=product.BusinessId AND link.ChildProductId=product.ProductId AND link.SharesInventory=1 AND link.IsActive=1
                    WHERE countHeader.InventoryPhysicalCountId=@CountId AND countHeader.ScopeType=N'General' AND link.ProductLinkId IS NULL
                      AND NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLines existing WHERE existing.InventoryPhysicalCountId=@CountId AND existing.ProductId=product.ProductId)
                  )
                  INSERT dbo.InventoryPhysicalCountLines(InventoryPhysicalCountId,InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase)
                  SELECT @CountId,@DraftId,scope.ProductId,MAX(scope.ProductCodeSnapshot),MAX(scope.ProductNameSnapshot),MAX(scope.SystemQuantityAtBase)
                  FROM reconciliationScope scope WHERE NOT EXISTS(
                    SELECT 1 FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLines counted ON counted.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId
                    WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND counted.ProductId=scope.ProductId AND counted.PreCountQuantity IS NOT NULL)
                  GROUP BY scope.ProductId;
                END
                IF @@ROWCOUNT=0 THROW 51201,'This reconciliation section has no products.',1;
                """;
            await ExecuteAsync(connection,transaction,sql,token,P("@ReconciliationId",reconciliationId),P("@CountId",countId),P("@BusinessId",user.BusinessId),P("@DraftId",request.DraftId),P("@Name",request.Name),P("@UserId",user.UserId),P("@Section",request.Section),P("@Now",now));
            await transaction.CommitAsync(token);return(await GetAsync(user,countId,token))!;
        }
        catch(SqlException exception){await RollbackIfActiveAsync(transaction);throw Translate(exception);}catch{await RollbackIfActiveAsync(transaction);throw;}
    }

    public async Task<InventoryPhysicalCountClosePreparation> PrepareApplyAsync(InventoryUserIdentity user,Guid countId,Guid reconciliationId,string section,CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var documentId=Guid.NewGuid();
            var statusColumn=section=="Counted"?"CountedApplicationStatus":"UncountedApplicationStatus";
            var documentColumn=section=="Counted"?"CountedDocumentId":"UncountedDocumentId";
            var countColumn=section=="Counted"?"CountedProductCount":"UncountedProductCount";
            var headerSql=$"""
                IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountReconciliations r WITH(UPDLOCK,HOLDLOCK) INNER JOIN dbo.InventoryPhysicalCounts c ON c.InventoryPhysicalCountId=r.InventoryPhysicalCountId WHERE r.InventoryPhysicalCountReconciliationId=@ReconciliationId AND r.InventoryPhysicalCountId=@CountId AND r.Status=N'Active' AND c.BusinessId=@BusinessId)
                  THROW 51202,'The active inventory reconciliation was not found.',1;
                IF EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLists draft ON draft.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND selected.DraftVersion<>draft.Version)
                  THROW 51202,'A selected draft changed. Reconcile again before applying.',1;
                IF (SELECT {countColumn} FROM dbo.InventoryPhysicalCountReconciliations WHERE InventoryPhysicalCountReconciliationId=@ReconciliationId)=0
                  THROW 51201,'This reconciliation section has no products.',1;
                UPDATE dbo.InventoryPhysicalCountReconciliations SET {documentColumn}=COALESCE({documentColumn},@DocumentId),{statusColumn}=N'Processing' WHERE InventoryPhysicalCountReconciliationId=@ReconciliationId AND ({statusColumn} IS NULL OR {statusColumn}=N'Processing');
                IF @@ROWCOUNT=0 THROW 51202,'This reconciliation section was already applied.',1;
                SELECT c.BusinessId,c.WarehouseId,c.ReasonCode,c.Notes,r.{documentColumn},processingCursor.LastCompletedSequence
                FROM dbo.InventoryPhysicalCountReconciliations r INNER JOIN dbo.InventoryPhysicalCounts c ON c.InventoryPhysicalCountId=r.InventoryPhysicalCountId INNER JOIN dbo.BusinessProcessingCursors processingCursor ON processingCursor.BusinessId=c.BusinessId
                WHERE r.InventoryPhysicalCountReconciliationId=@ReconciliationId;
                """;
            Guid business,warehouse,finalId;string reason;string?notes;long currentSequence;
            await using(var command=new SqlCommand(headerSql,connection,transaction)){command.Parameters.AddWithValue("@ReconciliationId",reconciliationId);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",user.BusinessId);command.Parameters.AddWithValue("@DocumentId",documentId);await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);business=reader.GetGuid(0);warehouse=reader.GetGuid(1);reason=reader.GetString(2);notes=reader.IsDBNull(3)?null:reader.GetString(3);finalId=reader.GetGuid(4);currentSequence=reader.GetInt64(5);}
            var lines=new List<InventoryPhysicalCountCloseLine>();
            var lineSql=section=="Counted"?"""
                WITH candidates AS (
                  SELECT line.ProductId,SUM(COALESCE(line.CountedQuantity,line.PreCountQuantity)) Quantity,
                    MAX(CASE WHEN line.CountedQuantity IS NOT NULL THEN line.CountedAtProcessingSequence ELSE line.PreCountedAtProcessingSequence END) LastCaptureSequence
                  FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLines line ON line.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId
                  WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND line.PreCountQuantity IS NOT NULL GROUP BY line.ProductId)
                SELECT candidate.ProductId,candidate.Quantity,candidate.Quantity+COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=@BusinessId AND m.WarehouseId=@WarehouseId AND m.ProductId=candidate.ProductId AND m.ProcessingSequence>candidate.LastCaptureSequence AND m.ProcessingSequence<=@CurrentSequence),0)
                FROM candidates candidate;
                """:"""
                SELECT scope.ProductId,CAST(0 AS DECIMAL(19,6)),CAST(0 AS DECIMAL(19,6)) FROM (
                  SELECT line.ProductId FROM dbo.InventoryPhysicalCountLines line WHERE line.InventoryPhysicalCountId=@CountId GROUP BY line.ProductId
                  UNION
                  SELECT product.ProductId FROM dbo.InventoryPhysicalCounts countHeader
                  INNER JOIN dbo.Products product ON product.BusinessId=countHeader.BusinessId AND product.IsActive=1 AND product.ManageStock=1
                  LEFT JOIN dbo.ProductLinks link ON link.BusinessId=product.BusinessId AND link.ChildProductId=product.ProductId AND link.SharesInventory=1 AND link.IsActive=1
                  WHERE countHeader.InventoryPhysicalCountId=@CountId AND countHeader.ScopeType=N'General' AND link.ProductLinkId IS NULL
                ) scope
                WHERE NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountReconciliationDrafts selected INNER JOIN dbo.InventoryPhysicalCountLines counted ON counted.InventoryPhysicalCountListId=selected.InventoryPhysicalCountListId WHERE selected.InventoryPhysicalCountReconciliationId=@ReconciliationId AND counted.ProductId=scope.ProductId AND counted.PreCountQuantity IS NOT NULL)
                GROUP BY scope.ProductId;
                """;
            await using(var command=new SqlCommand(lineSql,connection,transaction)){command.Parameters.AddWithValue("@ReconciliationId",reconciliationId);command.Parameters.AddWithValue("@CountId",countId);command.Parameters.AddWithValue("@BusinessId",business);command.Parameters.AddWithValue("@WarehouseId",warehouse);command.Parameters.AddWithValue("@CurrentSequence",currentSequence);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))lines.Add(new(reader.GetGuid(0),reader.GetDecimal(1),reader.GetDecimal(2)));}
            if(lines.Count==0)throw new InventoryValidationException("This reconciliation section has no products.");
            await transaction.CommitAsync(token);return new(countId,business,warehouse,reason,notes,finalId,section,lines);
        }
        catch(SqlException exception){await RollbackIfActiveAsync(transaction);throw Translate(exception);}catch{await RollbackIfActiveAsync(transaction);throw;}
    }

    public async Task<InventoryPhysicalCountDetail> RecordApplyAcceptanceAsync(InventoryUserIdentity user,Guid countId,Guid reconciliationId,string section,InventoryOperationAcceptance acceptance,CancellationToken token)
    {
        var documentColumn=section=="Counted"?"CountedDocumentId":"UncountedDocumentId";var numberColumn=section=="Counted"?"CountedDocumentNumber":"UncountedDocumentNumber";
        var sql=$"UPDATE dbo.InventoryPhysicalCountReconciliations SET {numberColumn}=@Number WHERE InventoryPhysicalCountReconciliationId=@ReconciliationId AND {documentColumn}=@DocumentId; IF @@ROWCOUNT=0 THROW 51202,'Inventory reconciliation application state is inconsistent.',1;";
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var command=new SqlCommand(sql,connection);command.Parameters.AddWithValue("@Number",acceptance.DocumentNumber);command.Parameters.AddWithValue("@ReconciliationId",reconciliationId);command.Parameters.AddWithValue("@DocumentId",acceptance.DocumentId);try{await command.ExecuteNonQueryAsync(token);}catch(SqlException exception){throw Translate(exception);}return(await GetAsync(user,countId,token))!;
    }

    private static InventoryReconciliationProduct BuildProduct(ProductState product,IReadOnlyList<SourceState> sourceStates)
    {
        var sources=sourceStates.Select(source=>new InventoryReconciliationSource(source.DraftId,source.DraftName,source.OwnerId,source.Initial,source.Verification,source.Verification??source.Initial)).ToArray();
        var proposed=sources.Length==0?(decimal?)null:sources.Sum(source=>source.FinalQuantity);
        return new(product.Id,product.Code,product.Name,sources.Length==0?"Uncounted":"Counted",proposed,product.SystemQuantity,product.UnitCost,product.AverageUnitCost,sources);
    }

    private static async Task InsertDraftLineAsync(SqlConnection connection,SqlTransaction transaction,Guid countId,Guid draftId,Guid productId,Guid businessId,Guid warehouseId,bool rejectOtherActiveCount,CancellationToken token)
    {
        var sql=rejectOtherActiveCount?"""
            IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1 AND ManageStock=1)
              THROW 51201,'A selected product is not inventory enabled.',1;
            IF EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLines line INNER JOIN dbo.InventoryPhysicalCounts count ON count.InventoryPhysicalCountId=line.InventoryPhysicalCountId
              WHERE count.BusinessId=@BusinessId AND count.WarehouseId=@WarehouseId AND line.ProductId=@ProductId AND count.InventoryPhysicalCountId<>@CountId
                AND count.Status IN (N'Open',N'Reconciling',N'Closing',N'Draft',N'PreCounting',N'Counting',N'Review'))
              THROW 51202,'A selected product already belongs to another active physical count.',1;
            INSERT dbo.InventoryPhysicalCountLines(InventoryPhysicalCountId,InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase)
            SELECT @CountId,@DraftId,p.ProductId,COALESCE(p.ProductCode,p.Sku,p.Reference,N''),p.Name,COALESCE(balance.QuantityOnHand,0)
            FROM dbo.Products p LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=p.BusinessId AND balance.WarehouseId=@WarehouseId AND balance.ProductId=p.ProductId
            WHERE p.ProductId=@ProductId AND p.BusinessId=@BusinessId;
            """:"""
            IF NOT EXISTS(SELECT 1 FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountId=@CountId AND ProductId=@ProductId)
              THROW 51201,'A draft can only contain products in the physical count scope.',1;
            INSERT dbo.InventoryPhysicalCountLines(InventoryPhysicalCountId,InventoryPhysicalCountListId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase)
            SELECT TOP(1) InventoryPhysicalCountId,@DraftId,ProductId,ProductCodeSnapshot,ProductNameSnapshot,SystemQuantityAtBase FROM dbo.InventoryPhysicalCountLines WHERE InventoryPhysicalCountId=@CountId AND ProductId=@ProductId;
            """;
        await ExecuteAsync(connection,transaction,sql,token,P("@CountId",countId),P("@DraftId",draftId),P("@ProductId",productId),P("@BusinessId",businessId),P("@WarehouseId",warehouseId));
    }

    private static async Task ExecuteAsync(SqlConnection connection,SqlTransaction transaction,string sql,CancellationToken token,params SqlParameter[] parameters)
    {await using var command=new SqlCommand(sql,connection,transaction);command.Parameters.AddRange(parameters);await command.ExecuteNonQueryAsync(token);}
    private static async Task RollbackIfActiveAsync(SqlTransaction transaction){if(transaction.Connection is not null)await transaction.RollbackAsync(CancellationToken.None);}
    private static SqlParameter P(string name,object? value)=>new(name,value??DBNull.Value);
    private static Exception Translate(SqlException exception)=>exception.Number==51202?new InventoryConflictException(exception.Message):exception.Number==51201?new InventoryValidationException(exception.Message):exception;
    private static DateTimeOffset? NullableDate(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDateTimeOffset(ordinal);
    private static decimal? NullableDecimal(SqlDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetDecimal(ordinal);
    private static string NormalizeCountStatus(string status)=>status switch{"Draft" or "PreCounting" or "Counting"=>"Open","Review"=>"Reconciling",_=>status};
    private static string NormalizeDraftStatus(string status)=>status switch{"Pending" or "PreCounting" or "Counting"=>"InProgress","PreCounted" or "Counted"=>"Ready",_=>status};
    private sealed record Header(Guid Id,Guid WarehouseId,string WarehouseName,string Scope,string Reason,string? Notes,long BaseSequence,string Status,Guid CreatedBy,DateTimeOffset CreatedAt,DateTimeOffset? StartedAt,DateTimeOffset? ReviewAt,DateTimeOffset? ClosedAt,Guid? FinalId,string? FinalNumber);
    private sealed record DraftState(Guid Id,string Name,Guid UserId,string Status,long Version,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,string CaptureStage);
    private sealed record ProductState(Guid Id,string Code,string Name,decimal SystemQuantity,decimal? UnitCost,decimal? AverageUnitCost);
    private sealed record SourceState(Guid DraftId,string DraftName,Guid OwnerId,decimal Initial,decimal? Verification);
    private sealed record ReconciliationHeader(Guid Id,long Snapshot,string Status,DateTimeOffset CreatedAt,Guid CreatedBy,Guid WarehouseId,long BaseSequence,bool Stale,string? CountedStatus,Guid? CountedDocumentId,string? CountedDocumentNumber,string? UncountedStatus,Guid? UncountedDocumentId,string? UncountedDocumentNumber);
}
