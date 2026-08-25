using System.Data;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlGoodsReceiptWorkspaceStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider,
    IAuralyIdGenerator ids) : IGoodsReceiptWorkspaceStore
{
    public async Task<GoodsReceiptWorkspaceOptions> GetOptionsAsync(
        PurchasingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT WarehouseId,Code,Name
            FROM dbo.Warehouses
            WHERE BusinessId=@BusinessId AND IsActive=1 AND UseForSales=1
            ORDER BY Name,Code;
            SELECT SupplierId,Identification,Name,PurchaseEvidencePolicy
            FROM dbo.Suppliers
            WHERE BusinessId=@BusinessId AND IsActive=1
            ORDER BY Name,Identification;
            SELECT Code,Label,Description
            FROM reference.Options
            WHERE CatalogCode=N'purchase-evidence-type' AND IsActive=1
            ORDER BY SortOrder,Label;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var warehouses = new List<GoodsReceiptWarehouseOption>();
        while (await reader.ReadAsync(cancellationToken))
            warehouses.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(cancellationToken);
        var suppliers = new List<GoodsReceiptSupplierOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var policy = reader.IsDBNull(3) ? null : reader.GetString(3);
            suppliers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                policy, PurchaseEvidenceTypes.AllowedFor(policy)));
        }
        await reader.NextResultAsync(cancellationToken);
        var evidenceTypes = new List<PurchaseEvidenceTypeOption>();
        while (await reader.ReadAsync(cancellationToken))
            evidenceTypes.Add(new(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        return new(warehouses, suppliers, evidenceTypes);
    }

    public async Task<GoodsReceiptProductPage> FindProductsAsync(
        PurchasingUserIdentity user, Guid supplierId, string? search,
        bool includeUnassociated, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers
              WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51120,'The supplier is outside the authenticated business.',1;

            SELECT COUNT(*)
            FROM dbo.Products p
            LEFT JOIN dbo.SupplierProducts sp
              ON sp.ProductId=p.ProductId AND sp.BusinessId=@BusinessId
             AND sp.SupplierId=@SupplierId AND sp.IsActive=1
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1
              AND NOT EXISTS(SELECT 1 FROM dbo.ProductLinks link
                             WHERE link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId
                               AND link.SharesInventory=1 AND link.IsActive=1)
              AND (@IncludeUnassociated=1 OR sp.SupplierProductId IS NOT NULL)
              AND (@Search IS NULL OR p.ProductCode LIKE N'%'+@Search+N'%'
                   OR p.Reference LIKE N'%'+@Search+N'%' OR p.Name LIKE N'%'+@Search+N'%'
                   OR sp.SupplierProductCode LIKE N'%'+@Search+N'%'
                   OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes pb
                              WHERE pb.ProductId=p.ProductId AND pb.BusinessId=@BusinessId
                                AND pb.IsActive=1 AND pb.Barcode LIKE N'%'+@Search+N'%'));

            SELECT p.ProductId,COALESCE(p.ProductCode,N''),p.Reference,p.Name,
                   sp.SupplierProductCode,latest.LatestUnitCost,
                   COALESCE(tp.Code,N'00'),COALESCE(tp.Rate,0),
                   COALESCE(p.PurchaseTaxTreatment,N'DeductibleInputVat'),
                   COALESCE(b.Barcodes,N''),
                   COALESCE(p.BaseUnitCode,N'EA'),
                   CONVERT(BIT,CASE WHEN sp.SupplierProductId IS NULL THEN 0 ELSE 1 END),
                   COALESCE(sp.PurchasePresentationName,N'Unidad'),COALESCE(sp.UnitsPerPresentation,1),
                   COALESCE(sp.IsPrimary,CONVERT(BIT,0))
            FROM dbo.Products p
            LEFT JOIN dbo.SupplierProducts sp
              ON sp.ProductId=p.ProductId AND sp.BusinessId=@BusinessId
             AND sp.SupplierId=@SupplierId AND sp.IsActive=1
            LEFT JOIN dbo.TaxProfiles tp
              ON tp.TaxProfileId=COALESCE(p.PurchaseTaxProfileId,p.TaxProfileId)
             AND tp.BusinessId=@BusinessId
            LEFT JOIN dbo.SupplierProductLatestCosts latest
              ON latest.BusinessId=@BusinessId AND latest.SupplierId=@SupplierId
             AND latest.ProductId=p.ProductId
            OUTER APPLY (
              SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),pb.Barcode),N'|') AS Barcodes
              FROM dbo.ProductBarcodes pb
              WHERE pb.ProductId=p.ProductId AND pb.BusinessId=@BusinessId AND pb.IsActive=1
            ) b
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1
              AND NOT EXISTS(SELECT 1 FROM dbo.ProductLinks link
                             WHERE link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId
                               AND link.SharesInventory=1 AND link.IsActive=1)
              AND (@IncludeUnassociated=1 OR sp.SupplierProductId IS NOT NULL)
              AND (@Search IS NULL OR p.ProductCode LIKE N'%'+@Search+N'%'
                   OR p.Reference LIKE N'%'+@Search+N'%' OR p.Name LIKE N'%'+@Search+N'%'
                   OR sp.SupplierProductCode LIKE N'%'+@Search+N'%'
                   OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes pb
                              WHERE pb.ProductId=p.ProductId AND pb.BusinessId=@BusinessId
                                AND pb.IsActive=1 AND pb.Barcode LIKE N'%'+@Search+N'%'))
            ORDER BY CASE WHEN sp.SupplierProductId IS NULL THEN 1 ELSE 0 END,p.Name,p.ProductId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@SupplierId", supplierId);
        command.Parameters.AddWithValue("@IncludeUnassociated", includeUnassociated);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var total = reader.GetInt32(0);
            await reader.NextResultAsync(cancellationToken);
            var items = new List<GoodsReceiptProductOption>();
            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadProduct(reader));
            return new(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
        }
        catch (SqlException exception) when (exception.Number == 51120)
        {
            throw new PurchasingValidationException(exception.Message);
        }
    }

    public async Task<GoodsReceiptProductOption> AssociateProductAsync(
        PurchasingUserIdentity user, AssociateGoodsReceiptProductRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string sql = """
                IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers
                               WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51120,'The supplier is outside the authenticated business.',1;
                IF NOT EXISTS (SELECT 1 FROM dbo.Products
                               WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51125,'The product is outside the authenticated business.',1;
                IF EXISTS (SELECT 1 FROM dbo.ProductLinks
                           WHERE BusinessId=@BusinessId AND ChildProductId=@ProductId
                             AND SharesInventory=1 AND IsActive=1)
                  THROW 51125,'Linked inventory products must be received through their root product.',1;

                IF @IsPrimary=1
                  UPDATE dbo.SupplierProducts SET IsPrimary=0
                  WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;

                IF EXISTS (SELECT 1 FROM dbo.SupplierProducts WITH (UPDLOCK,HOLDLOCK)
                           WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND ProductId=@ProductId)
                  UPDATE dbo.SupplierProducts
                  SET SupplierProductCode=COALESCE(@SupplierProductCode,SupplierProductCode),
                      PurchasePresentationName=@PresentationName,UnitsPerPresentation=@UnitsPerPresentation,
                      IsPrimary=@IsPrimary,IsActive=1
                  WHERE BusinessId=@BusinessId AND SupplierId=@SupplierId AND ProductId=@ProductId;
                ELSE
                  INSERT dbo.SupplierProducts
                    (SupplierProductId,BusinessId,ProductId,SupplierId,SupplierProductCode,PurchasePresentationName,UnitsPerPresentation,IsPrimary,IsActive,CreatedAt)
                  VALUES
                    (@SupplierProductId,@BusinessId,@ProductId,@SupplierId,@SupplierProductCode,@PresentationName,@UnitsPerPresentation,@IsPrimary,1,@Now);

                SELECT p.ProductId,COALESCE(p.ProductCode,N''),p.Reference,p.Name,
                       sp.SupplierProductCode,latest.LatestUnitCost,
                       COALESCE(tp.Code,N'00'),COALESCE(tp.Rate,0),
                       COALESCE(p.PurchaseTaxTreatment,N'DeductibleInputVat'),
                       COALESCE(b.Barcodes,N''),COALESCE(p.BaseUnitCode,N'EA'),CONVERT(BIT,1),
                       sp.PurchasePresentationName,sp.UnitsPerPresentation,sp.IsPrimary
                FROM dbo.Products p
                INNER JOIN dbo.SupplierProducts sp
                  ON sp.ProductId=p.ProductId AND sp.BusinessId=@BusinessId
                 AND sp.SupplierId=@SupplierId AND sp.IsActive=1
                LEFT JOIN dbo.TaxProfiles tp
                  ON tp.TaxProfileId=COALESCE(p.PurchaseTaxProfileId,p.TaxProfileId)
                 AND tp.BusinessId=@BusinessId
                LEFT JOIN dbo.SupplierProductLatestCosts latest
                  ON latest.BusinessId=@BusinessId AND latest.SupplierId=@SupplierId
                 AND latest.ProductId=p.ProductId
                OUTER APPLY (
                  SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),pb.Barcode),N'|') AS Barcodes
                  FROM dbo.ProductBarcodes pb
                  WHERE pb.ProductId=p.ProductId AND pb.BusinessId=@BusinessId AND pb.IsActive=1
                ) b
                WHERE p.ProductId=@ProductId AND p.BusinessId=@BusinessId;
                """;
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@SupplierProductId", ids.NewId());
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
            command.Parameters.AddWithValue("@ProductId", request.ProductId);
            command.Parameters.AddWithValue("@SupplierProductCode", (object?)request.SupplierProductCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsPrimary", request.IsPrimary);
            command.Parameters.AddWithValue("@PresentationName", request.PurchasePresentationName.Trim());
            command.Parameters.AddWithValue("@UnitsPerPresentation", request.UnitsPerPresentation);
            command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new PurchasingValidationException("The associated product could not be read.");
            var product = ReadProduct(reader);
            await reader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
            return product;
        }
        catch (SqlException exception) when (exception.Number is 51120 or 51125)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PurchasingValidationException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new PurchasingConflictException("The product is already associated with this supplier.");
        }
    }

    private static GoodsReceiptProductOption ReadProduct(SqlDataReader reader)
    {
        var barcodes = reader.GetString(9).Split('|', StringSplitOptions.RemoveEmptyEntries);
        return new(
            reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.GetString(6),
            reader.GetDecimal(7), reader.GetString(8), barcodes, reader.GetString(10), reader.GetBoolean(11),
            reader.GetString(12), reader.GetDecimal(13), reader.GetBoolean(14));
    }
    public async Task<GoodsReceiptPage> ListAsync(
        PurchasingUserIdentity user, string? search, string? status,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            WITH Entries AS (
              SELECT d.GoodsReceiptDraftId DocumentId,CAST(NULL AS NVARCHAR(40)) DocumentNumber,
                     N'Draft' Status,d.WarehouseId,w.Name WarehouseName,d.SupplierId,s.Name SupplierName,
                     d.SupplierInvoiceNumber,d.ReceivedAt,d.GrandTotal,d.UpdatedAt,d.PurchaseEvidenceType
              FROM dbo.GoodsReceiptDrafts d
              LEFT JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId
              LEFT JOIN dbo.Suppliers s ON s.SupplierId=d.SupplierId
              WHERE d.BusinessId=@BusinessId
              UNION ALL
              SELECT r.GoodsReceiptId,r.DocumentNumber,r.Status,r.WarehouseId,w.Name,r.SupplierId,s.Name,
                     r.SupplierInvoiceNumber,r.ReceivedAt,r.GrandTotal,COALESCE(r.ProcessedAt,r.AcceptedAt),r.PurchaseEvidenceType
              FROM dbo.GoodsReceipts r
              INNER JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
              INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
              WHERE r.BusinessId=@BusinessId
            ), Filtered AS (
              SELECT * FROM Entries
              WHERE (@Status IS NULL OR Status=@Status)
                AND (@Search IS NULL OR DocumentNumber LIKE N'%'+@Search+N'%'
                     OR SupplierName LIKE N'%'+@Search+N'%'
                     OR SupplierInvoiceNumber LIKE N'%'+@Search+N'%'
                     OR WarehouseName LIKE N'%'+@Search+N'%')
            )
            SELECT COUNT(*) FROM Filtered;
            WITH Entries AS (
              SELECT d.GoodsReceiptDraftId DocumentId,CAST(NULL AS NVARCHAR(40)) DocumentNumber,
                     N'Draft' Status,d.WarehouseId,w.Name WarehouseName,d.SupplierId,s.Name SupplierName,
                     d.SupplierInvoiceNumber,d.ReceivedAt,d.GrandTotal,d.UpdatedAt,d.PurchaseEvidenceType
              FROM dbo.GoodsReceiptDrafts d
              LEFT JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId
              LEFT JOIN dbo.Suppliers s ON s.SupplierId=d.SupplierId
              WHERE d.BusinessId=@BusinessId
              UNION ALL
              SELECT r.GoodsReceiptId,r.DocumentNumber,r.Status,r.WarehouseId,w.Name,r.SupplierId,s.Name,
                     r.SupplierInvoiceNumber,r.ReceivedAt,r.GrandTotal,COALESCE(r.ProcessedAt,r.AcceptedAt),r.PurchaseEvidenceType
              FROM dbo.GoodsReceipts r
              INNER JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
              INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
              WHERE r.BusinessId=@BusinessId
            )
            SELECT DocumentId,DocumentNumber,Status,WarehouseId,WarehouseName,SupplierId,SupplierName,
                   SupplierInvoiceNumber,ReceivedAt,GrandTotal,UpdatedAt,PurchaseEvidenceType
            FROM Entries
            WHERE (@Status IS NULL OR Status=@Status)
              AND (@Search IS NULL OR DocumentNumber LIKE N'%'+@Search+N'%'
                   OR SupplierName LIKE N'%'+@Search+N'%'
                   OR SupplierInvoiceNumber LIKE N'%'+@Search+N'%'
                   OR WarehouseName LIKE N'%'+@Search+N'%')
            ORDER BY UpdatedAt DESC,DocumentId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<GoodsReceiptListItem>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(
                reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetDateTimeOffset(8),
                reader.GetDecimal(9), reader.GetDateTimeOffset(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        return new(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<GoodsReceiptDetail?> GetDetailAsync(
        PurchasingUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT r.DocumentNumber,r.Status,r.WarehouseId,w.Name,r.SupplierId,s.Name,
                   r.SupplierInvoiceNumber,r.SupplierInvoiceDate,r.ReceivedAt,r.CreatesPayable,
                   r.DueDate,r.CurrencyCode,r.Notes,r.NetAmount,r.TaxAmount,r.GrandTotal,
                   r.AcceptedAt,r.ProcessedAt,r.PurchaseEvidenceType
            FROM dbo.GoodsReceipts r
            INNER JOIN dbo.Businesses b
              ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
            WHERE r.GoodsReceiptId=@DocumentId AND r.BusinessId=@BusinessId;

            SELECT l.LineNumber,l.ProductId,l.DescriptionSnapshot,l.Quantity,l.UnitCost,
                   l.DiscountAmount,l.TaxCode,l.TaxRate,l.TaxTreatment,l.NetAmount,
                   l.TaxAmount,l.LineTotal,l.PresentationNameSnapshot,
                   l.PresentationQuantity,l.UnitsPerPresentation
            FROM dbo.GoodsReceiptLines l
            WHERE l.GoodsReceiptId=@DocumentId
            ORDER BY l.LineNumber;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var number = reader.GetString(0);
        var status = reader.GetString(1);
        var warehouseId = reader.GetGuid(2);
        var warehouseName = reader.GetString(3);
        var supplierId = reader.GetGuid(4);
        var supplierName = reader.GetString(5);
        var supplierInvoiceNumber = reader.IsDBNull(6) ? null : reader.GetString(6);
        DateTimeOffset? supplierInvoiceDate = reader.IsDBNull(7) ? null : reader.GetDateTimeOffset(7);
        var receivedAt = reader.GetDateTimeOffset(8);
        var createsPayable = reader.GetBoolean(9);
        DateTimeOffset? dueDate = reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10);
        var currencyCode = reader.GetString(11);
        var notes = reader.IsDBNull(12) ? null : reader.GetString(12);
        var netAmount = reader.GetDecimal(13);
        var taxAmount = reader.GetDecimal(14);
        var grandTotal = reader.GetDecimal(15);
        var acceptedAt = reader.GetDateTimeOffset(16);
        DateTimeOffset? processedAt = reader.IsDBNull(17) ? null : reader.GetDateTimeOffset(17);
        var purchaseEvidenceType = reader.GetString(18);

        await reader.NextResultAsync(cancellationToken);
        var lines = new List<GoodsReceiptLineSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(
                reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetString(6), reader.GetDecimal(7), reader.GetString(8),
                reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetString(12), reader.GetDecimal(13), reader.GetDecimal(14)));

        return new GoodsReceiptDetail(
            documentId, number, status, warehouseId, warehouseName, supplierId,
            supplierName, supplierInvoiceNumber, supplierInvoiceDate, receivedAt,
            createsPayable, dueDate, currencyCode, notes, netAmount, taxAmount,
            grandTotal, acceptedAt, processedAt, lines, purchaseEvidenceType);
    }
    public async Task<GoodsReceiptDraft?> GetDraftAsync(
        PurchasingUserIdentity user, Guid draftId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await LoadDraftAsync(connection, null, user.BusinessId, draftId, cancellationToken);
    }

    public async Task<GoodsReceiptDraft> SaveDraftAsync(
        PurchasingUserIdentity user, SaveGoodsReceiptDraftRequest request,
        GoodsReceiptCalculation? calculation, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ValidateScopeAsync(connection, transaction, user, request, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var existingToken = await LoadTokenAsync(connection, transaction, user.BusinessId, request.DraftId, cancellationToken);
            if (existingToken is null)
            {
                if (request.ConcurrencyToken is not null)
                    throw new PurchasingConflictException("The draft no longer exists.");
                await InsertDraftAsync(connection, transaction, user, request, calculation, now, cancellationToken);
            }
            else
            {
                if (request.ConcurrencyToken is null ||
                    !existingToken.AsSpan().SequenceEqual(ParseToken(request.ConcurrencyToken)))
                    throw new PurchasingConflictException("The draft changed in another session.");
                await UpdateDraftAsync(connection, transaction, user, request, calculation, existingToken, now, cancellationToken);
                await DeleteLinesAsync(connection, transaction, request.DraftId, cancellationToken);
            }
            await InsertLinesAsync(connection, transaction, request.DraftId, request.Lines, calculation, cancellationToken);
            var saved = await LoadDraftAsync(connection, transaction, user.BusinessId, request.DraftId, cancellationToken)
                ?? throw new InvalidOperationException("The saved draft could not be loaded.");
            await transaction.CommitAsync(cancellationToken);
            return saved;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteDraftAsync(
        PurchasingUserIdentity user, Guid draftId, string concurrencyToken,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            DELETE dbo.GoodsReceiptDrafts
            WHERE GoodsReceiptDraftId=@DraftId AND BusinessId=@BusinessId AND RowVersion=@RowVersion;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DraftId", draftId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = ParseToken(concurrencyToken);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PurchasingConflictException("The draft no longer exists or changed in another session.");
    }

    private static async Task ValidateScopeAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        SaveGoodsReceiptDraftRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51121,'The business is outside the authenticated tenant.',1;
            IF @WarehouseId IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId
                AND IsActive=1 AND UseForSales=1)
              THROW 51122,'The warehouse is outside the authenticated business.',1;
            IF @SupplierId IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51123,'The supplier is outside the authenticated business.',1;
            IF @SupplierId IS NOT NULL AND @PurchaseEvidenceType IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers
              WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1
                AND (PurchaseEvidencePolicy IS NULL
                  OR PurchaseEvidencePolicy=N'InternalReceiptVoucher' AND @PurchaseEvidenceType=N'InternalReceiptVoucher'
                  OR PurchaseEvidencePolicy=N'SupplierElectronicInvoice' AND @PurchaseEvidenceType IN (N'SupplierElectronicInvoice',N'InternalReceiptVoucher')
                  OR PurchaseEvidencePolicy=N'BuyerElectronicSupportDocument' AND @PurchaseEvidenceType IN (N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher')))
              THROW 51126,'The selected evidence type is not allowed by the supplier configuration.',1;
            IF @ProductsJson<>N'[]' AND EXISTS (
              SELECT x.ProductId
              FROM OPENJSON(@ProductsJson) WITH (ProductId UNIQUEIDENTIFIER '$') x
              LEFT JOIN dbo.Products p ON p.ProductId=x.ProductId AND p.BusinessId=@BusinessId AND p.IsActive=1
              LEFT JOIN dbo.SupplierProducts sp ON sp.ProductId=x.ProductId AND sp.SupplierId=@SupplierId
                    AND sp.BusinessId=@BusinessId AND sp.IsActive=1
              WHERE p.ProductId IS NULL OR sp.SupplierProductId IS NULL)
              THROW 51124,'Every product must be active and associated with the selected supplier.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", (object?)request.WarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierId", (object?)request.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PurchaseEvidenceType", (object?)request.PurchaseEvidenceType ?? DBNull.Value);
        command.Parameters.AddWithValue("@ProductsJson", JsonSerializer.Serialize(request.Lines.Select(x => x.ProductId).Distinct()));
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number is >= 51121 and <= 51126)
        { throw new PurchasingValidationException(exception.Message); }
    }

    private static async Task<byte[]?> LoadTokenAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid draftId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT RowVersion FROM dbo.GoodsReceiptDrafts WITH (UPDLOCK,HOLDLOCK)
            WHERE GoodsReceiptDraftId=@DraftId AND BusinessId=@BusinessId;
            IF EXISTS (SELECT 1 FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@DraftId AND BusinessId=@BusinessId)
              THROW 51125,'A confirmed receipt already exists with this identifier.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DraftId", draftId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        try { return (byte[]?)await command.ExecuteScalarAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number == 51125)
        { throw new PurchasingConflictException(exception.Message); }
    }

    private static async Task InsertDraftAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        SaveGoodsReceiptDraftRequest request, GoodsReceiptCalculation? calculation,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.GoodsReceiptDrafts
              (GoodsReceiptDraftId,BusinessId,WarehouseId,SupplierId,PurchaseEvidenceType,SupplierInvoiceNumber,
               SupplierInvoiceDate,ReceivedAt,CreatesPayable,DueDate,CurrencyCode,Notes,
               NetAmount,TaxAmount,GrandTotal,CreatedByUserId,UpdatedByUserId,CreatedAt,UpdatedAt)
            VALUES(@Id,@BusinessId,@WarehouseId,@SupplierId,@PurchaseEvidenceType,@InvoiceNumber,@InvoiceDate,@ReceivedAt,
                   @CreatesPayable,@DueDate,@Currency,@Notes,@Net,@Tax,@Total,@UserId,@UserId,@Now,@Now);
            """;
        await using var command = DraftCommand(sql, connection, transaction, user, request, calculation, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateDraftAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        SaveGoodsReceiptDraftRequest request, GoodsReceiptCalculation? calculation,
        byte[] rowVersion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.GoodsReceiptDrafts
            SET WarehouseId=@WarehouseId,SupplierId=@SupplierId,PurchaseEvidenceType=@PurchaseEvidenceType,SupplierInvoiceNumber=@InvoiceNumber,
                SupplierInvoiceDate=@InvoiceDate,ReceivedAt=@ReceivedAt,CreatesPayable=@CreatesPayable,
                DueDate=@DueDate,CurrencyCode=@Currency,Notes=@Notes,NetAmount=@Net,TaxAmount=@Tax,
                GrandTotal=@Total,UpdatedByUserId=@UserId,UpdatedAt=@Now
            WHERE GoodsReceiptDraftId=@Id AND BusinessId=@BusinessId AND RowVersion=@RowVersion;
            """;
        await using var command = DraftCommand(sql, connection, transaction, user, request, calculation, now);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = rowVersion;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PurchasingConflictException("The draft changed in another session.");
    }

    private static SqlCommand DraftCommand(
        string sql, SqlConnection connection, SqlTransaction transaction,
        PurchasingUserIdentity user, SaveGoodsReceiptDraftRequest request,
        GoodsReceiptCalculation? calculation, DateTimeOffset now)
    {
        var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.DraftId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", (object?)request.WarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierId", (object?)request.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PurchaseEvidenceType", (object?)request.PurchaseEvidenceType ?? DBNull.Value);
        command.Parameters.AddWithValue("@InvoiceNumber", (object?)request.SupplierInvoiceNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@InvoiceDate", (object?)request.SupplierInvoiceDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReceivedAt", request.ReceivedAt);
        command.Parameters.AddWithValue("@CreatesPayable", request.CreatesPayable);
        command.Parameters.AddWithValue("@DueDate", (object?)request.DueDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@Currency", request.CurrencyCode);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
        AddDecimal(command, "@Net", calculation?.NetAmount ?? 0, 19, 4);
        AddDecimal(command, "@Tax", calculation?.TaxAmount ?? 0, 19, 4);
        AddDecimal(command, "@Total", calculation?.GrandTotal ?? 0, 19, 4);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        return command;
    }

    private static async Task DeleteLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid draftId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "DELETE dbo.GoodsReceiptDraftLines WHERE GoodsReceiptDraftId=@Id;", connection, transaction);
        command.Parameters.AddWithValue("@Id", draftId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid draftId, IReadOnlyCollection<GoodsReceiptLineRequest> requestLines,
        GoodsReceiptCalculation? calculation, CancellationToken cancellationToken)
    {
        if (calculation is null) return;
        const string sql = """
            INSERT dbo.GoodsReceiptDraftLines
              (GoodsReceiptDraftId,LineNumber,ProductId,DescriptionSnapshot,Quantity,UnitCost,
               DiscountAmount,TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation)
            VALUES(@Id,@Line,@ProductId,@Description,@Quantity,@UnitCost,@Discount,@TaxCode,
                   @TaxRate,@TaxTreatment,@Net,@Tax,@Total,@PresentationName,@PresentationQuantity,@UnitsPerPresentation);
            """;
        foreach (var line in calculation.Lines)
        {
            var source = requestLines.Single(item => item.LineNumber == line.LineNumber);
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Id", draftId);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            command.Parameters.AddWithValue("@ProductId", line.ProductId);
            command.Parameters.AddWithValue("@Description", line.Description);
            AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
            AddDecimal(command, "@UnitCost", line.UnitCost, 19, 6);
            AddDecimal(command, "@Discount", line.DiscountAmount, 19, 4);
            command.Parameters.AddWithValue("@TaxCode", line.TaxCode);
            AddDecimal(command, "@TaxRate", line.TaxRate, 9, 6);
            command.Parameters.AddWithValue("@TaxTreatment", line.TaxTreatment.ToString());
            AddDecimal(command, "@Net", line.NetAmount, 19, 4);
            AddDecimal(command, "@Tax", line.TaxAmount, 19, 4);
            AddDecimal(command, "@Total", line.LineTotal, 19, 4);
            command.Parameters.AddWithValue("@PresentationName", source.PresentationName);
            AddDecimal(command, "@PresentationQuantity", source.PresentationQuantity, 19, 6);
            AddDecimal(command, "@UnitsPerPresentation", source.UnitsPerPresentation, 19, 6);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<GoodsReceiptDraft?> LoadDraftAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid businessId,
        Guid draftId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT GoodsReceiptDraftId,BusinessId,WarehouseId,SupplierId,SupplierInvoiceNumber,
                   SupplierInvoiceDate,ReceivedAt,CreatesPayable,DueDate,CurrencyCode,Notes,
                   NetAmount,TaxAmount,GrandTotal,UpdatedAt,RowVersion,PurchaseEvidenceType
            FROM dbo.GoodsReceiptDrafts
            WHERE GoodsReceiptDraftId=@Id AND BusinessId=@BusinessId;
            SELECT LineNumber,ProductId,DescriptionSnapshot,Quantity,UnitCost,DiscountAmount,
                   TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,
                   PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation
            FROM dbo.GoodsReceiptDraftLines
            WHERE GoodsReceiptDraftId=@Id ORDER BY LineNumber;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", draftId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var header = new
        {
            Id = reader.GetGuid(0), Business = reader.GetGuid(1),
            Warehouse = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2),
            Supplier = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
            Invoice = reader.IsDBNull(4) ? null : reader.GetString(4),
            InvoiceDate = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(5),
            Received = reader.GetDateTimeOffset(6), Payable = reader.GetBoolean(7),
            Due = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(8),
            Currency = reader.GetString(9), Notes = reader.IsDBNull(10) ? null : reader.GetString(10),
            Net = reader.GetDecimal(11), Tax = reader.GetDecimal(12), Total = reader.GetDecimal(13),
            Updated = reader.GetDateTimeOffset(14), Token = Convert.ToBase64String(reader.GetFieldValue<byte[]>(15)),
            EvidenceType = reader.IsDBNull(16) ? null : reader.GetString(16)
        };
        await reader.NextResultAsync(cancellationToken);
        var lines = new List<GoodsReceiptLineSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(
                reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetString(6), reader.GetDecimal(7),
                reader.GetString(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetString(12), reader.GetDecimal(13), reader.GetDecimal(14)));
        return new(header.Id, header.Business, header.Warehouse, header.Supplier, header.Invoice,
            header.InvoiceDate, header.Received, header.Payable, header.Due, header.Currency,
            header.Notes, header.Net, header.Tax, header.Total, lines, header.Updated, header.Token,
            header.EvidenceType);
    }

    private static byte[] ParseToken(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException exception)
        { throw new PurchasingValidationException("ConcurrencyToken is invalid.", exception); }
    }

    private static void AddDecimal(SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }
}
