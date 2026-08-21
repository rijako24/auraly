using System.Data;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Dispatching;

public sealed class SqlDispatchStore(DispatchingSqlConnectionFactory connections) : IDispatchStore
{
    public async Task<DispatchPage> PageAsync(DispatchActorIdentity actor, DispatchQuery query, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        const string source = """
          FROM dbo.Dispatches d
          OUTER APPLY(SELECT COUNT(*) DocumentCount FROM dbo.DispatchSourceDocuments x WHERE x.DispatchId=d.DispatchId) docs
          OUTER APPLY(SELECT COUNT(*) LineCount,COALESCE(SUM(AssignedQuantity),0) Expected,COALESCE(SUM(VerifiedQuantity),0) Verified,COALESCE(SUM(ShortageQuantity),0) Shortage FROM dbo.DispatchLines x WHERE x.DispatchId=d.DispatchId) lines
          WHERE d.TenantId=@TenantId AND d.BusinessId=@BusinessId
            AND (@ReadAll=1 OR d.DriverUserId=@UserId)
            AND (@Status IS NULL OR d.Status=@Status)
            AND (@From IS NULL OR d.ScheduledDate>=@From) AND (@To IS NULL OR d.ScheduledDate<=@To)
            AND (@Search IS NULL OR d.DispatchNumber LIKE '%'+@Search+'%' OR d.DriverName LIKE '%'+@Search+'%' OR d.VehiclePlate LIKE '%'+@Search+'%'
                 OR EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments sd WHERE sd.DispatchId=d.DispatchId AND (sd.DocumentNumberSnapshot LIKE '%'+@Search+'%' OR sd.CustomerNameSnapshot LIKE '%'+@Search+'%')))
        """;
        await using var count = new SqlCommand("SELECT COUNT(*) "+source, connection); AddQuery(count, actor, query);
        var total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        await using var command = new SqlCommand("""
          SELECT d.DispatchId,d.DispatchNumber,d.ScheduledDate,d.DriverName,d.VehiclePlate,d.Status,docs.DocumentCount,lines.LineCount,lines.Expected,lines.Verified,lines.Shortage,d.UpdatedAt,d.RowVersion
        """+source+" ORDER BY d.ScheduledDate DESC,d.DispatchNumber DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", connection);
        AddQuery(command, actor, query); command.Parameters.AddWithValue("@Skip",(query.Page-1)*query.PageSize); command.Parameters.AddWithValue("@Take",query.PageSize);
        var items=new List<DispatchListItem>(); await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0),reader.GetString(1),DateOnly.FromDateTime(reader.GetDateTime(2)),reader.GetString(3),NullableString(reader,4),reader.GetString(5),reader.GetInt32(6),reader.GetInt32(7),reader.GetDecimal(8),reader.GetDecimal(9),reader.GetDecimal(10),reader.GetDateTimeOffset(11),Version(reader,12)));
        return new(items,query.Page,query.PageSize,total,total==0?0:(int)Math.Ceiling(total/(decimal)query.PageSize));
    }

    public async Task<DispatchOptions> OptionsAsync(DispatchActorIdentity actor, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        var warehouses=new List<DispatchWarehouseOption>();
        await using(var command=new SqlCommand("SELECT WarehouseId,Code,Name FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND IsActive=1 AND UseForSales=1 ORDER BY Name",connection))
        { Scope(command,actor); await using var reader=await command.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct)) warehouses.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2))); }
        var routes=new List<DispatchRouteOption>();
        await using(var command=new SqlCommand("""
          SELECT r.RouteId,r.Code,r.Name,p.DisplayName FROM dbo.SalesRoutes r INNER JOIN dbo.CommerceSellers s ON s.SellerId=r.SellerId INNER JOIN dbo.Parties p ON p.PartyId=s.PartyId
          WHERE r.BusinessId=@BusinessId AND r.IsActive=1 ORDER BY r.Name
        """,connection))
        { Scope(command,actor); await using var reader=await command.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct)) routes.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3))); }
        var drivers=new List<DispatchDriverOption>();
        await using(var command=new SqlCommand("""
          SELECT DISTINCT u.UserId,CONCAT(u.FirstName,N' ',u.LastName)
          FROM dbo.AppUsers u INNER JOIN dbo.UserRoles ur ON ur.UserId=u.UserId AND (ur.BusinessId=@BusinessId OR ur.BusinessId IS NULL)
          INNER JOIN dbo.RolePermissions rp ON rp.RoleId=ur.RoleId INNER JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
          WHERE u.TenantId=@TenantId AND u.IsActive=1 AND p.Resource=N'dispatches.delivery.execute'
          ORDER BY CONCAT(u.FirstName,N' ',u.LastName)
        """,connection))
        { Scope(command,actor); await using var reader=await command.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct)) drivers.Add(new(reader.GetGuid(0),reader.GetString(1))); }
        return new(warehouses,routes,drivers);
    }

    public async Task<DispatchCandidatePage> CandidatesAsync(DispatchActorIdentity actor, DispatchCandidateQuery query, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        const string source="""
          FROM dbo.SalesDocuments s INNER JOIN dbo.Warehouses w ON w.WarehouseId=s.WarehouseId
          LEFT JOIN dbo.Customers c ON c.CustomerId=s.CustomerId LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId
          LEFT JOIN dbo.AppUsers seller ON seller.UserId=s.SoldByUserId
          OUTER APPLY(SELECT TOP(1) site.AddressLine FROM dbo.PartySites site WHERE site.PartyId=p.PartyId AND site.IsActive=1 ORDER BY site.IsPrimary DESC,site.CreatedAt) address
          OUTER APPLY(SELECT COUNT(*) LineCount,COALESCE(SUM(l.Quantity),0) Quantity FROM dbo.SalesDocumentLines l WHERE l.DocumentId=s.DocumentId) lines
          WHERE s.BusinessId=@BusinessId AND s.ProcessingStatus=N'Completed' AND s.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
            AND (@DocumentType IS NULL OR s.DocumentType=@DocumentType) AND (@WarehouseId IS NULL OR s.WarehouseId=@WarehouseId)
            AND (@From IS NULL OR CAST(s.IssuedAt AS date)>=@From) AND (@To IS NULL OR CAST(s.IssuedAt AS date)<=@To)
            AND (@Search IS NULL OR s.DocumentNumber LIKE '%'+@Search+'%' OR s.CustomerIdentification LIKE '%'+@Search+'%' OR p.DisplayName LIKE '%'+@Search+'%')
            AND NOT EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments ds INNER JOIN dbo.Dispatches d ON d.DispatchId=ds.DispatchId WHERE ds.SourceDocumentId=s.DocumentId AND d.Status<>N'Cancelled')
        """;
        await using var count=new SqlCommand("SELECT COUNT(*) "+source,connection); AddCandidate(count,actor,query); var total=Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        await using var command=new SqlCommand("""
          SELECT s.DocumentId,s.DocumentType,s.DocumentNumber,s.IssuedAt,s.WarehouseId,w.Name,s.CustomerId,COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),CASE WHEN s.CustomerId IS NULL THEN N'Consumidor final' END,s.CustomerIdentification),address.AddressLine,
            COALESCE(NULLIF(CONCAT(seller.FirstName,N' ',seller.LastName),N' '),N'Sin vendedor'),lines.LineCount,lines.Quantity,s.PayableAmount
        """+source+" ORDER BY s.IssuedAt DESC,s.DocumentNumber OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY",connection);
        AddCandidate(command,actor,query); command.Parameters.AddWithValue("@Skip",(query.Page-1)*query.PageSize); command.Parameters.AddWithValue("@Take",query.PageSize);
        var items=new List<DispatchCandidateDocument>(); await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDateTimeOffset(3),reader.GetGuid(4),reader.GetString(5),NullableGuid(reader,6),reader.GetString(7),NullableString(reader,8),reader.GetString(9),reader.GetInt32(10),reader.GetDecimal(11),reader.GetDecimal(12)));
        return new(items,query.Page,query.PageSize,total,total==0?0:(int)Math.Ceiling(total/(decimal)query.PageSize));
    }

    public async Task<DispatchDetail?> GetAsync(DispatchActorIdentity actor, Guid dispatchId, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct);
        await using var header=new SqlCommand("""
          SELECT d.DispatchId,d.BusinessId,d.WarehouseId,w.Name,d.DispatchNumber,d.ScheduledDate,d.DriverName,d.VehiclePlate,d.RouteId,r.Name,d.Notes,d.Status,d.CreatedAt,d.UpdatedAt,d.RowVersion
          FROM dbo.Dispatches d INNER JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId LEFT JOIN dbo.SalesRoutes r ON r.RouteId=d.RouteId
          WHERE d.DispatchId=@Id AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId AND (@ReadAll=1 OR d.DriverUserId=@UserId)
        """,connection); Identity(header,actor,dispatchId);
        Guid business,warehouse; string warehouseName,number,driver,status,version; DateOnly date; string? plate,routeName,notes; Guid? routeId; DateTimeOffset created,updated;
        await using(var reader=await header.ExecuteReaderAsync(ct)) { if(!await reader.ReadAsync(ct)) return null; business=reader.GetGuid(1);warehouse=reader.GetGuid(2);warehouseName=reader.GetString(3);number=reader.GetString(4);date=DateOnly.FromDateTime(reader.GetDateTime(5));driver=reader.GetString(6);plate=NullableString(reader,7);routeId=NullableGuid(reader,8);routeName=NullableString(reader,9);notes=NullableString(reader,10);status=reader.GetString(11);created=reader.GetDateTimeOffset(12);updated=reader.GetDateTimeOffset(13);version=Version(reader,14); }
        var documents=new List<DispatchDocumentDetail>();
        await using(var command=new SqlCommand("SELECT DispatchSourceDocumentId,SourceDocumentId,SourceDocumentType,DocumentNumberSnapshot,CustomerId,CustomerNameSnapshot,DeliveryAddressSnapshot,SellerNameSnapshot,DocumentTotalSnapshot,Status FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id ORDER BY DocumentNumberSnapshot",connection))
        { command.Parameters.AddWithValue("@Id",dispatchId); await using var reader=await command.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct)) documents.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),NullableGuid(reader,4),reader.GetString(5),NullableString(reader,6),reader.GetString(7),reader.GetDecimal(8),reader.GetString(9))); }
        var lines=new List<DispatchLineDetail>();
        await using(var command=new SqlCommand("SELECT DispatchLineId,DispatchSourceDocumentId,SourceLineNumber,ProductId,ProductCodeSnapshot,DescriptionSnapshot,AssignedQuantity,VerifiedQuantity,ShortageQuantity,Status,RowVersion FROM dbo.DispatchLines WHERE DispatchId=@Id ORDER BY DescriptionSnapshot,SourceLineNumber",connection))
        { command.Parameters.AddWithValue("@Id",dispatchId); await using var reader=await command.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct)) lines.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetInt32(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetString(9),Version(reader,10))); }
        var shortages=new List<DispatchShortageDetail>();
        await using(var command=new SqlCommand("SELECT sh.DispatchShortageId,sh.DispatchLineId,sh.ProductId,l.ProductCodeSnapshot,l.DescriptionSnapshot,sh.Quantity,sh.Reason,sh.Notes,sh.CreatedAt FROM dbo.DispatchShortages sh INNER JOIN dbo.DispatchLines l ON l.DispatchLineId=sh.DispatchLineId WHERE sh.DispatchId=@Id ORDER BY sh.CreatedAt",connection))
        { command.Parameters.AddWithValue("@Id",dispatchId); await using var reader=await command.ExecuteReaderAsync(ct); while(await reader.ReadAsync(ct)) shortages.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetString(6),NullableString(reader,7),reader.GetDateTimeOffset(8))); }
        return new(dispatchId,business,warehouse,warehouseName,number,date,driver,plate,routeId,routeName,notes,status,documents,lines,shortages,created,updated,version);
    }

    public async Task<DispatchMutationResult> CreateAsync(DispatchActorIdentity actor, Guid dispatchId, string dispatchNumber, CreateDispatchRequest request, string driverName, string? vehiclePlate, string? notes, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(ct); await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(ct);
        try {
          await using(var command=new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1 AND UseForSales=1) THROW 51000,'Selecciona una bodega de venta válida para el despacho.',1;
            IF @RouteId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.SalesRoutes WHERE RouteId=@RouteId AND BusinessId=@BusinessId AND IsActive=1) THROW 51000,'La ruta no es válida para el despacho.',1;
            DECLARE @ResolvedDriver nvarchar(160)=@Driver;
            IF @DriverUserId IS NOT NULL BEGIN
              SELECT @ResolvedDriver=CONCAT(u.FirstName,N' ',u.LastName) FROM dbo.AppUsers u WHERE u.UserId=@DriverUserId AND u.TenantId=@TenantId AND u.IsActive=1;
              IF @ResolvedDriver IS NULL THROW 51000,'El transportador seleccionado no está activo.',1;
            END
            INSERT dbo.Dispatches(DispatchId,TenantId,BusinessId,WarehouseId,DispatchNumber,ScheduledDate,DriverUserId,DriverName,VehiclePlate,RouteId,Notes,Status,CreatedBy,CreatedAt,UpdatedAt) VALUES(@Id,@TenantId,@BusinessId,@WarehouseId,@Number,@Date,@DriverUserId,@ResolvedDriver,@Plate,@RouteId,@Notes,N'Draft',@UserId,@Now,@Now);
          """,connection,transaction)) { Scope(command,actor);command.Parameters.AddWithValue("@Id",dispatchId);command.Parameters.AddWithValue("@WarehouseId",request.WarehouseId);command.Parameters.AddWithValue("@Number",dispatchNumber);command.Parameters.AddWithValue("@Date",request.ScheduledDate.ToDateTime(TimeOnly.MinValue));command.Parameters.AddWithValue("@Driver",driverName);command.Parameters.AddWithValue("@DriverUserId",(object?)request.DriverUserId??DBNull.Value);command.Parameters.AddWithValue("@Plate",(object?)vehiclePlate??DBNull.Value);command.Parameters.AddWithValue("@RouteId",(object?)request.RouteId??DBNull.Value);command.Parameters.AddWithValue("@Notes",(object?)notes??DBNull.Value);command.Parameters.AddWithValue("@Now",now);await command.ExecuteNonQueryAsync(ct); }
          await AttachAsync(connection,transaction,actor,dispatchId,request.SourceDocumentIds,request.WarehouseId,now,ct); await transaction.CommitAsync(ct); return await ResultAsync(actor,dispatchId,ct);
        } catch(SqlException ex) { await transaction.RollbackAsync(CancellationToken.None); throw Conflict(ex); }
    }

    public async Task<DispatchMutationResult> AddDocumentsAsync(DispatchActorIdentity actor, Guid id, IReadOnlyCollection<Guid> documentIds, byte[] rowVersion, DateTimeOffset now, CancellationToken ct)
    {
      await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(ct);
      try { var warehouse=await LockDraftAsync(connection,tx,actor,id,rowVersion,ct);await AttachAsync(connection,tx,actor,id,documentIds,warehouse,now,ct);await TouchAsync(connection,tx,actor,id,now,ct);await tx.CommitAsync(ct);return await ResultAsync(actor,id,ct); } catch(SqlException ex){await tx.RollbackAsync(CancellationToken.None);throw Conflict(ex);}
    }

    public async Task<DispatchMutationResult> RemoveDocumentAsync(DispatchActorIdentity actor, Guid id, Guid sourceDocumentId, byte[] rowVersion, DateTimeOffset now, CancellationToken ct)
    {
      await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(ct);
      try { await LockDraftAsync(connection,tx,actor,id,rowVersion,ct);await using var command=new SqlCommand("DELETE l FROM dbo.DispatchLines l INNER JOIN dbo.DispatchSourceDocuments d ON d.DispatchSourceDocumentId=l.DispatchSourceDocumentId WHERE d.DispatchId=@Id AND d.SourceDocumentId=@DocumentId; DELETE dbo.DispatchSourceDocuments WHERE DispatchId=@Id AND SourceDocumentId=@DocumentId; IF @@ROWCOUNT<>1 THROW 51000,'El documento no pertenece al despacho.',1;",connection,tx);command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@DocumentId",sourceDocumentId);await command.ExecuteNonQueryAsync(ct);await TouchAsync(connection,tx,actor,id,now,ct);await tx.CommitAsync(ct);return await ResultAsync(actor,id,ct);} catch(SqlException ex){await tx.RollbackAsync(CancellationToken.None);throw Conflict(ex);}
    }

    public async Task<DispatchMutationResult> TransitionAsync(DispatchActorIdentity actor, Guid id, string target, byte[] rowVersion, string key, DateTimeOffset now, CancellationToken ct)
    {
      await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(ct);
      try {
        await using var existing=new SqlCommand("SELECT 1 FROM dbo.DispatchVerificationEvents WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key",connection,tx);existing.Parameters.AddWithValue("@BusinessId",actor.BusinessId);existing.Parameters.AddWithValue("@Key",key);if(await existing.ExecuteScalarAsync(ct) is not null){await tx.RollbackAsync(ct);return await ResultAsync(actor,id,ct);}
        await using var command=new SqlCommand("""
          DECLARE @Current nvarchar(24);SELECT @Current=Status FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId AND RowVersion=@Version;
          IF @Current IS NULL THROW 51000,'El despacho cambió o no existe.',1;
          IF @Target=N'Prepared' AND (@Current<>N'Draft' OR NOT EXISTS(SELECT 1 FROM dbo.DispatchSourceDocuments WHERE DispatchId=@Id)) THROW 51000,'El borrador no está listo para preparar.',1;
          IF @Target=N'InVerification' AND @Current NOT IN(N'Prepared',N'Verified') THROW 51000,'El despacho no puede iniciar verificación.',1;
          IF @Target=N'Verified' AND (@Current<>N'InVerification' OR EXISTS(SELECT 1 FROM dbo.DispatchLines WHERE DispatchId=@Id AND AssignedQuantity<>VerifiedQuantity+ShortageQuantity)) THROW 51000,'Aún existen cantidades sin verificar ni declarar como faltante.',1;
          IF @Target=N'Released' AND @Current<>N'Verified' THROW 51000,'Solo un despacho verificado puede liberarse.',1;
          IF @Target=N'Cancelled' AND @Current NOT IN(N'Draft',N'Prepared') THROW 51000,'El despacho ya no puede cancelarse.',1;
          UPDATE dbo.Dispatches SET Status=@Target,PreparedAt=CASE WHEN @Target=N'Prepared' THEN @Now ELSE PreparedAt END,VerificationStartedAt=CASE WHEN @Target=N'InVerification' THEN @Now ELSE VerificationStartedAt END,VerifiedAt=CASE WHEN @Target=N'Verified' THEN @Now ELSE VerifiedAt END,ReleasedAt=CASE WHEN @Target=N'Released' THEN @Now ELSE ReleasedAt END,CancelledAt=CASE WHEN @Target=N'Cancelled' THEN @Now ELSE CancelledAt END,UpdatedBy=@UserId,UpdatedAt=@Now WHERE DispatchId=@Id;
          INSERT dbo.DispatchVerificationEvents(DispatchVerificationEventId,BusinessId,DispatchId,QuantityDelta,EventType,UserId,OccurredAt,ReceivedAt,IdempotencyKey) VALUES(NEWID(),@BusinessId,@Id,0,N'StatusChanged',@UserId,@Now,@Now,@Key);
        """,connection,tx);Identity(command,actor,id);command.Parameters.Add("@Version",SqlDbType.Timestamp).Value=rowVersion;command.Parameters.AddWithValue("@Target",target);command.Parameters.AddWithValue("@Now",now);command.Parameters.AddWithValue("@Key",key);await command.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);return await ResultAsync(actor,id,ct);
      } catch(SqlException ex){await tx.RollbackAsync(CancellationToken.None);throw Conflict(ex);}
    }

    public async Task<DispatchMutationResult> VerifyQuantityAsync(DispatchActorIdentity actor, Guid id, DispatchVerificationRequest request, DateTimeOffset now, CancellationToken ct)
    {
      await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(ct);
      try { await using var command=new SqlCommand("""
        IF EXISTS(SELECT 1 FROM dbo.DispatchVerificationEvents WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key) RETURN;
        DECLARE @Assigned decimal(19,6),@Verified decimal(19,6),@Short decimal(19,6),@ProductId uniqueidentifier;
        SELECT @Assigned=l.AssignedQuantity,@Verified=l.VerifiedQuantity,@Short=l.ShortageQuantity,@ProductId=l.ProductId FROM dbo.DispatchLines l WITH(UPDLOCK,HOLDLOCK) INNER JOIN dbo.Dispatches d ON d.DispatchId=l.DispatchId WHERE l.DispatchLineId=@LineId AND l.DispatchId=@Id AND d.BusinessId=@BusinessId AND d.TenantId=@TenantId AND d.Status=N'InVerification';
        IF @Assigned IS NULL THROW 51000,'La línea no está disponible para verificación.',1;
        IF @Verified+@Delta<0 OR @Verified+@Delta+@Short>@Assigned THROW 51000,'La cantidad verificada queda fuera del rango permitido.',1;
        UPDATE dbo.DispatchLines SET VerifiedQuantity=@Verified+@Delta,Status=CASE WHEN @Verified+@Delta+@Short=@Assigned AND @Short>0 THEN N'Short' WHEN @Verified+@Delta=@Assigned THEN N'Verified' WHEN @Verified+@Delta>0 THEN N'PartiallyVerified' ELSE N'Pending' END WHERE DispatchLineId=@LineId;
        INSERT dbo.DispatchVerificationEvents(DispatchVerificationEventId,BusinessId,DispatchId,DispatchLineId,ProductId,Barcode,QuantityDelta,EventType,UserId,OccurredAt,ReceivedAt,IdempotencyKey) VALUES(NEWID(),@BusinessId,@Id,@LineId,@ProductId,@Barcode,@Delta,CASE WHEN @Delta>0 THEN N'Scanned' ELSE N'ScanUndone' END,@UserId,@Occurred,@Now,@Key);
        UPDATE dbo.Dispatches SET UpdatedBy=@UserId,UpdatedAt=@Now WHERE DispatchId=@Id;
      """,connection,tx);Identity(command,actor,id);command.Parameters.AddWithValue("@LineId",request.DispatchLineId);Decimal(command,"@Delta",request.QuantityDelta);command.Parameters.AddWithValue("@Barcode",(object?)request.Barcode??DBNull.Value);command.Parameters.AddWithValue("@Occurred",request.OccurredAt);command.Parameters.AddWithValue("@Now",now);command.Parameters.AddWithValue("@Key",request.IdempotencyKey.Trim());await command.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);return await ResultAsync(actor,id,ct);} catch(SqlException ex){await tx.RollbackAsync(CancellationToken.None);throw Conflict(ex);}
    }

    public async Task<DispatchMutationResult> DeclareShortageAsync(DispatchActorIdentity actor, Guid id, DeclareDispatchShortageRequest request, byte[] rowVersion, DateTimeOffset now, CancellationToken ct)
    {
      await using var connection=connections.Create();await connection.OpenAsync(ct);await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(ct);
      try { await using var command=new SqlCommand("""
        IF EXISTS(SELECT 1 FROM dbo.DispatchVerificationEvents WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key) RETURN;
        DECLARE @Assigned decimal(19,6),@Verified decimal(19,6),@Short decimal(19,6),@ProductId uniqueidentifier;
        SELECT @Assigned=l.AssignedQuantity,@Verified=l.VerifiedQuantity,@Short=l.ShortageQuantity,@ProductId=l.ProductId FROM dbo.DispatchLines l WITH(UPDLOCK,HOLDLOCK) INNER JOIN dbo.Dispatches d ON d.DispatchId=l.DispatchId WHERE l.DispatchLineId=@LineId AND l.DispatchId=@Id AND d.BusinessId=@BusinessId AND d.TenantId=@TenantId AND d.Status=N'InVerification' AND d.RowVersion=@Version;
        IF @Assigned IS NULL THROW 51000,'El despacho cambió o la línea no está disponible.',1;
        IF @Verified+@Short+@Quantity>@Assigned THROW 51000,'El faltante supera la cantidad pendiente.',1;
        INSERT dbo.DispatchShortages(DispatchShortageId,DispatchId,DispatchLineId,ProductId,Quantity,Reason,Notes,CreatedBy,CreatedAt) VALUES(NEWID(),@Id,@LineId,@ProductId,@Quantity,@Reason,@Notes,@UserId,@Now);
        UPDATE dbo.DispatchLines SET ShortageQuantity=@Short+@Quantity,Status=CASE WHEN @Verified+@Short+@Quantity=@Assigned THEN N'Short' ELSE N'PartiallyVerified' END WHERE DispatchLineId=@LineId;
        INSERT dbo.DispatchVerificationEvents(DispatchVerificationEventId,BusinessId,DispatchId,DispatchLineId,ProductId,QuantityDelta,EventType,UserId,OccurredAt,ReceivedAt,IdempotencyKey) VALUES(NEWID(),@BusinessId,@Id,@LineId,@ProductId,@Quantity,N'ShortageDeclared',@UserId,@Now,@Now,@Key);
        UPDATE dbo.Dispatches SET UpdatedBy=@UserId,UpdatedAt=@Now WHERE DispatchId=@Id;
      """,connection,tx);Identity(command,actor,id);command.Parameters.AddWithValue("@LineId",request.DispatchLineId);Decimal(command,"@Quantity",request.Quantity);command.Parameters.AddWithValue("@Reason",request.Reason);command.Parameters.AddWithValue("@Notes",(object?)request.Notes??DBNull.Value);command.Parameters.Add("@Version",SqlDbType.Timestamp).Value=rowVersion;command.Parameters.AddWithValue("@Now",now);command.Parameters.AddWithValue("@Key",request.IdempotencyKey.Trim());await command.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);return await ResultAsync(actor,id,ct);} catch(SqlException ex){await tx.RollbackAsync(CancellationToken.None);throw Conflict(ex);}
    }

    public async Task<DispatchReport> ReportAsync(DispatchActorIdentity actor, Guid id, bool prices, CancellationToken ct)
    {
      await using var connection=connections.Create();await connection.OpenAsync(ct);await using var command=new SqlCommand("""
        SELECT d.DispatchNumber,d.ScheduledDate,d.Status,d.DriverName,d.VehiclePlate,sd.SourceDocumentType,sd.DocumentNumberSnapshot,sd.CustomerNameSnapshot,sd.DeliveryAddressSnapshot,sd.SellerNameSnapshot,l.ProductCodeSnapshot,l.DescriptionSnapshot,l.AssignedQuantity,l.VerifiedQuantity,l.ShortageQuantity,l.UnitPriceSnapshot,l.LineTotalSnapshot
        FROM dbo.Dispatches d INNER JOIN dbo.DispatchSourceDocuments sd ON sd.DispatchId=d.DispatchId INNER JOIN dbo.DispatchLines l ON l.DispatchSourceDocumentId=sd.DispatchSourceDocumentId
        WHERE d.DispatchId=@Id AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId AND (@ReadAll=1 OR d.DriverUserId=@UserId) ORDER BY sd.CustomerNameSnapshot,sd.DocumentNumberSnapshot,l.DescriptionSnapshot
      """,connection);Identity(command,actor,id);var rows=new List<DispatchReportRow>();await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))rows.Add(new(reader.GetString(0),DateOnly.FromDateTime(reader.GetDateTime(1)),reader.GetString(2),reader.GetString(3),NullableString(reader,4),reader.GetString(5),reader.GetString(6),reader.GetString(7),NullableString(reader,8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetDecimal(12),reader.GetDecimal(13),reader.GetDecimal(14),prices?reader.GetDecimal(15):null,prices?reader.GetDecimal(16):null));if(rows.Count==0)throw new DispatchNotFoundException("The dispatch does not exist or has no documents.");return new($"Manifiesto {rows[0].DispatchNumber}",DateTimeOffset.UtcNow,prices,rows);
    }

    private static async Task AttachAsync(SqlConnection connection,SqlTransaction tx,DispatchActorIdentity actor,Guid id,IReadOnlyCollection<Guid> ids,Guid warehouse,DateTimeOffset now,CancellationToken ct)
    {
      var table=new DataTable();table.Columns.Add("Id",typeof(Guid));foreach(var value in ids)table.Rows.Add(value);
      await using var command=new SqlCommand("""
        IF EXISTS(SELECT 1 FROM @Ids i WHERE NOT EXISTS(SELECT 1 FROM dbo.SalesDocuments s WHERE s.DocumentId=i.Id AND s.BusinessId=@BusinessId AND s.WarehouseId=@WarehouseId AND s.ProcessingStatus=N'Completed' AND s.DocumentType IN(N'SalesInvoice',N'SalesReceipt'))) THROW 51000,'Uno o más documentos no son ventas procesadas de la bodega.',1;
        IF EXISTS(SELECT 1 FROM @Ids i INNER JOIN dbo.DispatchSourceDocuments sd ON sd.SourceDocumentId=i.Id INNER JOIN dbo.Dispatches d ON d.DispatchId=sd.DispatchId WHERE d.Status<>N'Cancelled') THROW 51000,'Una venta ya pertenece a otro despacho activo.',1;
        INSERT dbo.DispatchSourceDocuments(DispatchSourceDocumentId,DispatchId,SourceDocumentId,SourceDocumentType,DocumentNumberSnapshot,CustomerId,CustomerNameSnapshot,DeliveryAddressSnapshot,SellerId,SellerNameSnapshot,DocumentTotalSnapshot,Status,CreatedAt)
        SELECT NEWID(),@Id,s.DocumentId,s.DocumentType,s.DocumentNumber,s.CustomerId,COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),CASE WHEN s.CustomerId IS NULL THEN N'Consumidor final' END,s.CustomerIdentification),site.AddressLine,s.SoldByUserId,COALESCE(NULLIF(CONCAT(u.FirstName,N' ',u.LastName),N' '),N'Sin vendedor'),s.PayableAmount,N'Pending',@Now
        FROM @Ids i INNER JOIN dbo.SalesDocuments s ON s.DocumentId=i.Id LEFT JOIN dbo.Customers c ON c.CustomerId=s.CustomerId LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId LEFT JOIN dbo.AppUsers u ON u.UserId=s.SoldByUserId OUTER APPLY(SELECT TOP(1) ps.AddressLine FROM dbo.PartySites ps WHERE ps.PartyId=p.PartyId AND ps.IsActive=1 ORDER BY ps.IsPrimary DESC,ps.CreatedAt) site;
        INSERT dbo.DispatchLines(DispatchLineId,DispatchId,DispatchSourceDocumentId,SourceLineNumber,ProductId,ProductCodeSnapshot,DescriptionSnapshot,AssignedQuantity,VerifiedQuantity,ShortageQuantity,UnitPriceSnapshot,LineTotalSnapshot,Status)
        SELECT NEWID(),@Id,sd.DispatchSourceDocumentId,l.LineNumber,l.ProductId,COALESCE(p.ProductCode,p.Sku,CONVERT(nvarchar(36),p.ProductId)),l.Description,l.Quantity,0,0,l.UnitPrice,l.LineTotal,N'Pending'
        FROM @Ids i INNER JOIN dbo.DispatchSourceDocuments sd ON sd.DispatchId=@Id AND sd.SourceDocumentId=i.Id INNER JOIN dbo.SalesDocumentLines l ON l.DocumentId=i.Id INNER JOIN dbo.Products p ON p.ProductId=l.ProductId;
      """,connection,tx);var parameter=command.Parameters.AddWithValue("@Ids",table);parameter.SqlDbType=SqlDbType.Structured;parameter.TypeName="dbo.GuidIdList";command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@BusinessId",actor.BusinessId);command.Parameters.AddWithValue("@WarehouseId",warehouse);command.Parameters.AddWithValue("@Now",now);await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Guid> LockDraftAsync(SqlConnection c,SqlTransaction tx,DispatchActorIdentity actor,Guid id,byte[] version,CancellationToken ct){await using var command=new SqlCommand("SELECT WarehouseId FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK) WHERE DispatchId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Draft' AND RowVersion=@Version",c,tx);Identity(command,actor,id);command.Parameters.Add("@Version",SqlDbType.Timestamp).Value=version;var value=await command.ExecuteScalarAsync(ct);return value is Guid warehouse?warehouse:throw new DispatchConflictException("The draft changed or is not editable.");}
    private static async Task TouchAsync(SqlConnection c,SqlTransaction tx,DispatchActorIdentity actor,Guid id,DateTimeOffset now,CancellationToken ct){await using var command=new SqlCommand("UPDATE dbo.Dispatches SET UpdatedBy=@UserId,UpdatedAt=@Now WHERE DispatchId=@Id",c,tx);command.Parameters.AddWithValue("@Id",id);command.Parameters.AddWithValue("@UserId",actor.UserId);command.Parameters.AddWithValue("@Now",now);await command.ExecuteNonQueryAsync(ct);}
    private async Task<DispatchMutationResult> ResultAsync(DispatchActorIdentity actor,Guid id,CancellationToken ct){await using var c=connections.Create();await c.OpenAsync(ct);await using var command=new SqlCommand("SELECT d.DispatchNumber,d.Status,COUNT(DISTINCT sd.DispatchSourceDocumentId),COUNT(DISTINCT l.DispatchLineId),COALESCE(SUM(l.AssignedQuantity),0),COALESCE(SUM(l.VerifiedQuantity),0),COALESCE(SUM(l.ShortageQuantity),0),d.RowVersion FROM dbo.Dispatches d LEFT JOIN dbo.DispatchSourceDocuments sd ON sd.DispatchId=d.DispatchId LEFT JOIN dbo.DispatchLines l ON l.DispatchId=d.DispatchId WHERE d.DispatchId=@Id AND d.TenantId=@TenantId AND d.BusinessId=@BusinessId GROUP BY d.DispatchNumber,d.Status,d.RowVersion",c);Identity(command,actor,id);await using var r=await command.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new DispatchNotFoundException("The dispatch does not exist.");return new(id,r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetInt32(3),r.GetDecimal(4),r.GetDecimal(5),r.GetDecimal(6),Version(r,7));}
    private static void AddQuery(SqlCommand c,DispatchActorIdentity a,DispatchQuery q){Scope(c,a);c.Parameters.AddWithValue("@Search",(object?)q.Search??DBNull.Value);c.Parameters.AddWithValue("@Status",(object?)q.Status??DBNull.Value);c.Parameters.AddWithValue("@From",q.From.HasValue?q.From.Value.ToDateTime(TimeOnly.MinValue):DBNull.Value);c.Parameters.AddWithValue("@To",q.To.HasValue?q.To.Value.ToDateTime(TimeOnly.MinValue):DBNull.Value);}
    private static void AddCandidate(SqlCommand c,DispatchActorIdentity a,DispatchCandidateQuery q){c.Parameters.AddWithValue("@BusinessId",a.BusinessId);c.Parameters.AddWithValue("@Search",(object?)q.Search??DBNull.Value);c.Parameters.AddWithValue("@DocumentType",(object?)q.DocumentType??DBNull.Value);c.Parameters.AddWithValue("@WarehouseId",(object?)q.WarehouseId??DBNull.Value);c.Parameters.AddWithValue("@From",q.From.HasValue?q.From.Value.ToDateTime(TimeOnly.MinValue):DBNull.Value);c.Parameters.AddWithValue("@To",q.To.HasValue?q.To.Value.ToDateTime(TimeOnly.MinValue):DBNull.Value);}
    private static void Scope(SqlCommand c,DispatchActorIdentity a){c.Parameters.AddWithValue("@TenantId",a.TenantId);c.Parameters.AddWithValue("@BusinessId",a.BusinessId);c.Parameters.AddWithValue("@UserId",a.UserId);c.Parameters.AddWithValue("@ReadAll",a.Permissions.Contains(DispatchPermissionCodes.ReadAll));}
    private static void Identity(SqlCommand c,DispatchActorIdentity a,Guid id){Scope(c,a);c.Parameters.AddWithValue("@Id",id);}
    private static void Decimal(SqlCommand c,string name,decimal value){var p=c.Parameters.Add(name,SqlDbType.Decimal);p.Precision=19;p.Scale=6;p.Value=value;}
    private static string Version(SqlDataReader r,int i)=>Convert.ToBase64String((byte[])r.GetValue(i));
    private static string? NullableString(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static Guid? NullableGuid(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetGuid(i);
    private static Exception Conflict(SqlException ex)=>new DispatchConflictException(ex.Message);
}
