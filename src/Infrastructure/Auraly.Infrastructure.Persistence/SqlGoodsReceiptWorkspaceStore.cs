using System.Data;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlGoodsReceiptWorkspaceStore(
    SqlServerConnectionFactory connections,
    TimeProvider timeProvider,
    IAuralyIdGenerator ids) : IGoodsReceiptWorkspaceStore
{
    public async Task<IReadOnlyDictionary<Guid, decimal?>> GetUnitGrossWeightsAsync(
        PurchasingUserIdentity user, IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0) return new Dictionary<Guid, decimal?>();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT product.ProductId,product.UnitGrossWeightKg
            FROM dbo.Products product
            JOIN OPENJSON(@ProductIdsJson) WITH (ProductId UNIQUEIDENTIFIER '$') requested
              ON requested.ProductId=product.ProductId
            WHERE product.TenantId=@TenantId AND product.IsActive=1;
            """;
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@ProductIdsJson", JsonSerializer.Serialize(productIds.Distinct()));
        var result = new Dictionary<Guid, decimal?>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetGuid(0)] = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
        return result;
    }

    public async Task<GoodsReceiptWorkspaceOptions> GetOptionsAsync(
        PurchasingUserIdentity user, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT WarehouseId,Code,Name
            FROM dbo.Warehouses
            WHERE BusinessId=@BusinessId AND IsActive=1 AND IsSystem=0 AND UseForGoodsReceipts=1
            ORDER BY Name,Code;
            SELECT SupplierId,Identification,Name,PurchaseEvidencePolicy
            FROM dbo.Suppliers
            WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND IsActive=1
            ORDER BY Name,Identification;
            SELECT Code,Label,Description
            FROM reference.Options
            WHERE CatalogCode=N'purchase-evidence-type' AND IsActive=1
            ORDER BY SortOrder,Label;
            WITH latest AS (
              SELECT source.*,ROW_NUMBER() OVER(PARTITION BY source.RuleId ORDER BY source.Version DESC) AS rn
              FROM dbo.WithholdingRules source
              WHERE source.BusinessId=@BusinessId AND source.Direction=N'Purchase'
            )
            SELECT ConceptCode AS Code,CONCAT(ConceptCode,N' · ',MIN(Name)) AS Label
            FROM latest
            WHERE rn=1 AND IsActive=1 AND ConceptCode IS NOT NULL
            GROUP BY ConceptCode
            ORDER BY ConceptCode;
            SELECT city.Code,CONCAT(city.Name,N' · ',division.Name) AS Label
            FROM dbo.Cities city
            JOIN dbo.AdministrativeDivisions division
              ON division.AdministrativeDivisionId=city.AdministrativeDivisionId
            JOIN dbo.Countries country ON country.CountryId=division.CountryId
            WHERE country.Code=N'CO' AND country.IsActive=1
              AND division.IsActive=1 AND city.IsActive=1
            ORDER BY city.Name,division.Name,city.Code;
            SELECT CatalogCode,Code,Label,Description
            FROM reference.Options
            WHERE CatalogCode IN(N'purchase-cost-evidence-type',N'purchase-cost-kind',
              N'purchase-cost-treatment',N'purchase-cost-allocation-method',
              N'purchase-tax-rate',N'purchase-tax-treatment',N'purchase-currency',
              N'exchange-rate-source',N'purchase-cost-association-mode') AND IsActive=1
            ORDER BY CatalogCode,SortOrder,Label;
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
        await reader.NextResultAsync(cancellationToken);
        var concepts = new List<GoodsReceiptTaxOption>();
        while (await reader.ReadAsync(cancellationToken))
            concepts.Add(new(reader.GetString(0), reader.GetString(1)));
        await reader.NextResultAsync(cancellationToken);
        var jurisdictions = new List<GoodsReceiptTaxOption>();
        while (await reader.ReadAsync(cancellationToken))
            jurisdictions.Add(new(reader.GetString(0), reader.GetString(1)));
        await reader.NextResultAsync(cancellationToken);
        var references = new Dictionary<string, List<PurchaseEvidenceTypeOption>>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var catalog = reader.GetString(0);
            if (!references.TryGetValue(catalog, out var values))
                references[catalog] = values = [];
            values.Add(new(reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        List<PurchaseEvidenceTypeOption> Options(string catalog) =>
            references.TryGetValue(catalog, out var values) ? values : [];
        return new(warehouses, suppliers, evidenceTypes, concepts, jurisdictions,
            Options("purchase-cost-evidence-type"), Options("purchase-cost-kind"),
            Options("purchase-cost-treatment"), Options("purchase-cost-allocation-method"),
            Options("purchase-tax-rate"), Options("purchase-tax-treatment"),
            Options("purchase-currency"),
            Options("exchange-rate-source"), Options("purchase-cost-association-mode"));
    }

    public async Task<GoodsReceiptProductPage> FindProductsAsync(
        PurchasingUserIdentity user, Guid supplierId, string? search,
        bool includeUnassociated, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SET NOCOUNT ON;

            DECLARE @TenantId UNIQUEIDENTIFIER,@SharesProductPrices BIT;
            SELECT @TenantId=TenantId,@SharesProductPrices=SharesProductPrices
            FROM dbo.Businesses
            WHERE BusinessId=@BusinessId;

            IF NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers
              WHERE SupplierId=@SupplierId AND TenantId=@TenantId AND IsActive=1)
              THROW 51120,'El proveedor no está activo en este tenant.',1;

            SELECT COUNT(*)
            FROM dbo.Products p
            LEFT JOIN dbo.SupplierProducts sp
              ON sp.ProductId=p.ProductId AND sp.TenantId=@TenantId
             AND sp.SupplierId=@SupplierId AND sp.IsActive=1
            WHERE p.TenantId=@TenantId AND p.IsActive=1
              AND NOT EXISTS(SELECT 1 FROM dbo.ProductLinks link
                             WHERE link.TenantId=@TenantId AND link.ChildProductId=p.ProductId
                               AND link.SharesInventory=1 AND link.IsActive=1)
              AND (@IncludeUnassociated=1 OR sp.SupplierProductId IS NOT NULL)
              AND (@Search IS NULL OR p.ProductCode LIKE N'%'+@Search+N'%'
                   OR p.Reference LIKE N'%'+@Search+N'%' OR p.Name LIKE N'%'+@Search+N'%'
                   OR sp.SupplierProductCode LIKE N'%'+@Search+N'%'
                   OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes pb
                              WHERE pb.ProductId=p.ProductId AND pb.TenantId=@TenantId
                                AND pb.IsActive=1 AND pb.Barcode LIKE N'%'+@Search+N'%'))
            OPTION (RECOMPILE);

            CREATE TABLE #RequestedProducts(
              ProductId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
            );

            INSERT #RequestedProducts(ProductId)
            SELECT p.ProductId
            FROM dbo.Products p
            LEFT JOIN dbo.SupplierProducts sp
              ON sp.ProductId=p.ProductId AND sp.TenantId=@TenantId
             AND sp.SupplierId=@SupplierId AND sp.IsActive=1
            WHERE p.TenantId=@TenantId AND p.IsActive=1
              AND NOT EXISTS(SELECT 1 FROM dbo.ProductLinks link
                             WHERE link.TenantId=@TenantId AND link.ChildProductId=p.ProductId
                               AND link.SharesInventory=1 AND link.IsActive=1)
              AND (@IncludeUnassociated=1 OR sp.SupplierProductId IS NOT NULL)
              AND (@Search IS NULL OR p.ProductCode LIKE N'%'+@Search+N'%'
                   OR p.Reference LIKE N'%'+@Search+N'%' OR p.Name LIKE N'%'+@Search+N'%'
                   OR sp.SupplierProductCode LIKE N'%'+@Search+N'%'
                   OR EXISTS (SELECT 1 FROM dbo.ProductBarcodes pb
                              WHERE pb.ProductId=p.ProductId AND pb.TenantId=@TenantId
                                AND pb.IsActive=1 AND pb.Barcode LIKE N'%'+@Search+N'%'))
            ORDER BY CASE WHEN sp.SupplierProductId IS NULL THEN 1 ELSE 0 END,p.Name,p.ProductId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            OPTION (RECOMPILE);

            SELECT p.ProductId,COALESCE(p.ProductCode,N''),p.Reference,p.Name,
                   sp.SupplierProductCode,latest.LatestUnitCost,averageCost.AverageUnitCost,
                   COALESCE(tp.Code,N'00'),COALESCE(tp.Rate,0),
                   COALESCE(p.PurchaseTaxTreatment,N'DeductibleInputVat'),
                   COALESCE(b.Barcodes,N''),
                   COALESCE(p.BaseUnitCode,N'EA'),
                   CONVERT(BIT,CASE WHEN sp.SupplierProductId IS NULL THEN 0 ELSE 1 END),
                   COALESCE(sp.PurchasePresentationName,N'Unidad'),COALESCE(sp.UnitsPerPresentation,1),
                   COALESCE(sp.IsPrimary,CONVERT(BIT,0)),p.UnitGrossWeightKg
            FROM #RequestedProducts requested
            INNER JOIN dbo.Products p ON p.ProductId=requested.ProductId
            LEFT JOIN dbo.SupplierProducts sp
              ON sp.ProductId=p.ProductId AND sp.TenantId=@TenantId
             AND sp.SupplierId=@SupplierId AND sp.IsActive=1
            LEFT JOIN dbo.TaxProfiles tp
              ON tp.TaxProfileId=COALESCE(p.PurchaseTaxProfileId,p.TaxProfileId)
             AND tp.TenantId=@TenantId
            LEFT JOIN dbo.SupplierProductLatestCosts latest
              ON latest.BusinessId=@BusinessId AND latest.SupplierId=@SupplierId
             AND latest.ProductId=p.ProductId
            OUTER APPLY (
              SELECT COALESCE(
                SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.InventoryValue ELSE 0 END)
                  / NULLIF(SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.QuantityOnHand ELSE 0 END),0),
                MAX(balance.AverageUnitCost)) AverageUnitCost
              FROM dbo.InventoryBalances balance
              INNER JOIN dbo.Businesses balanceBusiness ON balanceBusiness.BusinessId=balance.BusinessId
              WHERE balance.ProductId=p.ProductId
                AND (@SharesProductPrices=1 AND balanceBusiness.TenantId=@TenantId
                     AND balanceBusiness.SharesProductPrices=1 OR balance.BusinessId=@BusinessId)
            ) averageCost
            OUTER APPLY (
              SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),pb.Barcode),N'|') AS Barcodes
              FROM dbo.ProductBarcodes pb
              WHERE pb.ProductId=p.ProductId AND pb.TenantId=@TenantId AND pb.IsActive=1
            ) b
            ORDER BY CASE WHEN sp.SupplierProductId IS NULL THEN 1 ELSE 0 END,p.Name,p.ProductId
            OPTION (RECOMPILE);
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
                DECLARE @TenantId uniqueidentifier=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
                IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers
                               WHERE SupplierId=@SupplierId AND TenantId=@TenantId AND IsActive=1)
                  THROW 51120,'El proveedor no está activo en esta sede.',1;
                IF NOT EXISTS (SELECT 1 FROM dbo.Products
                               WHERE ProductId=@ProductId AND IsActive=1
                                 AND TenantId=@TenantId)
                  THROW 51125,'El producto no pertenece a esta sede.',1;
                IF EXISTS (SELECT 1 FROM dbo.ProductLinks
                           WHERE TenantId=@TenantId AND ChildProductId=@ProductId
                             AND SharesInventory=1 AND IsActive=1)
                  THROW 51125,'Los productos vinculados al inventario deben recibirse mediante su producto principal.',1;

                IF @IsPrimary=1
                  UPDATE dbo.SupplierProducts SET IsPrimary=0
                  WHERE TenantId=@TenantId AND ProductId=@ProductId AND IsActive=1;

                IF EXISTS (SELECT 1 FROM dbo.SupplierProducts WITH (UPDLOCK,HOLDLOCK)
                           WHERE TenantId=@TenantId AND SupplierId=@SupplierId AND ProductId=@ProductId)
                  UPDATE dbo.SupplierProducts
                  SET SupplierProductCode=COALESCE(@SupplierProductCode,SupplierProductCode),
                      PurchasePresentationName=@PresentationName,UnitsPerPresentation=@UnitsPerPresentation,
                      IsPrimary=@IsPrimary,IsActive=1
                  WHERE TenantId=@TenantId AND SupplierId=@SupplierId AND ProductId=@ProductId;
                ELSE
                  INSERT dbo.SupplierProducts
                    (SupplierProductId,TenantId,ProductId,SupplierId,SupplierProductCode,PurchasePresentationName,UnitsPerPresentation,IsPrimary,IsActive,CreatedAt)
                  VALUES
                    (@SupplierProductId,@TenantId,@ProductId,@SupplierId,@SupplierProductCode,@PresentationName,@UnitsPerPresentation,@IsPrimary,1,@Now);

                SELECT p.ProductId,COALESCE(p.ProductCode,N''),p.Reference,p.Name,
                       sp.SupplierProductCode,latest.LatestUnitCost,averageCost.AverageUnitCost,
                       COALESCE(tp.Code,N'00'),COALESCE(tp.Rate,0),
                       COALESCE(p.PurchaseTaxTreatment,N'DeductibleInputVat'),
                       COALESCE(b.Barcodes,N''),COALESCE(p.BaseUnitCode,N'EA'),CONVERT(BIT,1),
                       sp.PurchasePresentationName,sp.UnitsPerPresentation,sp.IsPrimary,p.UnitGrossWeightKg
                FROM dbo.Products p
                INNER JOIN dbo.SupplierProducts sp
                  ON sp.ProductId=p.ProductId
                 AND sp.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                 AND sp.SupplierId=@SupplierId AND sp.IsActive=1
                LEFT JOIN dbo.TaxProfiles tp
                  ON tp.TaxProfileId=COALESCE(p.PurchaseTaxProfileId,p.TaxProfileId)
                 AND tp.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                LEFT JOIN dbo.SupplierProductLatestCosts latest
                  ON latest.BusinessId=@BusinessId AND latest.SupplierId=@SupplierId
                 AND latest.ProductId=p.ProductId
                OUTER APPLY (
                  SELECT COALESCE(
                    SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.InventoryValue ELSE 0 END)
                      / NULLIF(SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.QuantityOnHand ELSE 0 END),0),
                    MAX(balance.AverageUnitCost)) AverageUnitCost
                  FROM dbo.InventoryBalances balance
                  INNER JOIN dbo.Businesses balanceBusiness ON balanceBusiness.BusinessId=balance.BusinessId
                  INNER JOIN dbo.Businesses currentBusiness ON currentBusiness.BusinessId=@BusinessId
                  WHERE balance.ProductId=p.ProductId
                    AND (currentBusiness.SharesProductPrices=1 AND balanceBusiness.TenantId=currentBusiness.TenantId
                         AND balanceBusiness.SharesProductPrices=1 OR balance.BusinessId=@BusinessId)
                ) averageCost
                OUTER APPLY (
                  SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),pb.Barcode),N'|') AS Barcodes
                  FROM dbo.ProductBarcodes pb
                  WHERE pb.ProductId=p.ProductId
                    AND pb.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND pb.IsActive=1
                ) b
                WHERE p.ProductId=@ProductId
                  AND p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId);
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
                throw new PurchasingValidationException("No fue posible consultar el producto asociado.");
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
            throw new PurchasingConflictException("El producto ya está asociado con este proveedor.");
        }
    }

    private static GoodsReceiptProductOption ReadProduct(SqlDataReader reader)
    {
        var barcodes = reader.GetString(10).Split('|', StringSplitOptions.RemoveEmptyEntries);
        return new(
            reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6),
            reader.GetString(7), reader.GetDecimal(8), reader.GetString(9), barcodes, reader.GetString(11), reader.GetBoolean(12),
            reader.GetString(13), reader.GetDecimal(14), reader.GetBoolean(15),
            reader.IsDBNull(16) ? null : reader.GetDecimal(16));
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
              WHERE d.BusinessId=@BusinessId AND d.CostEditOfReceiptId IS NULL
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
              WHERE d.BusinessId=@BusinessId AND d.CostEditOfReceiptId IS NULL
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
                   r.AcceptedAt,r.ProcessedAt,r.PurchaseEvidenceType,r.PurchaseOrderId,
                   payable.PayableId,payable.Status,payable.OutstandingAmount,payable.CurrencyCode,
                   payable.PartySiteId,site.Name,payable.DueDate
            FROM dbo.GoodsReceipts r
            INNER JOIN dbo.Businesses b
              ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=r.SupplierId
            -- The source-line constraint and unique source key give a native
            -- receipt at most one obligation. This reads its owner's balance.
            LEFT JOIN dbo.Payables payable ON payable.SourceDocumentId=r.GoodsReceiptId
              AND payable.SourceDocumentType=N'GoodsReceipt' AND payable.SourceLineId IS NULL
              AND payable.BusinessId=r.BusinessId AND payable.SupplierId=r.SupplierId
            LEFT JOIN dbo.PartySites site ON site.PartySiteId=payable.PartySiteId AND site.PartyId=s.PartyId
            WHERE r.GoodsReceiptId=@DocumentId AND r.BusinessId=@BusinessId;

            SELECT l.LineNumber,l.ProductId,l.DescriptionSnapshot,l.Quantity,l.UnitCost,
                   l.DiscountAmount,l.TaxCode,l.TaxRate,l.TaxTreatment,l.NetAmount,
                   l.TaxAmount,l.LineTotal,l.PresentationNameSnapshot,
                   l.PresentationQuantity,l.UnitsPerPresentation,l.PurchaseOrderLineId,l.OverReceiptReason,l.OverReceiptAuthorized,l.CommercialDiscountPercent
            FROM dbo.GoodsReceiptLines l
            WHERE l.GoodsReceiptId=@DocumentId
            ORDER BY l.LineNumber;

            SELECT s.GrossAmount,s.WithholdingTotal,s.NetAmount,s.AdjustmentsJson
            FROM dbo.DocumentWithholdingSnapshots s
            WHERE s.DocumentId=@DocumentId AND s.DocumentType=N'GoodsReceipt'
              AND s.BusinessId=@BusinessId;

            SELECT l.RuleId,l.RuleVersion,l.RuleCode,l.Name,l.Kind,l.BaseKind,
                   l.TaxableBase,l.Rate,l.Amount,l.JurisdictionCode,l.AccountId,l.ManualLineId
            FROM dbo.DocumentWithholdingLines l
            INNER JOIN dbo.DocumentWithholdingSnapshots s
              ON s.DocumentId=l.DocumentId AND s.DocumentType=l.DocumentType
            WHERE l.DocumentId=@DocumentId AND l.DocumentType=N'GoodsReceipt'
              AND s.BusinessId=@BusinessId
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
        var purchaseOrderId = reader.IsDBNull(19) ? (Guid?)null : reader.GetGuid(19);
        var payable = reader.IsDBNull(20) ? null : new GoodsReceiptPayableView(
            reader.GetGuid(20), reader.GetString(21), reader.GetDecimal(22), reader.GetString(23),
            reader.IsDBNull(24) ? null : reader.GetGuid(24), reader.IsDBNull(25) ? null : reader.GetString(25),
            reader.GetDateTimeOffset(26));

        await reader.NextResultAsync(cancellationToken);
        var lines = new List<GoodsReceiptLineSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(
                reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetString(6), reader.GetDecimal(7), reader.GetString(8),
                reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetString(12), reader.GetDecimal(13), reader.GetDecimal(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15),
                reader.IsDBNull(16) ? null : reader.GetString(16), reader.GetBoolean(17),
                CommercialDiscountPercent: reader.IsDBNull(18) ? null : reader.GetDecimal(18)));

        WithholdingCalculationSnapshot? withholding = null;
        if (await reader.NextResultAsync(cancellationToken) &&
            await reader.ReadAsync(cancellationToken))
        {
            var withholdingLines = new List<WithholdingLineSnapshot>();
            var grossAmount = reader.GetDecimal(0);
            var withholdingTotal = reader.GetDecimal(1);
            var netPayable = reader.GetDecimal(2);
            var adjustments = reader.IsDBNull(3) ? null :
                System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<WithholdingAdjustmentSnapshot>>(
                    reader.GetString(3));
            if (await reader.NextResultAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken))
                    withholdingLines.Add(new WithholdingLineSnapshot(
                        reader.IsDBNull(0) ? Guid.Empty : reader.GetGuid(0),
                        reader.IsDBNull(1) ? 0 : reader.GetInt32(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4), reader.GetString(5),
                        reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetGuid(10),
                        reader.IsDBNull(11) ? null : reader.GetGuid(11)));
            withholding = new WithholdingCalculationSnapshot(
                grossAmount, withholdingTotal, netPayable, withholdingLines, adjustments);
        }

        await reader.DisposeAsync();
        GoodsReceiptDocumentPayload? immutable = null;
        await using (var payloadCommand = new SqlCommand("""
            SELECT PayloadJson FROM dbo.DocumentProcessingPayloads
            WHERE DocumentId=@DocumentId AND DocumentType=N'GoodsReceipt' AND BusinessId=@BusinessId;
            """, connection))
        {
            payloadCommand.Parameters.AddWithValue("@DocumentId", documentId);
            payloadCommand.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            var payloadJson = await payloadCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (payloadJson is not null) immutable = GoodsReceiptContractSerializer.Deserialize(payloadJson);
        }
        var accountingStatuses = new List<GoodsReceiptAccountingStatus>();
        var laterCosts = new List<GoodsReceiptVoucherCostSnapshot>();
        GoodsReceiptCostEditSnapshot? latestEdit = null;
        await using (var accountingCommand = new SqlCommand("""
            SELECT j.SourceDocumentId,j.SourceDocumentType,j.Status,j.LastErrorCode,j.LastErrorMessage
            FROM dbo.AccountingPostingJobs j
            WHERE j.BusinessId=@BusinessId AND
              (j.SourceDocumentId=@DocumentId OR j.SourceDocumentId IN
                (SELECT CostDocumentId FROM purchasing.GoodsReceiptCostDocuments WHERE GoodsReceiptId=@DocumentId AND SourceEntryId IS NULL)
                OR j.SourceDocumentId IN (SELECT RevisionId FROM purchasing.GoodsReceiptCostRevisions
                  WHERE GoodsReceiptId=@DocumentId AND BusinessId=@BusinessId))
            ORDER BY CASE WHEN j.SourceDocumentId=@DocumentId THEN 0 ELSE 1 END,j.OccurredAt,j.SourceDocumentId;
            SELECT job.DocumentId,job.DocumentType,job.Status,job.LastError
            FROM dbo.DocumentProcessingJobs job
            JOIN purchasing.GoodsReceiptCostRevisions revision
              ON revision.RevisionId=job.DocumentId AND revision.BusinessId=job.BusinessId
            WHERE revision.GoodsReceiptId=@DocumentId AND revision.BusinessId=@BusinessId
              AND job.DocumentType=N'GoodsReceiptCostRevision'
              AND job.Status<>N'Completed'
              AND NOT EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs posting
                WHERE posting.SourceDocumentId=job.DocumentId
                  AND posting.SourceDocumentType=job.DocumentType
                  AND posting.BusinessId=job.BusinessId)
            ORDER BY revision.RevisionNumber;
            SELECT revision.RevisionNumber,revision.Status,cost.CostDocumentId,entry.EntryId,
              entry.SourceDocumentId,entry.SourceDocumentType,entry.EntryNumber,cost.AssociationMode,
              (SELECT line.LineNumber AS EntryLineNumber,original.AccountId,
                  original.PartyId,original.CostCenterId,line.FunctionalAmount,line.AllocationMethod,
                  JSON_QUERY((SELECT allocation.ReceiptLineNumber,
                    allocation.AllocationFactor AS Factor,allocation.FunctionalAmount
                    FROM purchasing.GoodsReceiptCostAllocations allocation
                    WHERE allocation.CostDocumentId=line.CostDocumentId
                      AND allocation.CostLineNumber=line.LineNumber
                    ORDER BY allocation.ReceiptLineNumber FOR JSON PATH)) AS Allocations
                FROM purchasing.GoodsReceiptCostLines line
                JOIN dbo.AccountingEntryLines original ON original.EntryId=line.SourceEntryId
                  AND original.LineNumber=line.SourceEntryLineNumber
                WHERE line.CostDocumentId=cost.CostDocumentId
                ORDER BY line.LineNumber FOR JSON PATH) LinesJson
            FROM purchasing.GoodsReceiptCostRevisions revision
            JOIN purchasing.GoodsReceiptCostDocuments cost
              ON cost.CostRevisionId=revision.RevisionId
            JOIN dbo.AccountingEntries entry ON entry.EntryId=cost.SourceEntryId
              AND entry.BusinessId=revision.BusinessId
            WHERE revision.GoodsReceiptId=@DocumentId AND revision.BusinessId=@BusinessId
            ORDER BY revision.RevisionNumber,cost.CostDocumentId;
            SELECT TOP(1) payload.PayloadJson
            FROM purchasing.GoodsReceiptCostRevisions revision
            JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=revision.RevisionId
             AND payload.DocumentType=N'GoodsReceiptCostRevision'
             AND payload.BusinessId=revision.BusinessId
            WHERE revision.GoodsReceiptId=@DocumentId AND revision.BusinessId=@BusinessId
              AND revision.Status=N'Processed'
              AND JSON_QUERY(payload.PayloadJson,'$.costEdit') IS NOT NULL
            ORDER BY revision.RevisionNumber DESC;
            """, connection))
        {
            accountingCommand.Parameters.AddWithValue("@DocumentId", documentId);
            accountingCommand.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            await using var accountingReader = await accountingCommand.ExecuteReaderAsync(cancellationToken);
            while (await accountingReader.ReadAsync(cancellationToken))
                accountingStatuses.Add(new(accountingReader.GetGuid(0), accountingReader.GetString(1),
                    accountingReader.GetString(2), accountingReader.IsDBNull(3) ? null : accountingReader.GetString(3),
                    accountingReader.IsDBNull(4) ? null : accountingReader.GetString(4)));
            await accountingReader.NextResultAsync(cancellationToken);
            while (await accountingReader.ReadAsync(cancellationToken))
                accountingStatuses.Add(new(accountingReader.GetGuid(0), accountingReader.GetString(1),
                    accountingReader.GetString(2), "DocumentProcessing",
                    accountingReader.IsDBNull(3) ? null : accountingReader.GetString(3)));
            await accountingReader.NextResultAsync(cancellationToken);
            while (await accountingReader.ReadAsync(cancellationToken))
            {
                var costLines = accountingReader.IsDBNull(8) ? [] :
                    JsonSerializer.Deserialize<List<GoodsReceiptVoucherCostLineSnapshot>>(
                        accountingReader.GetString(8), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("La revisión no conserva sus partidas de costo.");
                laterCosts.Add(new(accountingReader.GetGuid(2), accountingReader.GetGuid(3),
                    accountingReader.GetGuid(4), accountingReader.GetString(5),
                    accountingReader.GetString(6), accountingReader.GetString(7),
                    costLines, accountingReader.GetInt32(0), accountingReader.GetString(1)));
            }
            await accountingReader.NextResultAsync(cancellationToken);
            if (await accountingReader.ReadAsync(cancellationToken))
                latestEdit = GoodsReceiptContractSerializer
                    .DeserializeCostRevision(accountingReader.GetString(0)).CostEdit;
        }

        var laterByLine = laterCosts
            .Where(document => document.Mode == GoodsReceiptCostAssociationModes.Distribute &&
                document.CostRevisionStatus == "Processed")
            .SelectMany(document => document.Lines)
            .SelectMany(line => line.Allocations)
            .GroupBy(allocation => allocation.ReceiptLineNumber)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.FunctionalAmount));
        var currentLines = (latestEdit?.CurrentLines ?? immutable?.Lines ?? lines).Select(line =>
        {
            var laterAmount = latestEdit is null ? laterByLine.GetValueOrDefault(line.LineNumber) : 0m;
            return laterAmount == 0m ? line : line with
            {
                AllocatedLandedCostAmount = line.AllocatedLandedCostAmount + laterAmount,
                RecognizedInventoryCostAmount = line.RecognizedInventoryCostAmount + laterAmount
            };
        }).ToArray();
        return new GoodsReceiptDetail(
            documentId, number, status, warehouseId, warehouseName, supplierId,
            supplierName, supplierInvoiceNumber, supplierInvoiceDate, receivedAt,
            createsPayable, dueDate, currencyCode, notes,
            latestEdit?.CurrentLines.Sum(line => line.NetAmount) ?? netAmount,
            latestEdit?.CurrentLines.Sum(line => line.TaxAmount) ?? taxAmount,
            latestEdit?.CurrentLines.Sum(line => line.LineTotal) ?? grandTotal,
            acceptedAt, processedAt, currentLines, purchaseEvidenceType,
            purchaseOrderId, latestEdit?.CurrentWithholding ?? withholding ?? immutable?.Withholding,
            immutable?.ExchangeRate ?? 1, immutable?.ExchangeRateDate,
            immutable?.ExchangeRateSource ?? "FunctionalCurrency",
            latestEdit?.CurrentLines.Sum(line => line.FunctionalNetAmount) ?? immutable?.FunctionalNetAmount ?? netAmount,
            latestEdit?.CurrentLines.Sum(line => line.FunctionalTaxAmount) ?? immutable?.FunctionalTaxAmount ?? taxAmount,
            latestEdit?.CurrentFunctionalGrandTotal ?? immutable?.FunctionalGrandTotal ?? grandTotal,
            latestEdit?.CurrentAdditionalDocuments ?? immutable?.AdditionalCostDocuments,
            accountingStatuses,
            latestEdit?.CurrentReferencedDocuments ??
                (immutable?.ReferencedCostDocuments ?? []).Concat(laterCosts).ToArray(), payable,
            latestEdit?.CurrentFinancialDiscount ?? immutable?.FinancialDiscount,
            immutable?.PartySiteId, immutable?.WithholdingConceptCode);
    }
    public async Task<GoodsReceiptCostEditState?> GetCostEditStateAsync(
        PurchasingUserIdentity user, Guid receiptId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COALESCE(MAX(revision.RevisionNumber),0),draft.GoodsReceiptDraftId
            FROM dbo.GoodsReceipts receipt
            JOIN dbo.Businesses business ON business.BusinessId=receipt.BusinessId
              AND business.TenantId=@TenantId
            LEFT JOIN purchasing.GoodsReceiptCostRevisions revision
              ON revision.GoodsReceiptId=receipt.GoodsReceiptId
                AND revision.BusinessId=receipt.BusinessId
            LEFT JOIN dbo.GoodsReceiptDrafts draft
              ON draft.CostEditOfReceiptId=receipt.GoodsReceiptId
                AND draft.BusinessId=receipt.BusinessId
            WHERE receipt.GoodsReceiptId=@ReceiptId AND receipt.BusinessId=@BusinessId
              AND receipt.Status=N'Processed'
            GROUP BY draft.GoodsReceiptDraftId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@ReceiptId", receiptId);
        int revisionNumber;
        Guid? draftId;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            revisionNumber = reader.GetInt32(0);
            draftId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }
        var draft = draftId is null ? null :
            await LoadDraftAsync(connection, null, user.BusinessId, draftId.Value, cancellationToken);
        return new(receiptId, revisionNumber, draft);
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
            await ValidateCostEditDraftAsync(connection, transaction, user, request, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var existingToken = await LoadTokenAsync(connection, transaction, user.BusinessId, request.DraftId, cancellationToken);
            if (existingToken is null)
            {
                if (request.ConcurrencyToken is not null)
                    throw new PurchasingConflictException("El borrador ya no existe.");
                await InsertDraftAsync(connection, transaction, user, request, calculation, now, cancellationToken);
            }
            else
            {
                if (request.ConcurrencyToken is null ||
                    !existingToken.AsSpan().SequenceEqual(ParseToken(request.ConcurrencyToken)))
                    throw new PurchasingConflictException("El borrador cambió en otra sesión. Recárgalo antes de continuar.");
                await UpdateDraftAsync(connection, transaction, user, request, calculation, existingToken, now, cancellationToken);
                await DeleteLinesAsync(connection, transaction, request.DraftId, cancellationToken);
            }
            await InsertLinesAsync(connection, transaction, request.DraftId, request.Lines, calculation, cancellationToken);
            var saved = await LoadDraftAsync(connection, transaction, user.BusinessId, request.DraftId, cancellationToken)
                ?? throw new InvalidOperationException("No fue posible cargar el borrador guardado.");
            await transaction.CommitAsync(cancellationToken);
            return saved;
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task ValidateCostEditDraftAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        SaveGoodsReceiptDraftRequest request, CancellationToken cancellationToken)
    {
        if (request.CostEditOfReceiptId is null)
        {
            if (request.BaseCostRevisionNumber is not null)
                throw new PurchasingValidationException("La versión del ajuste requiere una recepción.");
            return;
        }
        if (request.CostEditOfReceiptId == Guid.Empty || request.BaseCostRevisionNumber is null or < 0)
            throw new PurchasingValidationException("La edición de costos requiere recepción y versión válidas.");

        await using var command = new SqlCommand("""
            SELECT receipt.WarehouseId,receipt.SupplierId,receipt.PartySiteId,receipt.PurchaseOrderId,
              receipt.PurchaseEvidenceType,receipt.SupplierInvoiceNumber,receipt.SupplierInvoiceDate,
              receipt.ReceivedAt,receipt.CreatesPayable,receipt.DueDate,receipt.CurrencyCode,
              receipt.ExchangeRate,receipt.ExchangeRateDate,receipt.ExchangeRateSource,
              COALESCE((SELECT MAX(revision.RevisionNumber)
                FROM purchasing.GoodsReceiptCostRevisions revision WITH(UPDLOCK,HOLDLOCK)
                WHERE revision.GoodsReceiptId=receipt.GoodsReceiptId
                  AND revision.BusinessId=receipt.BusinessId),0)
            FROM dbo.GoodsReceipts receipt WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business ON business.BusinessId=receipt.BusinessId
              AND business.TenantId=@TenantId
            WHERE receipt.GoodsReceiptId=@ReceiptId AND receipt.BusinessId=@BusinessId
              AND receipt.Status=N'Processed';
            SELECT line.LineNumber,line.ProductId,line.Quantity,line.PresentationQuantity,
              line.UnitsPerPresentation,line.PurchaseOrderLineId,line.TaxCode,line.TaxRate,
              line.TaxTreatment
            FROM dbo.GoodsReceiptLines line
            WHERE line.GoodsReceiptId=@ReceiptId ORDER BY line.LineNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@ReceiptId", request.CostEditOfReceiptId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new PurchasingConflictException("La recepción ya no está confirmada o no pertenece a esta sede.");
        var physicalHeaderMatches =
            reader.GetGuid(0) == request.WarehouseId && reader.GetGuid(1) == request.SupplierId &&
            (reader.IsDBNull(2) ? null : reader.GetGuid(2)) == request.PartySiteId &&
            (reader.IsDBNull(3) ? null : reader.GetGuid(3)) == request.PurchaseOrderId &&
            reader.GetString(4) == request.PurchaseEvidenceType &&
            (reader.IsDBNull(5) ? null : reader.GetString(5)) == request.SupplierInvoiceNumber &&
            (reader.IsDBNull(6) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTimeOffset(6).Date)) ==
              (request.SupplierInvoiceDate is null ? null : DateOnly.FromDateTime(request.SupplierInvoiceDate.Value.Date)) &&
            reader.GetDateTimeOffset(7) == request.ReceivedAt &&
            reader.GetBoolean(8) == request.CreatesPayable &&
            (reader.IsDBNull(9) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTimeOffset(9).Date)) ==
              (request.DueDate is null ? null : DateOnly.FromDateTime(request.DueDate.Value.Date)) &&
            reader.GetString(10) == request.CurrencyCode &&
            reader.GetDecimal(11) == request.ExchangeRate &&
            (reader.IsDBNull(12) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(12))) == request.ExchangeRateDate &&
            reader.GetString(13) == request.ExchangeRateSource;
        var revisionNumber = reader.GetInt32(14);
        await reader.NextResultAsync(cancellationToken);
        var sourceLines = new List<(int Number, Guid ProductId, decimal Quantity,
            decimal PresentationQuantity, decimal UnitsPerPresentation, Guid? OrderLineId,
            string TaxCode, decimal TaxRate, string TaxTreatment)>();
        while (await reader.ReadAsync(cancellationToken))
            sourceLines.Add((reader.GetInt32(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetString(6),
                reader.GetDecimal(7), reader.GetString(8)));
        var requestedLines = request.Lines.OrderBy(line => line.LineNumber).ToArray();
        if (!physicalHeaderMatches || sourceLines.Count != requestedLines.Length ||
            sourceLines.Where((source, index) =>
                source.Number != requestedLines[index].LineNumber ||
                source.ProductId != requestedLines[index].ProductId ||
                source.Quantity != requestedLines[index].Quantity ||
                source.PresentationQuantity != requestedLines[index].PresentationQuantity ||
                source.UnitsPerPresentation != requestedLines[index].UnitsPerPresentation ||
                source.OrderLineId != requestedLines[index].PurchaseOrderLineId ||
                source.TaxCode != requestedLines[index].TaxCode ||
                source.TaxRate != requestedLines[index].TaxRate ||
                source.TaxTreatment != requestedLines[index].TaxTreatment).Any())
            throw new PurchasingValidationException(
                "La edición solo permite cambiar costos; los datos físicos y fiscales de la entrada permanecen iguales.");
        if (revisionNumber != request.BaseCostRevisionNumber)
            throw new PurchasingConflictException(
                "Los costos de esta recepción cambiaron. Vuelve a abrirla antes de guardar el borrador.");
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
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1) return;

        await using var exists = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.GoodsReceiptDrafts
            WHERE GoodsReceiptDraftId=@DraftId AND BusinessId=@BusinessId;
            """, connection);
        exists.Parameters.AddWithValue("@DraftId", draftId);
        exists.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken)) != 0)
            throw new PurchasingConflictException("El borrador ya no existe o cambió en otra sesión. Recárgalo antes de continuar.");
    }

    private static async Task ValidateScopeAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        SaveGoodsReceiptDraftRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51121,'La sede no pertenece a la empresa autenticada.',1;
            IF EXISTS(SELECT 1 FROM OPENJSON(@ReferencedCostsJson) WITH(EntryId uniqueidentifier) requested
              WHERE NOT EXISTS(SELECT 1 FROM dbo.AccountingEntries entry WHERE entry.EntryId=requested.EntryId
                AND entry.BusinessId=@BusinessId AND entry.TenantId=@TenantId))
              THROW 51130,'Un comprobante no está contabilizado o no pertenece a esta sede.',1;
            IF @WarehouseId IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId
                AND IsActive=1 AND IsSystem=0 AND UseForGoodsReceipts=1)
              THROW 51122,'La bodega no pertenece a esta sede.',1;
            IF @SupplierId IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId
                AND TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND IsActive=1)
              THROW 51123,'El proveedor no está activo en esta sede.',1;
            IF EXISTS (
              SELECT 1 FROM OPENJSON(@CostDocumentsJson)
              WITH (SupplierId uniqueidentifier '$.SupplierId') x
              LEFT JOIN dbo.Suppliers s ON s.SupplierId=x.SupplierId
                AND s.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND s.IsActive=1
              WHERE s.SupplierId IS NULL)
              THROW 51127,'Un proveedor de costo adicional no está activo en esta sede.',1;
            IF @CurrencyCode<>N'COP' AND NOT EXISTS (
              SELECT 1 FROM reference.Options
              WHERE CatalogCode=N'exchange-rate-source' AND Code=@ExchangeRateSource AND IsActive=1)
              THROW 51128,'La fuente de la tasa de cambio no está activa.',1;
            IF EXISTS (
              SELECT 1 FROM OPENJSON(@CostDocumentsJson)
              WITH (CurrencyCode nvarchar(3) '$.CurrencyCode',ExchangeRateSource nvarchar(64) '$.ExchangeRateSource') x
              LEFT JOIN reference.Options optionValue
                ON optionValue.CatalogCode=N'exchange-rate-source'
               AND optionValue.Code=x.ExchangeRateSource AND optionValue.IsActive=1
              WHERE UPPER(x.CurrencyCode)<>N'COP' AND optionValue.OptionId IS NULL)
              THROW 51129,'Un documento adicional usa una fuente de tasa de cambio que no está activa.',1;
            IF EXISTS (
              SELECT 1 FROM OPENJSON(@CostDocumentsJson)
              WITH (SupplierId uniqueidentifier '$.SupplierId',PurchaseEvidenceType nvarchar(64) '$.PurchaseEvidenceType') x
              INNER JOIN dbo.Suppliers supplier
                ON supplier.SupplierId=x.SupplierId
               AND supplier.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND supplier.IsActive=1
              WHERE NOT (
                x.PurchaseEvidenceType IN (N'ForeignCommercialInvoice',N'ImportDeclaration')
                OR supplier.PurchaseEvidencePolicy IS NULL
                OR supplier.PurchaseEvidencePolicy=N'InternalReceiptVoucher' AND x.PurchaseEvidenceType=N'InternalReceiptVoucher'
                OR supplier.PurchaseEvidencePolicy=N'SupplierElectronicInvoice' AND x.PurchaseEvidenceType IN (N'SupplierElectronicInvoice',N'InternalReceiptVoucher')
                OR supplier.PurchaseEvidencePolicy=N'BuyerElectronicSupportDocument' AND x.PurchaseEvidenceType IN (N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher')))
              THROW 51130,'El tipo de soporte de un documento adicional no está permitido para su proveedor.',1;
            IF @SupplierId IS NOT NULL AND @PurchaseEvidenceType IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers
              WHERE SupplierId=@SupplierId AND TenantId=@TenantId AND IsActive=1
                AND (@PurchaseEvidenceType=N'ForeignCommercialInvoice' OR PurchaseEvidencePolicy IS NULL
                  OR PurchaseEvidencePolicy=N'InternalReceiptVoucher' AND @PurchaseEvidenceType=N'InternalReceiptVoucher'
                  OR PurchaseEvidencePolicy=N'SupplierElectronicInvoice' AND @PurchaseEvidenceType IN (N'SupplierElectronicInvoice',N'InternalReceiptVoucher')
                  OR PurchaseEvidencePolicy=N'BuyerElectronicSupportDocument' AND @PurchaseEvidenceType IN (N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher')))
              THROW 51126,'El tipo de soporte seleccionado no está permitido para este proveedor.',1;
            IF @ProductsJson<>N'[]' AND EXISTS (
              SELECT x.ProductId
              FROM OPENJSON(@ProductsJson) WITH (ProductId UNIQUEIDENTIFIER '$') x
              LEFT JOIN dbo.Products p ON p.ProductId=x.ProductId AND p.IsActive=1
                AND p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
              LEFT JOIN dbo.SupplierProducts sp ON sp.ProductId=x.ProductId AND sp.SupplierId=@SupplierId
                    AND sp.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND sp.IsActive=1
              WHERE p.ProductId IS NULL OR sp.SupplierProductId IS NULL)
              THROW 51124,'Cada producto debe estar activo y asociado con el proveedor seleccionado.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", (object?)request.WarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierId", (object?)request.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PurchaseEvidenceType", (object?)request.PurchaseEvidenceType ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatesPayable", request.CreatesPayable);
        command.Parameters.AddWithValue("@IssueDate", (object?)request.SupplierInvoiceDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@DueDate", (object?)request.DueDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@CurrencyCode", request.CurrencyCode);
        command.Parameters.AddWithValue("@ExchangeRateSource", request.ExchangeRateSource);
        command.Parameters.AddWithValue("@ProductsJson", JsonSerializer.Serialize(request.Lines.Select(x => x.ProductId).Distinct()));
        command.Parameters.Add("@ReferencedCostsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(request.ReferencedCostDocuments ?? []);
        command.Parameters.AddWithValue("@CostDocumentsJson",
            JsonSerializer.Serialize(request.AdditionalCostDocuments ?? []));
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number is >= 51121 and <= 51130)
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
              THROW 51125,'Ya existe una recepción confirmada con este identificador.',1;
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
              (GoodsReceiptDraftId,BusinessId,CostEditOfReceiptId,BaseCostRevisionNumber,WarehouseId,SupplierId,PartySiteId,PurchaseOrderId,PurchaseEvidenceType,SupplierInvoiceNumber,
               SupplierInvoiceDate,ReceivedAt,CreatesPayable,DueDate,CurrencyCode,ExchangeRate,
               ExchangeRateDate,ExchangeRateSource,AdditionalCostsJson,ReferencedCostsJson,FinancialDiscountJson,WithholdingConceptCode,WithholdingAdjustmentsJson,Notes,
               NetAmount,TaxAmount,GrandTotal,CreatedByUserId,UpdatedByUserId,CreatedAt,UpdatedAt)
            VALUES(@Id,@BusinessId,@CostEditOfReceiptId,@BaseCostRevisionNumber,@WarehouseId,@SupplierId,@PartySiteId,@PurchaseOrderId,@PurchaseEvidenceType,@InvoiceNumber,@InvoiceDate,@ReceivedAt,
                   @CreatesPayable,@DueDate,@Currency,@ExchangeRate,@ExchangeRateDate,@ExchangeRateSource,
                   @AdditionalCostsJson,@ReferencedCostsJson,@FinancialDiscountJson,@WithholdingConceptCode,@WithholdingAdjustmentsJson,@Notes,@Net,@Tax,@Total,@UserId,@UserId,@Now,@Now);
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
            SET WarehouseId=@WarehouseId,SupplierId=@SupplierId,PartySiteId=@PartySiteId,PurchaseOrderId=@PurchaseOrderId,PurchaseEvidenceType=@PurchaseEvidenceType,SupplierInvoiceNumber=@InvoiceNumber,
                SupplierInvoiceDate=@InvoiceDate,ReceivedAt=@ReceivedAt,CreatesPayable=@CreatesPayable,
                DueDate=@DueDate,CurrencyCode=@Currency,ExchangeRate=@ExchangeRate,
                ExchangeRateDate=@ExchangeRateDate,ExchangeRateSource=@ExchangeRateSource,
                AdditionalCostsJson=@AdditionalCostsJson,ReferencedCostsJson=@ReferencedCostsJson,FinancialDiscountJson=@FinancialDiscountJson,WithholdingConceptCode=@WithholdingConceptCode,
                WithholdingAdjustmentsJson=@WithholdingAdjustmentsJson,Notes=@Notes,NetAmount=@Net,TaxAmount=@Tax,
                GrandTotal=@Total,UpdatedByUserId=@UserId,UpdatedAt=@Now
            WHERE GoodsReceiptDraftId=@Id AND BusinessId=@BusinessId AND RowVersion=@RowVersion
              AND ((CostEditOfReceiptId IS NULL AND @CostEditOfReceiptId IS NULL)
                OR CostEditOfReceiptId=@CostEditOfReceiptId AND BaseCostRevisionNumber=@BaseCostRevisionNumber);
            """;
        await using var command = DraftCommand(sql, connection, transaction, user, request, calculation, now);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = rowVersion;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PurchasingConflictException("El borrador cambió en otra sesión. Recárgalo antes de continuar.");
    }

    private static SqlCommand DraftCommand(
        string sql, SqlConnection connection, SqlTransaction transaction,
        PurchasingUserIdentity user, SaveGoodsReceiptDraftRequest request,
        GoodsReceiptCalculation? calculation, DateTimeOffset now)
    {
        var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.DraftId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@CostEditOfReceiptId", (object?)request.CostEditOfReceiptId ?? DBNull.Value);
        command.Parameters.AddWithValue("@BaseCostRevisionNumber", (object?)request.BaseCostRevisionNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@WarehouseId", (object?)request.WarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierId", (object?)request.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PartySiteId", (object?)request.PartySiteId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PurchaseOrderId", (object?)request.PurchaseOrderId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PurchaseEvidenceType", (object?)request.PurchaseEvidenceType ?? DBNull.Value);
        command.Parameters.AddWithValue("@InvoiceNumber", (object?)request.SupplierInvoiceNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@InvoiceDate", (object?)request.SupplierInvoiceDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReceivedAt", request.ReceivedAt);
        command.Parameters.AddWithValue("@CreatesPayable", request.CreatesPayable);
        command.Parameters.AddWithValue("@DueDate", (object?)request.DueDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@Currency", request.CurrencyCode);
        AddDecimal(command, "@ExchangeRate", request.ExchangeRate, 19, 8);
        command.Parameters.AddWithValue("@ExchangeRateDate",
            request.ExchangeRateDate is DateOnly exchangeDate
                ? exchangeDate.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        command.Parameters.AddWithValue("@ExchangeRateSource", request.ExchangeRateSource);
        command.Parameters.AddWithValue("@AdditionalCostsJson",
            request.AdditionalCostDocuments is { Count: > 0 }
                ? JsonSerializer.Serialize(request.AdditionalCostDocuments,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) : DBNull.Value);
        command.Parameters.Add("@ReferencedCostsJson", SqlDbType.NVarChar, -1).Value =
            request.ReferencedCostDocuments is { Count: > 0 }
                ? JsonSerializer.Serialize(request.ReferencedCostDocuments,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) : DBNull.Value;
        command.Parameters.Add("@FinancialDiscountJson", SqlDbType.NVarChar, -1).Value =
            request.FinancialDiscount is { } financialDiscount
                ? JsonSerializer.Serialize(financialDiscount,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) : DBNull.Value;
        command.Parameters.AddWithValue("@WithholdingConceptCode",
            (object?)request.WithholdingConceptCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@WithholdingAdjustmentsJson",
            request.WithholdingAdjustments is { Count: > 0 }
                ? JsonSerializer.Serialize(request.WithholdingAdjustments,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) : DBNull.Value);
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
        var sources = requestLines.ToDictionary(line => line.LineNumber);
        var lines = calculation.Lines.Select(line =>
        {
            var source = sources[line.LineNumber];
            return new {
                line.LineNumber, line.ProductId, line.Description, line.Quantity,
                line.UnitCost, line.DiscountAmount, line.CommercialDiscountPercent,
                line.TaxCode, line.TaxRate, TaxTreatment = line.TaxTreatment.ToString(),
                line.NetAmount, line.TaxAmount, line.LineTotal, source.PresentationName,
                source.PresentationQuantity, source.UnitsPerPresentation,
                source.PurchaseOrderLineId, source.OverReceiptReason,
                source.TotalGrossWeightKg, source.TotalVolumeM3
            };
        }).ToArray();
        await using var command = new SqlCommand("purchasing.GoodsReceiptDraftLinesInsert", connection, transaction)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add("@DocumentId", SqlDbType.UniqueIdentifier).Value = draftId;
        command.Parameters.Add("@LinesJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(lines);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<GoodsReceiptDraft?> LoadDraftAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid businessId,
        Guid draftId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT GoodsReceiptDraftId,BusinessId,WarehouseId,SupplierId,SupplierInvoiceNumber,
                   SupplierInvoiceDate,ReceivedAt,CreatesPayable,DueDate,CurrencyCode,Notes,
                   NetAmount,TaxAmount,GrandTotal,UpdatedAt,RowVersion,PurchaseEvidenceType,PurchaseOrderId,
                   ExchangeRate,ExchangeRateDate,ExchangeRateSource,AdditionalCostsJson,PartySiteId,
                   WithholdingConceptCode,WithholdingAdjustmentsJson,ReferencedCostsJson,FinancialDiscountJson,
                   CostEditOfReceiptId,BaseCostRevisionNumber
            FROM dbo.GoodsReceiptDrafts
            WHERE GoodsReceiptDraftId=@Id AND BusinessId=@BusinessId;
            SELECT receiptLine.LineNumber,receiptLine.ProductId,receiptLine.DescriptionSnapshot,receiptLine.Quantity,receiptLine.UnitCost,receiptLine.DiscountAmount,
                   receiptLine.TaxCode,receiptLine.TaxRate,receiptLine.TaxTreatment,receiptLine.NetAmount,receiptLine.TaxAmount,receiptLine.LineTotal,
                   receiptLine.PresentationNameSnapshot,receiptLine.PresentationQuantity,receiptLine.UnitsPerPresentation,receiptLine.PurchaseOrderLineId,receiptLine.OverReceiptReason,
                   receiptLine.TotalGrossWeightKg,receiptLine.TotalVolumeM3,product.UnitGrossWeightKg,receiptLine.CommercialDiscountPercent
            FROM dbo.GoodsReceiptDraftLines receiptLine
            JOIN dbo.Products product ON product.ProductId=receiptLine.ProductId
            WHERE receiptLine.GoodsReceiptDraftId=@Id ORDER BY receiptLine.LineNumber;
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
            EvidenceType = reader.IsDBNull(16) ? null : reader.GetString(16),
            OrderId = reader.IsDBNull(17) ? (Guid?)null : reader.GetGuid(17),
            ExchangeRate = reader.GetDecimal(18),
            ExchangeDate = reader.IsDBNull(19) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(19)),
            ExchangeSource = reader.GetString(20),
            AdditionalCosts = reader.IsDBNull(21) ? null :
                JsonSerializer.Deserialize<IReadOnlyList<GoodsReceiptCostDocumentRequest>>(
                    reader.GetString(21), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ReferencedCosts = reader.IsDBNull(25) ? null :
                JsonSerializer.Deserialize<IReadOnlyList<GoodsReceiptVoucherCostRequest>>(
                    reader.GetString(25), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            PartySiteId = reader.IsDBNull(22) ? (Guid?)null : reader.GetGuid(22),
            WithholdingConceptCode = reader.IsDBNull(23) ? null : reader.GetString(23),
            FinancialDiscount = reader.IsDBNull(26) ? null :
                JsonSerializer.Deserialize<GoodsReceiptFinancialDiscountRequest>(
                    reader.GetString(26), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CostEditOfReceiptId = reader.IsDBNull(27) ? (Guid?)null : reader.GetGuid(27),
            BaseCostRevisionNumber = reader.IsDBNull(28) ? (int?)null : reader.GetInt32(28),
            WithholdingAdjustments = reader.IsDBNull(24) ? null :
                JsonSerializer.Deserialize<IReadOnlyList<Auraly.Commerce.Taxation.Contracts.WithholdingAdjustmentRequest>>(
                    reader.GetString(24), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        await reader.NextResultAsync(cancellationToken);
        var lines = new List<GoodsReceiptLineSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(
                reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetString(6), reader.GetDecimal(7),
                reader.GetString(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetString(12), reader.GetDecimal(13), reader.GetDecimal(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15),
                reader.IsDBNull(16) ? null : reader.GetString(16), false,
                reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                UnitGrossWeightKg: reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                CommercialDiscountPercent: reader.IsDBNull(20) ? null : reader.GetDecimal(20)));
        return new(header.Id, header.Business, header.Warehouse, header.Supplier, header.Invoice,
            header.InvoiceDate, header.Received, header.Payable, header.Due, header.Currency,
            header.Notes, header.Net, header.Tax, header.Total, lines, header.Updated, header.Token,
            header.EvidenceType, header.OrderId, header.ExchangeRate, header.ExchangeDate,
            header.ExchangeSource, header.AdditionalCosts, header.PartySiteId,
            header.WithholdingConceptCode, header.WithholdingAdjustments, header.ReferencedCosts,
            header.FinancialDiscount, header.CostEditOfReceiptId, header.BaseCostRevisionNumber);
    }

    internal static byte[] ParseToken(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException exception)
        { throw new PurchasingValidationException("La versión del borrador es inválida.", exception); }
    }

    private static void AddDecimal(SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

}
