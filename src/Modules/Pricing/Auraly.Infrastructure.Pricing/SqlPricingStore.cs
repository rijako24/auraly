using System.Data;
using Auraly.Application.Pricing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Pricing;
using Auraly.Domain.Pricing;
using Microsoft.Data.SqlClient;
using System.Text.Json;

namespace Auraly.Infrastructure.Pricing;

public sealed class SqlPricingStore(
    PricingSqlConnectionFactory connections,
    IAuralyIdGenerator ids) : IPricingStore
{
    public async Task<PriceRevisionPage> ListAsync(
        PricingUserIdentity user, PriceRevisionQuery query, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        const string rows = """
            FROM (
              SELECT p.PriceRevisionProposalId AS CandidateId,p.ProductId,
                COALESCE(x.ProductCode,x.Sku,CONVERT(nvarchar(36),x.ProductId)) AS ProductCode,x.Name AS ProductName,
                p.SourceDocumentId,p.SourceLineNumber,s.Name AS SupplierName,s.SupplierId,
                p.PreviousObservedUnitCost,p.ObservedUnitCost,p.CurrentSalePrice,currentPrice.CurrentPricePublishedAt,p.CurrentMarginPercent,
                p.TargetMarginPercent,p.SuggestedSalePrice,COALESCE(tax.Rate,0) AS SalesTaxRate,
                p.EffectiveMarginAfterRounding,p.Status,p.CreatedAt,p.RowVersion,
                CAST(0 AS bit) AS IsManual,N'GoodsReceipt' AS Origin
              FROM dbo.PriceRevisionProposals p
              INNER JOIN dbo.Products x ON x.ProductId=p.ProductId
              LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
              OUTER APPLY (
                SELECT TOP(1)
                  COALESCE(history.PublishedAt,history.ValidFrom,history.CreatedAt) AS CurrentPricePublishedAt
                FROM dbo.ProductPrices history
                WHERE history.BusinessId=p.BusinessId AND history.ProductId=p.ProductId
                  AND history.Amount=p.CurrentSalePrice AND history.IsActive=1
                ORDER BY history.ValidFrom DESC,history.CreatedAt DESC
              ) currentPrice
              INNER JOIN dbo.GoodsReceipts g ON g.GoodsReceiptId=p.SourceDocumentId
              INNER JOIN dbo.Suppliers s ON s.SupplierId=g.SupplierId
              WHERE p.BusinessId=@BusinessId
                AND NOT EXISTS(
                  SELECT 1 FROM dbo.ProductLinks costLink
                  WHERE costLink.BusinessId=p.BusinessId
                    AND costLink.ChildProductId=p.ProductId
                    AND costLink.SharesPrice=1 AND costLink.IsActive=1)

              UNION ALL

              SELECT prepared.ProductPricePreparationId,x.ProductId,
                COALESCE(x.ProductCode,x.Sku,CONVERT(nvarchar(36),x.ProductId)),x.Name,
                CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier),0,
                COALESCE(supplier.Name,N'Ajuste desde producto'),supplier.SupplierId,
                NULL,COALESCE(prepared.CostBasisAmount,0),price.Amount,COALESCE(price.PublishedAt,price.ValidFrom,price.CreatedAt),price.EffectiveMarginPercent,
                prepared.TargetMarginPercent,prepared.PreparedAmount,COALESCE(tax.Rate,0),
                prepared.EffectiveMarginPercent,N'Approved',prepared.PreparedAt,prepared.RowVersion,
                CAST(1 AS bit),N'Product'
              FROM dbo.ProductPricePreparations prepared
              INNER JOIN dbo.ProductPrices price ON price.BusinessId=prepared.BusinessId
                AND price.ProductId=prepared.ProductId AND price.IsActive=1
              INNER JOIN dbo.Products x ON x.ProductId=prepared.ProductId
              LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
              OUTER APPLY (
                SELECT TOP(1) s.SupplierId,s.Name
                FROM dbo.SupplierProducts sp
                INNER JOIN dbo.Suppliers s ON s.SupplierId=sp.SupplierId AND s.BusinessId=sp.BusinessId
                WHERE sp.BusinessId=prepared.BusinessId AND sp.ProductId=prepared.ProductId AND sp.IsActive=1
                ORDER BY sp.IsPrimary DESC,sp.CreatedAt DESC
              ) supplier
              WHERE prepared.BusinessId=@BusinessId AND prepared.Status=N'Pending'
                AND prepared.PreparationOrigin IN(N'Product',N'LinkedProduct',N'Migration')
            ) candidate
            INNER JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
            OUTER APPLY (
              SELECT TOP(1) observation.UnitCost AS LatestUnitCost
              FROM dbo.SupplierCostObservations observation
              INNER JOIN dbo.Businesses sourceBusiness ON sourceBusiness.BusinessId=observation.BusinessId
              WHERE observation.ProductId=candidate.ProductId
                AND sourceBusiness.TenantId=@TenantId
                AND (b.SharesProductPrices=1 AND sourceBusiness.SharesProductPrices=1
                     OR observation.BusinessId=@BusinessId)
              ORDER BY observation.ObservedAt DESC,observation.SupplierCostObservationId DESC
            ) latestCost
            OUTER APPLY (
              SELECT COALESCE(
                SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.InventoryValue ELSE 0 END)
                  / NULLIF(SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.QuantityOnHand ELSE 0 END),0),
                MAX(balance.AverageUnitCost)) AS AverageUnitCost
              FROM dbo.InventoryBalances balance
              INNER JOIN dbo.Businesses balanceBusiness ON balanceBusiness.BusinessId=balance.BusinessId
              WHERE balance.ProductId=candidate.ProductId AND balanceBusiness.TenantId=@TenantId
                AND (b.SharesProductPrices=1 AND balanceBusiness.SharesProductPrices=1
                     OR balance.BusinessId=@BusinessId)
            ) averageCost
            OUTER APPLY (
              SELECT TOP(1)
                receiptLine.RecognizedInventoryCostAmount / NULLIF(receiptLine.Quantity,0)
                  AS LatestLandedUnitCost
              FROM dbo.GoodsReceiptLines receiptLine
              INNER JOIN dbo.GoodsReceipts receipt
                ON receipt.GoodsReceiptId=receiptLine.GoodsReceiptId
              INNER JOIN dbo.Businesses receiptBusiness
                ON receiptBusiness.BusinessId=receipt.BusinessId
              WHERE receiptLine.ProductId=candidate.ProductId
                AND receipt.Status=N'Processed'
                AND receiptBusiness.TenantId=@TenantId
                AND (b.SharesProductPrices=1 AND receiptBusiness.SharesProductPrices=1
                     OR receipt.BusinessId=@BusinessId)
                AND receiptLine.Quantity>0
              ORDER BY receipt.ReceivedAt DESC,receipt.GoodsReceiptId DESC,receiptLine.LineNumber DESC
            ) latestLandedCost
            WHERE b.TenantId=@TenantId
              AND (@Status IS NULL OR candidate.Status=@Status
                   OR (@Status=N'Pending' AND candidate.Status IN(N'PendingReview',N'Approved')))
              AND (@SupplierId IS NULL OR candidate.SupplierId=@SupplierId)
              AND (@SourceDocumentId IS NULL OR candidate.SourceDocumentId=@SourceDocumentId)
              AND (@Search IS NULL OR candidate.ProductCode LIKE '%'+@Search+'%'
                   OR candidate.ProductName LIKE '%'+@Search+'%'
                   OR candidate.SupplierName LIKE '%'+@Search+'%')
            """;
        int total;
        await using (var count = new SqlCommand("SELECT COUNT(*) "+rows, connection))
        {
            AddQuery(count, user, query);
            total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        }
        await using var command = new SqlCommand("""
            SELECT CandidateId,ProductId,ProductCode,ProductName,SourceDocumentId,SourceLineNumber,
              SupplierName,PreviousObservedUnitCost,ObservedUnitCost,CurrentSalePrice,CurrentPricePublishedAt,CurrentMarginPercent,
              TargetMarginPercent,SuggestedSalePrice,SalesTaxRate,EffectiveMarginAfterRounding,
              candidate.Status,candidate.CreatedAt,candidate.RowVersion,candidate.Origin,
              averageCost.AverageUnitCost,latestCost.LatestUnitCost,
              latestLandedCost.LatestLandedUnitCost
            """ + Environment.NewLine + rows + Environment.NewLine + """
            ORDER BY candidate.CreatedAt DESC,candidate.CandidateId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """, connection);
        AddQuery(command, user, query);
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", query.PageSize);
        var items = new List<PriceRevisionListItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new(
                reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),
                reader.GetGuid(4),reader.GetInt32(5),reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),reader.GetDecimal(8),reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),reader.GetDecimal(13),reader.GetDecimal(14),
                reader.IsDBNull(15) ? null : reader.GetDecimal(15),reader.GetString(16),
                reader.GetDateTimeOffset(17),Convert.ToBase64String(reader.GetFieldValue<byte[]>(18)),reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetDecimal(20),reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                reader.IsDBNull(22) ? null : reader.GetDecimal(22)));
        return new(items, query.Page, query.PageSize, total);
    }
    public async Task<PriceProposalSource?> GetProposalAsync(
        PricingUserIdentity user, Guid proposalId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT p.PriceRevisionProposalId,p.ProductId,p.ObservedUnitCost,
              N'ObservedSupplierCost',COALESCE(tax.Rate,0),p.Status,p.RowVersion,CAST(0 AS bit)
            FROM dbo.PriceRevisionProposals p
            INNER JOIN dbo.Products x ON x.ProductId=p.ProductId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId
            UNION ALL
            SELECT prepared.ProductPricePreparationId,prepared.ProductId,prepared.CostBasisAmount,
              prepared.CostBasisType,COALESCE(tax.Rate,0),N'Approved',prepared.RowVersion,CAST(1 AS bit)
            FROM dbo.ProductPricePreparations prepared
            INNER JOIN dbo.Products x ON x.ProductId=prepared.ProductId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
            INNER JOIN dbo.Businesses b ON b.BusinessId=prepared.BusinessId
            WHERE prepared.ProductPricePreparationId=@ProposalId
              AND prepared.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND prepared.Status=N'Pending';
            """, connection);
        command.Parameters.AddWithValue("@ProposalId", proposalId);
        AddScope(command, user);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),reader.GetDecimal(4),
                reader.GetString(5), reader.GetFieldValue<byte[]>(6),reader.GetBoolean(7))
            : null;
    }

    public async Task<IReadOnlyList<PriceProposalSource>> GetProposalsAsync(
        PricingUserIdentity user,
        IReadOnlyCollection<Guid> proposalIds,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            DECLARE @Ids TABLE(ProposalId UNIQUEIDENTIFIER PRIMARY KEY);
            INSERT @Ids(ProposalId)
            SELECT ProposalId
            FROM OPENJSON(@ProposalIds) WITH(ProposalId UNIQUEIDENTIFIER '$');

            SELECT proposal.PriceRevisionProposalId,proposal.ProductId,
                   proposal.ObservedUnitCost,N'ObservedSupplierCost',COALESCE(tax.Rate,0),
                   proposal.Status,proposal.RowVersion,CAST(0 AS bit)
            FROM @Ids selected
            INNER JOIN dbo.PriceRevisionProposals proposal
              ON proposal.PriceRevisionProposalId=selected.ProposalId
             AND proposal.BusinessId=@BusinessId
            INNER JOIN dbo.Products product ON product.ProductId=proposal.ProductId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId
            INNER JOIN dbo.Businesses business ON business.BusinessId=proposal.BusinessId
                                             AND business.TenantId=@TenantId
            WHERE NOT EXISTS(
              SELECT 1 FROM dbo.ProductLinks costLink
              WHERE costLink.BusinessId=proposal.BusinessId
                AND costLink.ChildProductId=proposal.ProductId
                AND costLink.SharesPrice=1 AND costLink.IsActive=1)

            UNION ALL

            SELECT preparation.ProductPricePreparationId,preparation.ProductId,
                   preparation.CostBasisAmount,preparation.CostBasisType,COALESCE(tax.Rate,0),
                   N'Approved',preparation.RowVersion,CAST(1 AS bit)
            FROM @Ids selected
            INNER JOIN dbo.ProductPricePreparations preparation
              ON preparation.ProductPricePreparationId=selected.ProposalId
             AND preparation.BusinessId=@BusinessId AND preparation.Status=N'Pending'
            INNER JOIN dbo.Products product ON product.ProductId=preparation.ProductId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId
            INNER JOIN dbo.Businesses business ON business.BusinessId=preparation.BusinessId
                                             AND business.TenantId=@TenantId;
            """, connection);
        AddScope(command, user);
        command.Parameters.Add("@ProposalIds", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(proposalIds);
        var result = new List<PriceProposalSource>(proposalIds.Count);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(
                reader.GetGuid(0),reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),reader.GetDecimal(4),
                reader.GetString(5),reader.GetFieldValue<byte[]>(6),reader.GetBoolean(7)));
        return result;
    }

    public async Task<IReadOnlyList<PreparedPricePublication>> GetPendingPublicationsAsync(
        PricingUserIdentity user,
        PublishPendingPricesRequest request,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT
              CASE WHEN proposal.PriceRevisionProposalId IS NULL
                   THEN preparation.ProductPricePreparationId
                   ELSE proposal.PriceRevisionProposalId END ProposalId,
              preparation.ProductId,preparation.CostBasisAmount,
              preparation.CostBasisType,preparation.InputMode,
              preparation.TargetMarginPercent,preparation.PreparedAmount,
              preparation.EffectiveMarginPercent,preparation.RoundingIncrement,
              preparation.RoundingMode,
              CASE WHEN proposal.PriceRevisionProposalId IS NULL
                   THEN preparation.RowVersion ELSE proposal.RowVersion END ExpectedRowVersion,
              CAST(CASE WHEN proposal.PriceRevisionProposalId IS NULL THEN 1 ELSE 0 END AS bit) IsManual
            FROM dbo.ProductPricePreparations preparation
            INNER JOIN dbo.Products product ON product.ProductId=preparation.ProductId
            INNER JOIN dbo.Businesses business
              ON business.BusinessId=preparation.BusinessId AND business.TenantId=@TenantId
            LEFT JOIN dbo.PriceRevisionProposals proposal
              ON proposal.PriceRevisionProposalId=preparation.SourceProposalId
             AND proposal.BusinessId=preparation.BusinessId
             AND proposal.Status IN(N'PendingReview',N'Approved')
            OUTER APPLY(
              SELECT TOP(1) supplier.SupplierId,supplier.Name
              FROM dbo.SupplierProducts supplierProduct
              INNER JOIN dbo.Suppliers supplier
                ON supplier.SupplierId=supplierProduct.SupplierId
               AND supplier.BusinessId=supplierProduct.BusinessId
              WHERE supplierProduct.BusinessId=preparation.BusinessId
                AND supplierProduct.ProductId=preparation.ProductId
                AND supplierProduct.IsActive=1
              ORDER BY supplierProduct.IsPrimary DESC,supplierProduct.CreatedAt DESC
            ) productSupplier
            LEFT JOIN dbo.GoodsReceipts receipt
              ON receipt.GoodsReceiptId=proposal.SourceDocumentId
             AND receipt.BusinessId=proposal.BusinessId
            LEFT JOIN dbo.Suppliers receiptSupplier
              ON receiptSupplier.SupplierId=receipt.SupplierId
             AND receiptSupplier.BusinessId=receipt.BusinessId
            WHERE preparation.BusinessId=@BusinessId
              AND preparation.Status=N'Pending'
              AND (proposal.PriceRevisionProposalId IS NOT NULL
                   OR preparation.SourceProposalId IS NULL
                      AND preparation.PreparationOrigin IN(N'Product',N'LinkedProduct',N'Migration'))
              AND (proposal.PriceRevisionProposalId IS NULL OR NOT EXISTS(
                SELECT 1 FROM dbo.ProductLinks costLink
                WHERE costLink.BusinessId=preparation.BusinessId
                  AND costLink.ChildProductId=preparation.ProductId
                  AND costLink.SharesPrice=1 AND costLink.IsActive=1))
              AND (@SupplierId IS NULL OR COALESCE(receiptSupplier.SupplierId,productSupplier.SupplierId)=@SupplierId)
              AND (@SourceDocumentId IS NULL OR proposal.SourceDocumentId=@SourceDocumentId)
              AND (@Search IS NULL
                   OR COALESCE(product.ProductCode,product.Sku,CONVERT(nvarchar(36),product.ProductId)) LIKE N'%'+@Search+N'%'
                   OR product.Name LIKE N'%'+@Search+N'%'
                   OR COALESCE(receiptSupplier.Name,productSupplier.Name,N'Ajuste desde producto') LIKE N'%'+@Search+N'%')
            ORDER BY preparation.PreparedAt DESC,preparation.ProductPricePreparationId;
            """, connection);
        AddScope(command, user);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 120).Value =
            (object?)request.Search ?? DBNull.Value;
        command.Parameters.AddWithValue("@SupplierId", (object?)request.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SourceDocumentId", (object?)request.SourceDocumentId ?? DBNull.Value);
        var result = new List<PreparedPricePublication>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(
                reader.GetGuid(0),reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),reader.GetDecimal(8),
                reader.GetString(9),reader.GetFieldValue<byte[]>(10),reader.GetBoolean(11)));
        return result;
    }

    public async Task ReviewAsync(
        PricingUserIdentity user, Guid proposalId, PriceCalculationResult calculation,
        byte[] expectedRowVersion, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand("""
            DECLARE @Now DATETIMEOFFSET(7)=SYSDATETIMEOFFSET();
            IF EXISTS(SELECT 1 FROM dbo.PriceRevisionProposals WHERE PriceRevisionProposalId=@ProposalId)
            BEGIN
              DECLARE @ProductId UNIQUEIDENTIFIER,@SharesPrices BIT,@ScopeTenantId UNIQUEIDENTIFIER;
              SELECT @ProductId=p.ProductId,@SharesPrices=b.SharesProductPrices,@ScopeTenantId=b.TenantId
              FROM dbo.PriceRevisionProposals p INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId;
              UPDATE p SET TargetMarginPercent=@TargetMargin,SuggestedSalePrice=@SalePrice,
                RoundedSuggestedSalePrice=@SalePrice,EffectiveMarginAfterRounding=@EffectiveMargin,
                LastInputMode=@InputMode,Status=N'Approved',ReviewedByUserId=@UserId,ReviewedAt=@Now
              FROM dbo.PriceRevisionProposals p
              INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId
                AND b.TenantId=@TenantId AND p.RowVersion=@RowVersion
                AND p.Status IN (N'PendingReview',N'Approved');
              IF @@ROWCOUNT=0 THROW 51601,'The price proposal changed or is not reviewable.',1;
              UPDATE proposal SET TargetMarginPercent=@TargetMargin,SuggestedSalePrice=@SalePrice,
                RoundedSuggestedSalePrice=@SalePrice,EffectiveMarginAfterRounding=@EffectiveMargin,
                LastInputMode=@InputMode,Status=N'Approved',ReviewedByUserId=@UserId,ReviewedAt=@Now
              FROM dbo.PriceRevisionProposals proposal
              INNER JOIN dbo.Businesses target ON target.BusinessId=proposal.BusinessId
              WHERE proposal.ProductId=@ProductId AND proposal.Status=N'PendingReview'
                AND ((@SharesPrices=1 AND target.TenantId=@ScopeTenantId
                      AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@SharesPrices=0 AND proposal.BusinessId=@BusinessId));

              DECLARE @ReceiptTargets TABLE(
                BusinessId UNIQUEIDENTIFIER PRIMARY KEY,
                ProductPriceId UNIQUEIDENTIFIER NOT NULL,
                PublicAmount DECIMAL(19,4) NOT NULL,
                SourceProposalId UNIQUEIDENTIFIER NOT NULL,
                SourceDocumentId UNIQUEIDENTIFIER NOT NULL,
                SourceLineNumber INT NOT NULL,
                CostBasisType NVARCHAR(32) NOT NULL,
                CostBasisAmount DECIMAL(19,6) NOT NULL,
                RoundingIncrement DECIMAL(19,4) NOT NULL,
                RoundingMode NVARCHAR(16) NOT NULL);
              INSERT @ReceiptTargets
              SELECT price.BusinessId,price.ProductPriceId,price.Amount,source.PriceRevisionProposalId,
                     source.SourceDocumentId,source.SourceLineNumber,
                     COALESCE(currentPreparation.CostBasisType,N'ObservedSupplierCost'),
                     source.ObservedUnitCost,
                     COALESCE(currentPreparation.RoundingIncrement,price.RoundingIncrement,1),
                     COALESCE(currentPreparation.RoundingMode,price.RoundingMode,N'Nearest')
              FROM dbo.ProductPrices price
              INNER JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
              CROSS APPLY (
                SELECT TOP(1) proposal.PriceRevisionProposalId,proposal.SourceDocumentId,
                       proposal.SourceLineNumber,proposal.ObservedUnitCost
                FROM dbo.PriceRevisionProposals proposal
                WHERE proposal.BusinessId=price.BusinessId AND proposal.ProductId=@ProductId
                  AND proposal.Status=N'Approved'
                ORDER BY proposal.ReviewedAt DESC,proposal.CreatedAt DESC
              ) source
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisType,value.RoundingIncrement,value.RoundingMode
                FROM dbo.ProductPricePreparations value
                WHERE value.BusinessId=price.BusinessId AND value.ProductId=@ProductId
                  AND value.Status=N'Pending' AND value.SourceProposalId=source.PriceRevisionProposalId
                ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC
              ) currentPreparation
              WHERE price.ProductId=@ProductId AND price.IsActive=1
                AND ((@SharesPrices=1 AND target.TenantId=@ScopeTenantId
                      AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@SharesPrices=0 AND price.BusinessId=@BusinessId));

              UPDATE preparation SET Status=N'Superseded',SupersededAt=@Now
              FROM dbo.ProductPricePreparations preparation
              INNER JOIN @ReceiptTargets target ON target.BusinessId=preparation.BusinessId
              WHERE preparation.ProductId=@ProductId AND preparation.Status=N'Pending';

              INSERT dbo.ProductPricePreparations
                (ProductPricePreparationId,BusinessId,ProductId,SourceProposalId,
                 SourceDocumentId,SourceLineNumber,PreparationOrigin,PublicAmountSnapshot,
                 PreparedAmount,CostBasisType,CostBasisAmount,TargetMarginPercent,
                 EffectiveMarginPercent,InputMode,RoundingIncrement,RoundingMode,
                 Status,PreparedByUserId,PreparedAt)
              SELECT NEWID(),target.BusinessId,@ProductId,target.SourceProposalId,
                     target.SourceDocumentId,target.SourceLineNumber,N'ProposalReview',
                     target.PublicAmount,@SalePrice,target.CostBasisType,target.CostBasisAmount,
                     @TargetMargin,@EffectiveMargin,@InputMode,target.RoundingIncrement,
                     target.RoundingMode,N'Pending',@UserId,@Now
              FROM @ReceiptTargets target;
            END
            ELSE
            BEGIN
              DECLARE @ManualProductId UNIQUEIDENTIFIER;
              SELECT @ManualProductId=preparation.ProductId
              FROM dbo.ProductPricePreparations preparation
              INNER JOIN dbo.Businesses b ON b.BusinessId=preparation.BusinessId
              WHERE preparation.ProductPricePreparationId=@ProposalId
                AND preparation.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND preparation.Status=N'Pending' AND preparation.RowVersion=@RowVersion;
              IF @@ROWCOUNT=0 THROW 51601,'The prepared product price changed or is no longer available.',1;

              DECLARE @ManualSharesPrices BIT;
              SELECT @ManualSharesPrices=SharesProductPrices FROM dbo.Businesses WHERE BusinessId=@BusinessId;
              DECLARE @ManualTargets TABLE(
                BusinessId UNIQUEIDENTIFIER PRIMARY KEY,
                PublicAmount DECIMAL(19,4) NOT NULL,
                CostBasisType NVARCHAR(32) NULL,
                CostBasisAmount DECIMAL(19,6) NULL,
                RoundingIncrement DECIMAL(19,4) NOT NULL,
                RoundingMode NVARCHAR(16) NOT NULL);
              INSERT @ManualTargets
              SELECT price.BusinessId,price.Amount,preparation.CostBasisType,
                     preparation.CostBasisAmount,preparation.RoundingIncrement,preparation.RoundingMode
              FROM dbo.ProductPrices price
              INNER JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
              INNER JOIN dbo.ProductPricePreparations preparation
                ON preparation.BusinessId=price.BusinessId AND preparation.ProductId=price.ProductId
               AND preparation.Status=N'Pending'
              WHERE price.ProductId=@ManualProductId AND price.IsActive=1
                AND ((@ManualSharesPrices=1 AND target.TenantId=@TenantId
                      AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@ManualSharesPrices=0 AND price.BusinessId=@BusinessId));
              UPDATE preparation SET Status=N'Superseded',SupersededAt=@Now
              FROM dbo.ProductPricePreparations preparation
              INNER JOIN @ManualTargets target ON target.BusinessId=preparation.BusinessId
              WHERE preparation.ProductId=@ManualProductId AND preparation.Status=N'Pending';
              INSERT dbo.ProductPricePreparations
                (ProductPricePreparationId,BusinessId,ProductId,PreparationOrigin,
                 PublicAmountSnapshot,PreparedAmount,CostBasisType,CostBasisAmount,
                 TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
                 RoundingMode,Status,PreparedByUserId,PreparedAt)
              SELECT NEWID(),target.BusinessId,@ManualProductId,N'Product',target.PublicAmount,
                     @SalePrice,target.CostBasisType,target.CostBasisAmount,@TargetMargin,
                     @EffectiveMargin,@InputMode,target.RoundingIncrement,target.RoundingMode,
                     N'Pending',@UserId,@Now
              FROM @ManualTargets target;
            END
            """, connection, transaction);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProposalId", proposalId);
        command.Parameters.AddWithValue("@TargetMargin", (object?)calculation.TargetMarginPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("@SalePrice", calculation.RoundedSalePrice);
        command.Parameters.AddWithValue("@EffectiveMargin", (object?)calculation.EffectiveMarginPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("@InputMode", calculation.InputMode);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = expectedRowVersion;
        try
        {
            await ExecuteAsync(command, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RejectAsync(
        PricingUserIdentity user, Guid proposalId, byte[] expectedRowVersion,
        string? reason, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.PriceRevisionProposals WHERE PriceRevisionProposalId=@ProposalId)
            BEGIN
              UPDATE p SET Status=N'Rejected',ReviewedByUserId=@UserId,
                ReviewedAt=SYSDATETIMEOFFSET(),RejectReason=@Reason
              FROM dbo.PriceRevisionProposals p
              INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId
                AND b.TenantId=@TenantId AND p.RowVersion=@RowVersion
                AND p.Status IN (N'PendingReview',N'Approved');
              IF @@ROWCOUNT=0 THROW 51601,'The price proposal changed or is not reviewable.',1;
              UPDATE dbo.ProductPricePreparations
              SET Status=N'Discarded',SupersededAt=SYSDATETIMEOFFSET()
              WHERE BusinessId=@BusinessId AND SourceProposalId=@ProposalId AND Status=N'Pending';
            END
            ELSE
            BEGIN
              UPDATE preparation
              SET Status=N'Discarded',SupersededAt=SYSDATETIMEOFFSET()
              FROM dbo.ProductPricePreparations preparation
              INNER JOIN dbo.Businesses b ON b.BusinessId=preparation.BusinessId
              WHERE preparation.ProductPricePreparationId=@ProposalId
                AND preparation.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND preparation.RowVersion=@RowVersion AND preparation.Status=N'Pending';
              IF @@ROWCOUNT=0 THROW 51601,'The prepared product price changed or is no longer available.',1;
            END
            """, connection, transaction);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProposalId", proposalId);
        command.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = expectedRowVersion;
        try
        {
            await ExecuteAsync(command, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<PricePublicationStoreResult> PublishAsync(
        PricingUserIdentity user, IReadOnlyList<PreparedPricePublication> values,
        DateTimeOffset now, CancellationToken ct)
    {
        if (values.Select(x => x.ProductId).Distinct().Count() != values.Count)
            throw new PricingConflictException("A product can only be published once per batch.");
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await PublishBatchAsync(
                connection, transaction, user, values, now, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException exception) when (exception.Number is 51600 or 51601 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new PricingConflictException(exception.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<PricePublicationStoreResult> PublishBatchAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PricingUserIdentity user,
        IReadOnlyList<PreparedPricePublication> values,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            CREATE TABLE #Batch(
              Ordinal INT NOT NULL PRIMARY KEY,
              ProposalId UNIQUEIDENTIFIER NOT NULL UNIQUE,
              ProductId UNIQUEIDENTIFIER NOT NULL UNIQUE,
              CostBasisAmount DECIMAL(19,6) NULL,
              CostBasisType NVARCHAR(32) NULL,
              InputMode NVARCHAR(16) NOT NULL,
              TargetMarginPercent DECIMAL(9,6) NULL,
              SalePrice DECIMAL(19,4) NOT NULL,
              EffectiveMarginPercent DECIMAL(9,6) NULL,
              RoundingIncrement DECIMAL(19,4) NOT NULL,
              RoundingMode NVARCHAR(16) NOT NULL,
              ExpectedRowVersion BINARY(8) NOT NULL,
              IsManual BIT NOT NULL);

            INSERT #Batch
            SELECT CONVERT(INT,source.[key]),value.ProposalId,value.ProductId,
                   value.CostBasisAmount,value.CostBasisType,value.InputMode,
                   value.TargetMarginPercent,value.SalePrice,
                   value.EffectiveMarginPercent,value.RoundingIncrement,
                   value.RoundingMode,CONVERT(BINARY(8),value.ExpectedRowVersion,2),
                   value.IsManual
            FROM OPENJSON(@BatchJson) source
            CROSS APPLY OPENJSON(source.value) WITH(
              ProposalId UNIQUEIDENTIFIER '$.ProposalId',
              ProductId UNIQUEIDENTIFIER '$.ProductId',
              CostBasisAmount DECIMAL(19,6) '$.CostBasisAmount',
              CostBasisType NVARCHAR(32) '$.CostBasisType',
              InputMode NVARCHAR(16) '$.InputMode',
              TargetMarginPercent DECIMAL(9,6) '$.TargetMarginPercent',
              SalePrice DECIMAL(19,4) '$.SalePrice',
              EffectiveMarginPercent DECIMAL(9,6) '$.EffectiveMarginPercent',
              RoundingIncrement DECIMAL(19,4) '$.RoundingIncrement',
              RoundingMode NVARCHAR(16) '$.RoundingMode',
              ExpectedRowVersion VARCHAR(16) '$.ExpectedRowVersion',
              IsManual BIT '$.IsManual') value;

            IF EXISTS(
              SELECT 1 FROM #Batch batch
              LEFT JOIN dbo.ProductPricePreparations preparation WITH(UPDLOCK,HOLDLOCK)
                ON batch.IsManual=1
               AND preparation.ProductPricePreparationId=batch.ProposalId
               AND preparation.ProductId=batch.ProductId
               AND preparation.BusinessId=@BusinessId
               AND preparation.RowVersion=batch.ExpectedRowVersion
               AND preparation.Status=N'Pending'
              LEFT JOIN dbo.PriceRevisionProposals proposal WITH(UPDLOCK,HOLDLOCK)
                ON batch.IsManual=0
               AND proposal.PriceRevisionProposalId=batch.ProposalId
               AND proposal.ProductId=batch.ProductId
               AND proposal.BusinessId=@BusinessId
               AND proposal.RowVersion=batch.ExpectedRowVersion
               AND proposal.Status IN(N'PendingReview',N'Approved')
              WHERE (batch.IsManual=1 AND preparation.ProductPricePreparationId IS NULL)
                 OR (batch.IsManual=0 AND proposal.PriceRevisionProposalId IS NULL))
              THROW 51600,'Uno o más precios preparados cambiaron o ya fueron publicados.',1;

            DECLARE @SharesPrices BIT,@ScopeTenantId UNIQUEIDENTIFIER;
            SELECT @SharesPrices=SharesProductPrices,@ScopeTenantId=TenantId
            FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;

            CREATE TABLE #Targets(
              Ordinal INT NOT NULL,
              BusinessId UNIQUEIDENTIFIER NOT NULL,
              ProposalId UNIQUEIDENTIFIER NOT NULL,
              ProductId UNIQUEIDENTIFIER NOT NULL,
              ProductPriceId UNIQUEIDENTIFIER NOT NULL,
              PreviousSalePrice DECIMAL(19,4) NOT NULL,
              CostBasisAmount DECIMAL(19,6) NULL,
              CostBasisType NVARCHAR(32) NULL,
              InputMode NVARCHAR(16) NOT NULL,
              TargetMarginPercent DECIMAL(9,6) NULL,
              SalePrice DECIMAL(19,4) NOT NULL,
              EffectiveMarginPercent DECIMAL(9,6) NULL,
              RoundingIncrement DECIMAL(19,4) NOT NULL,
              RoundingMode NVARCHAR(16) NOT NULL,
              IsManual BIT NOT NULL,
              PRIMARY KEY(BusinessId,ProductId));

            INSERT #Targets
            SELECT batch.Ordinal,business.BusinessId,batch.ProposalId,batch.ProductId,
                   NEWID(),price.Amount,batch.CostBasisAmount,batch.CostBasisType,
                   batch.InputMode,batch.TargetMarginPercent,batch.SalePrice,
                   batch.EffectiveMarginPercent,batch.RoundingIncrement,
                   batch.RoundingMode,batch.IsManual
            FROM #Batch batch
            INNER JOIN dbo.Businesses business
              ON (@SharesPrices=1 AND business.TenantId=@ScopeTenantId
                  AND business.SharesProductPrices=1 AND business.IsActive=1)
               OR (@SharesPrices=0 AND business.BusinessId=@BusinessId)
            INNER JOIN dbo.ProductPrices price WITH(UPDLOCK,HOLDLOCK)
              ON price.BusinessId=business.BusinessId
             AND price.ProductId=batch.ProductId AND price.IsActive=1;

            IF EXISTS(
              SELECT 1 FROM #Batch batch
              WHERE NOT EXISTS(
                SELECT 1 FROM #Targets target
                WHERE target.BusinessId=@BusinessId
                  AND target.ProductId=batch.ProductId))
              THROW 51601,'Uno o más productos ya no tienen un precio base activo.',1;

            UPDATE price
            SET IsActive=0,ValidUntil=@Now
            FROM dbo.ProductPrices price
            INNER JOIN #Targets target ON target.BusinessId=price.BusinessId
                                      AND target.ProductId=price.ProductId
            WHERE price.IsActive=1;

            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
               CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
               InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt,
               PublishedByUserId,PublishedAt)
            SELECT ProductPriceId,BusinessId,ProductId,SalePrice,SalePrice,N'COP',
                   CostBasisType,CostBasisAmount,TargetMarginPercent,
                   EffectiveMarginPercent,InputMode,RoundingIncrement,RoundingMode,
                   @Now,1,@Now,@UserId,@Now
            FROM #Targets;

            UPDATE proposal
            SET Status=N'Published',ReviewedByUserId=@UserId,ReviewedAt=@Now,
                TargetMarginPercent=target.TargetMarginPercent,
                SuggestedSalePrice=target.SalePrice,
                RoundedSuggestedSalePrice=target.SalePrice,
                EffectiveMarginAfterRounding=target.EffectiveMarginPercent,
                LastInputMode=target.InputMode
            FROM dbo.PriceRevisionProposals proposal
            INNER JOIN #Targets target ON target.BusinessId=proposal.BusinessId
                                      AND target.ProductId=proposal.ProductId
                                      AND target.IsManual=0
            WHERE proposal.Status IN(N'PendingReview',N'Approved');

            UPDATE preparation
            SET Status=N'Published',PublishedAt=@Now
            FROM dbo.ProductPricePreparations preparation
            INNER JOIN #Targets target ON target.BusinessId=preparation.BusinessId
                                      AND target.ProductId=preparation.ProductId
            WHERE preparation.Status=N'Pending';

            CREATE TABLE #Changes(
              CatalogChangeId BIGINT NOT NULL,
              BusinessId UNIQUEIDENTIFIER NOT NULL,
              ProductId UNIQUEIDENTIFIER NOT NULL,
              PRIMARY KEY(BusinessId,ProductId));
            INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
              OUTPUT inserted.CatalogChangeId,inserted.BusinessId,inserted.ProductId
                INTO #Changes
              SELECT BusinessId,ProductId,N'Upsert',@Now FROM #Targets;

            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            SELECT NEWID(),BusinessId,N'Catalog',MAX(CatalogChangeId),@Now
            FROM #Changes
            GROUP BY BusinessId;

            INSERT dbo.PricePublicationAudits
              (PricePublicationAuditId,BusinessId,ProductId,ProductPriceId,ProposalId,
               PublicationOrigin,PreviousSalePrice,PublishedSalePrice,CostBasisAmount,
               EffectiveMarginPercent,InputMode,PublishedByUserId,PublishedAt)
            SELECT NEWID(),target.BusinessId,target.ProductId,target.ProductPriceId,
                   CASE WHEN target.BusinessId=@BusinessId AND target.IsManual=0
                        THEN target.ProposalId ELSE NULL END,
                   CASE WHEN target.IsManual=0 THEN N'ReceiptProposal'
                        WHEN target.CostBasisType=N'LinkedProduct' THEN N'LinkedProduct'
                        ELSE N'Manual' END,
                   target.PreviousSalePrice,target.SalePrice,target.CostBasisAmount,
                   target.EffectiveMarginPercent,target.InputMode,@UserId,@Now
            FROM #Targets target;

            SELECT target.ProductPriceId,target.ProposalId,target.ProductId,
                   target.SalePrice,target.EffectiveMarginPercent,
                   change.CatalogChangeId,@Now
            FROM #Targets target
            INNER JOIN #Changes change ON change.BusinessId=target.BusinessId
                                      AND change.ProductId=target.ProductId
            WHERE target.BusinessId=@BusinessId
            ORDER BY target.Ordinal;

            SELECT COALESCE(MAX(CatalogChangeId),0) FROM #Changes;
            SELECT DISTINCT BusinessId FROM #Targets ORDER BY BusinessId;
            """, connection, transaction);
        AddScope(command, user);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.Add("@BatchJson", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(values.Select(value => new
            {
                value.ProposalId,
                value.ProductId,
                value.CostBasisAmount,
                value.CostBasisType,
                value.InputMode,
                value.TargetMarginPercent,
                value.SalePrice,
                value.EffectiveMarginPercent,
                value.RoundingIncrement,
                value.RoundingMode,
                ExpectedRowVersion = Convert.ToHexString(value.ExpectedRowVersion),
                value.IsManual
            }));

        var published = new List<PublishedPrice>(values.Count);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            published.Add(new(
                reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),reader.GetInt64(5),
                reader.GetDateTimeOffset(6)));
        await reader.NextResultAsync(ct);
        var highestCursor = await reader.ReadAsync(ct) ? reader.GetInt64(0) : 0;
        await reader.NextResultAsync(ct);
        var businessIds = new List<Guid>();
        while (await reader.ReadAsync(ct)) businessIds.Add(reader.GetGuid(0));
        return new(new(published, highestCursor), businessIds);
    }

    public async Task<ProductPricingContext?> GetProductContextAsync(
        PricingUserIdentity user, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT x.ProductId,x.Name,COALESCE(preparation.PreparedAmount,price.Amount,0),
              COALESCE(price.Amount,0),
              CASE WHEN costLink.ProductLinkId IS NOT NULL
                     THEN ROUND(parentCost.CostBasisAmount*costLink.PriceFactor,6)
                   ELSE COALESCE(preparation.CostBasisAmount,cost.LatestUnitCost,price.CostBasisAmount) END,
              CASE WHEN costLink.ProductLinkId IS NOT NULL THEN N'LinkedProduct'
                   WHEN preparation.ProductPricePreparationId IS NOT NULL THEN preparation.CostBasisType
                   WHEN cost.LatestUnitCost IS NOT NULL THEN N'ObservedSupplierCost'
                   ELSE price.CostBasisType END,
              COALESCE(preparation.EffectiveMarginPercent,price.EffectiveMarginPercent),COALESCE(tax.Rate,0),
              COALESCE(preparation.RoundingIncrement,price.RoundingIncrement,1),
              COALESCE(preparation.RoundingMode,price.RoundingMode,N'Nearest'),
              CASE WHEN costLink.ProductLinkId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END,
              costLink.ParentProductId,costParent.Name,costLink.PriceFactor
            FROM dbo.Products x
            INNER JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
            LEFT JOIN dbo.ProductLinks costLink
              ON costLink.BusinessId=@BusinessId AND costLink.ChildProductId=x.ProductId
             AND costLink.SharesPrice=1 AND costLink.IsActive=1
            LEFT JOIN dbo.Products costParent ON costParent.ProductId=costLink.ParentProductId
            OUTER APPLY (
              SELECT TOP(1) p.Amount,p.PreparedAmount,p.CostBasisAmount,p.CostBasisType,p.EffectiveMarginPercent,p.RoundingIncrement,p.RoundingMode
              FROM dbo.ProductPrices p
              WHERE p.BusinessId=@BusinessId AND p.ProductId=x.ProductId
                AND p.IsActive=1 AND p.ValidFrom<=SYSDATETIMEOFFSET()
                AND (p.ValidUntil IS NULL OR p.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY p.ValidFrom DESC,p.ProductPriceId
            ) price
            OUTER APPLY (
              SELECT TOP(1) prepared.ProductPricePreparationId,prepared.PreparedAmount,
                prepared.CostBasisAmount,prepared.CostBasisType,prepared.EffectiveMarginPercent,
                prepared.RoundingIncrement,prepared.RoundingMode
              FROM dbo.ProductPricePreparations prepared
              WHERE prepared.BusinessId=@BusinessId AND prepared.ProductId=x.ProductId
                AND prepared.Status=N'Pending'
              ORDER BY prepared.PreparedAt DESC,prepared.ProductPricePreparationId DESC
            ) preparation
            OUTER APPLY (
              SELECT TOP(1) latest.LatestUnitCost
              FROM dbo.SupplierProductLatestCosts latest
              LEFT JOIN dbo.SupplierProducts association ON association.BusinessId=latest.BusinessId
                AND association.SupplierId=latest.SupplierId AND association.ProductId=latest.ProductId
              WHERE latest.ProductId=x.ProductId
                AND (b.SharesProductPrices=1 AND latest.BusinessId IN (
                       SELECT shared.BusinessId FROM dbo.Businesses shared
                       WHERE shared.TenantId=@TenantId AND shared.SharesProductPrices=1 AND shared.IsActive=1)
                     OR b.SharesProductPrices=0 AND latest.BusinessId=@BusinessId)
              ORDER BY CASE WHEN association.IsPrimary=1 AND association.IsActive=1 THEN 0 ELSE 1 END,
                latest.ObservedAt DESC,latest.SupplierId
            ) cost
            OUTER APPLY (
              SELECT COALESCE(parentPreparation.CostBasisAmount,
                              parentLatest.LatestUnitCost,
                              parentPrice.CostBasisAmount) CostBasisAmount
              FROM (SELECT 1 AS Anchor) source
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisAmount
                FROM dbo.ProductPricePreparations value
                WHERE value.BusinessId=@BusinessId
                  AND value.ProductId=costLink.ParentProductId
                  AND value.Status=N'Pending'
                ORDER BY value.PreparedAt DESC,value.ProductPricePreparationId DESC
              ) parentPreparation
              OUTER APPLY (
                SELECT TOP(1) value.LatestUnitCost
                FROM dbo.SupplierProductLatestCosts value
                WHERE value.BusinessId=@BusinessId
                  AND value.ProductId=costLink.ParentProductId
                ORDER BY value.ObservedAt DESC,value.SupplierId
              ) parentLatest
              OUTER APPLY (
                SELECT TOP(1) value.CostBasisAmount
                FROM dbo.ProductPrices value
                WHERE value.BusinessId=@BusinessId
                  AND value.ProductId=costLink.ParentProductId AND value.IsActive=1
                ORDER BY value.ValidFrom DESC,value.ProductPriceId
              ) parentPrice
            ) parentCost
            WHERE x.ProductId=@ProductId AND b.TenantId=@TenantId
              AND (x.TenantId=@TenantId OR (x.TenantId IS NULL AND x.BusinessId=@BusinessId));
            """, connection);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProductId", productId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),reader.GetDecimal(7),
                reader.GetDecimal(8),reader.GetString(9),reader.GetBoolean(10),
                reader.IsDBNull(11) ? null : reader.GetGuid(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13))
            : null;
    }

    public async Task<PreparedProductPrice> SavePreparedProductAsync(
        PricingUserIdentity user, PreparedDirectProductPricePublication value,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var proposedPriceId = ids.NewId();
            await using var command = new SqlCommand("""
                IF NOT EXISTS(
                  SELECT 1 FROM dbo.Products x WITH(UPDLOCK,HOLDLOCK)
                  INNER JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
                  WHERE x.ProductId=@ProductId AND b.TenantId=@TenantId
                    AND (x.TenantId=@TenantId OR (x.TenantId IS NULL AND x.BusinessId=@BusinessId)))
                  THROW 51600,'The product is outside the authenticated business.',1;

                DECLARE @CostParentProductId UNIQUEIDENTIFIER;
                SELECT @CostParentProductId=link.ParentProductId
                FROM dbo.ProductLinks link WITH(UPDLOCK,HOLDLOCK)
                WHERE link.BusinessId=@BusinessId AND link.ChildProductId=@ProductId
                  AND link.SharesPrice=1 AND link.IsActive=1;
                IF @CostParentProductId IS NOT NULL
                  AND ISNULL(@CostBasisType,N'')<>N'LinkedProduct'
                  THROW 51600,'El costo del producto vinculado depende del producto principal y no se puede editar.',1;

                DECLARE @SharesPrices BIT,@ScopeTenantId UNIQUEIDENTIFIER;
                SELECT @SharesPrices=SharesProductPrices,@ScopeTenantId=TenantId
                FROM dbo.Businesses WHERE BusinessId=@BusinessId;

                DECLARE @Targets TABLE(BusinessId UNIQUEIDENTIFIER PRIMARY KEY,PublicAmount DECIMAL(19,4));
                INSERT @Targets
                SELECT price.BusinessId,price.Amount
                FROM dbo.ProductPrices price WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
                WHERE price.ProductId=@ProductId AND price.IsActive=1
                  AND ((@SharesPrices=1 AND target.TenantId=@ScopeTenantId
                        AND target.SharesProductPrices=1 AND target.IsActive=1)
                    OR (@SharesPrices=0 AND price.BusinessId=@BusinessId));
                IF NOT EXISTS(SELECT 1 FROM @Targets WHERE BusinessId=@BusinessId)
                  THROW 51600,'The product has no published base price.',1;

                UPDATE proposal SET Status=N'Superseded'
                FROM dbo.PriceRevisionProposals proposal
                INNER JOIN @Targets target ON target.BusinessId=proposal.BusinessId
                WHERE proposal.ProductId=@ProductId
                  AND proposal.Status IN(N'PendingReview',N'Approved');

                UPDATE preparation
                SET Status=N'Superseded',SupersededAt=@Now
                FROM dbo.ProductPricePreparations preparation
                INNER JOIN @Targets target ON target.BusinessId=preparation.BusinessId
                WHERE preparation.ProductId=@ProductId AND preparation.Status=N'Pending';

                INSERT dbo.ProductPricePreparations
                  (ProductPricePreparationId,BusinessId,ProductId,PreparationOrigin,
                   PublicAmountSnapshot,PreparedAmount,CostBasisType,CostBasisAmount,
                   TargetMarginPercent,EffectiveMarginPercent,InputMode,RoundingIncrement,
                   RoundingMode,Status,PreparedByUserId,PreparedAt)
                SELECT CASE WHEN target.BusinessId=@BusinessId THEN @ProductPriceId ELSE NEWID() END,
                       target.BusinessId,@ProductId,N'Product',target.PublicAmount,@PreparedAmount,
                       @CostBasisType,@CostBasis,@TargetMargin,@EffectiveMargin,@InputMode,
                       @RoundingIncrement,@RoundingMode,N'Pending',@UserId,@Now
                FROM @Targets target;

                IF @CostParentProductId IS NULL
                  EXEC dbo.ProductLinkedCostsPrepare
                    @BusinessId=@BusinessId,@ParentProductId=@ProductId,
                    @ParentCost=@CostBasis,@ChildProductId=NULL,@UserId=@UserId,@Now=@Now;
                ELSE
                  EXEC dbo.ProductLinkedCostsPrepare
                    @BusinessId=@BusinessId,@ParentProductId=@CostParentProductId,
                    @ParentCost=NULL,@ChildProductId=@ProductId,@UserId=@UserId,@Now=@Now;

                SELECT TOP(1) ProductPricePreparationId,PreparedAmount,PublicAmountSnapshot,
                       CostBasisAmount,EffectiveMarginPercent
                FROM dbo.ProductPricePreparations
                WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND Status=N'Pending'
                ORDER BY PreparedAt DESC,ProductPricePreparationId DESC;
                """, connection, transaction);
            AddScope(command, user);
            command.Parameters.AddWithValue("@ProductId", value.ProductId);
            command.Parameters.AddWithValue("@ProductPriceId", proposedPriceId);
            command.Parameters.AddWithValue("@CostBasis", (object?)value.CostBasisAmount ?? DBNull.Value);
            command.Parameters.AddWithValue("@CostBasisType", (object?)value.CostBasisType ?? DBNull.Value);
            command.Parameters.AddWithValue("@TargetMargin", (object?)value.TargetMarginPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("@EffectiveMargin", (object?)value.EffectiveMarginPercent ?? DBNull.Value);
            command.Parameters.AddWithValue("@InputMode", value.InputMode);
            command.Parameters.AddWithValue("@RoundingIncrement", value.RoundingIncrement);
            command.Parameters.AddWithValue("@RoundingMode", value.RoundingMode);
            command.Parameters.AddWithValue("@PreparedAmount", value.SalePrice);
            command.Parameters.AddWithValue("@Now", now);
            Guid priceId;
            decimal preparedAmount;
            decimal publicAmount;
            decimal? costBasisAmount;
            decimal? effectiveMarginPercent;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                await reader.ReadAsync(ct);
                priceId = reader.GetGuid(0);
                preparedAmount = reader.GetDecimal(1);
                publicAmount = reader.GetDecimal(2);
                costBasisAmount = reader.IsDBNull(3) ? null : reader.GetDecimal(3);
                effectiveMarginPercent = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
            }
            await transaction.CommitAsync(ct);
            return new(priceId,value.ProductId,preparedAmount,publicAmount,
                costBasisAmount,effectiveMarginPercent,now);
        }
        catch (SqlException exception) when (exception.Number is 51020 or 51600 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new PricingConflictException(exception.Message);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ProductPriceHistoryPage> HistoryAsync(
        PricingUserIdentity user, Guid productId, int page, int pageSize, string? activityType, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT activity.ActivityId,activity.ProductId,activity.ActivityType,
              activity.Origin,activity.Status,activity.PublicAmount,activity.PreparedAmount,
              activity.CostBasisAmount,activity.CostBasisType,
              activity.TargetMarginPercent,activity.EffectiveMarginPercent,
              activity.InputMode,activity.RoundingIncrement,activity.RoundingMode,
              activity.SourceDocumentId,activity.SourceLineNumber,activity.SourceProductId,
              activity.UserId,activity.UserName,activity.OccurredAt,
              COUNT_BIG(1) OVER() TotalCount
            FROM (
              SELECT preparation.ProductPricePreparationId ActivityId,
                preparation.ProductId,N'Preparation' ActivityType,
                preparation.PreparationOrigin Origin,preparation.Status,
                preparation.PublicAmountSnapshot PublicAmount,
                preparation.PreparedAmount,preparation.CostBasisAmount,
                preparation.CostBasisType,preparation.TargetMarginPercent,
                preparation.EffectiveMarginPercent,preparation.InputMode,
                preparation.RoundingIncrement,preparation.RoundingMode,
                preparation.SourceDocumentId,preparation.SourceLineNumber,
                preparation.SourceProductId,preparation.PreparedByUserId UserId,
                COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(userValue.FirstName,N' ',userValue.LastName))),N''),
                         userValue.Username,N'Sistema') UserName,
                preparation.PreparedAt OccurredAt
              FROM dbo.ProductPricePreparations preparation
              INNER JOIN dbo.Businesses business ON business.BusinessId=preparation.BusinessId
              LEFT JOIN dbo.AppUsers userValue ON userValue.UserId=preparation.PreparedByUserId
              WHERE preparation.ProductId=@ProductId
                AND preparation.BusinessId=@BusinessId AND business.TenantId=@TenantId

              UNION ALL

              SELECT publication.PricePublicationAuditId,publication.ProductId,
                N'Publication',publication.PublicationOrigin,N'Published',
                publication.PreviousSalePrice,publication.PublishedSalePrice,
                publication.CostBasisAmount,price.CostBasisType,
                price.TargetMarginPercent,publication.EffectiveMarginPercent,
                publication.InputMode,price.RoundingIncrement,price.RoundingMode,
                proposal.SourceDocumentId,proposal.SourceLineNumber,
                CAST(NULL AS UNIQUEIDENTIFIER),publication.PublishedByUserId,
                COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(userValue.FirstName,N' ',userValue.LastName))),N''),
                         userValue.Username,N'Sistema'),publication.PublishedAt
              FROM dbo.PricePublicationAudits publication
              INNER JOIN dbo.ProductPrices price ON price.ProductPriceId=publication.ProductPriceId
              INNER JOIN dbo.Businesses business ON business.BusinessId=publication.BusinessId
              LEFT JOIN dbo.PriceRevisionProposals proposal
                ON proposal.PriceRevisionProposalId=publication.ProposalId
              LEFT JOIN dbo.AppUsers userValue ON userValue.UserId=publication.PublishedByUserId
              WHERE publication.ProductId=@ProductId
                AND publication.BusinessId=@BusinessId AND business.TenantId=@TenantId
            ) activity
            WHERE @ActivityType IS NULL OR activity.ActivityType=@ActivityType
            ORDER BY activity.OccurredAt DESC,activity.ActivityId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@ActivityType", (object?)activityType ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        var items = new List<ProductPriceHistoryItem>();
        var totalCount = 0;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            totalCount = checked((int)reader.GetInt64(20));
            items.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetDecimal(5),reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetGuid(14),
                reader.IsDBNull(15) ? null : reader.GetInt32(15),
                reader.IsDBNull(16) ? null : reader.GetGuid(16),
                reader.IsDBNull(17) ? null : reader.GetGuid(17),reader.GetString(18),
                reader.GetDateTimeOffset(19)));
        }
        return new(items, page, pageSize, totalCount);
    }

    public async Task<PriceChannelReportSource?> GetChannelReportSourceAsync(
        PricingUserIdentity user, Guid priceChannelId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT PriceChannelId,Code,Name,Strategy,Value,IsActive
            FROM dbo.PriceChannels
            WHERE PriceChannelId=@PriceChannelId AND BusinessId=@BusinessId;

            ;WITH CategoryAncestors AS
            (
              SELECT category.ProductCategoryId DescendantId,
                     category.ProductCategoryId AncestorId,
                     category.ParentProductCategoryId
              FROM dbo.ProductCategories category
              WHERE category.BusinessId=@BusinessId
              UNION ALL
              SELECT child.DescendantId,parent.ProductCategoryId,
                     parent.ParentProductCategoryId
              FROM CategoryAncestors child
              JOIN dbo.ProductCategories parent
                ON parent.ProductCategoryId=child.ParentProductCategoryId
               AND parent.BusinessId=@BusinessId
            )
            SELECT product.ProductId,
                   COALESCE(NULLIF(product.ProductCode,N''),NULLIF(product.Sku,N''),N''),
                   product.Name,price.Amount,price.CurrencyCode,
                   product.ProductCategoryId,product.ProductBrandId,
                   COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),ancestor.AncestorId),N',')
                             FROM CategoryAncestors ancestor
                             WHERE ancestor.DescendantId=product.ProductCategoryId),N''),
                   COALESCE(averageCost.Amount,price.CostBasisAmount,0),
                   COALESCE(latestCost.LatestUnitCost,price.CostBasisAmount,averageCost.Amount,0),
                   COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)
            FROM dbo.Products product
            CROSS APPLY (
              SELECT TOP(1) value.Amount,value.CurrencyCode,value.CostBasisAmount,
                     value.TargetMarginPercent,value.EffectiveMarginPercent
              FROM dbo.ProductPrices value
              WHERE value.BusinessId=@BusinessId AND value.ProductId=product.ProductId
                AND value.IsActive=1 AND value.ValidFrom<=SYSDATETIMEOFFSET()
                AND (value.ValidUntil IS NULL OR value.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY value.ValidFrom DESC,value.ProductPriceId
            ) price
            OUTER APPLY (
              SELECT COALESCE(
                SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.InventoryValue ELSE 0 END)
                  / NULLIF(SUM(CASE WHEN balance.QuantityOnHand>0 THEN balance.QuantityOnHand ELSE 0 END),0),
                MAX(NULLIF(balance.AverageUnitCost,0))) Amount
              FROM dbo.InventoryBalances balance
              WHERE balance.BusinessId=@BusinessId AND balance.ProductId=product.ProductId
            ) averageCost
            OUTER APPLY (
              SELECT TOP(1) latest.LatestUnitCost
              FROM dbo.SupplierProductLatestCosts latest
              WHERE latest.BusinessId=@BusinessId AND latest.ProductId=product.ProductId
              ORDER BY latest.ObservedAt DESC,latest.SupplierId
            ) latestCost
            WHERE product.TenantId=@TenantId AND product.IsActive=1
            ORDER BY product.Name,product.ProductId;

            SELECT item.PriceChannelId,item.ProductId,item.MinimumQuantity,
                   item.Amount,item.CurrencyCode
            FROM dbo.PriceChannelItems item
            WHERE item.PriceChannelId=@PriceChannelId AND item.IsActive=1
              AND item.ValidFrom<=SYSDATETIMEOFFSET()
              AND (item.ValidUntil IS NULL OR item.ValidUntil>SYSDATETIMEOFFSET())
            ORDER BY item.ProductId,item.MinimumQuantity;

            SELECT exclusion.PriceChannelId,exclusion.ProductId,
                   exclusion.ProductCategoryId,exclusion.ProductBrandId
            FROM dbo.PriceChannelExclusions exclusion
            WHERE exclusion.PriceChannelId=@PriceChannelId;
            """, connection);
        AddScope(command, user);
        command.Parameters.AddWithValue("@PriceChannelId", priceChannelId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var channelId = reader.GetGuid(0);
        var code = reader.GetString(1);
        var name = reader.GetString(2);
        var strategy = reader.GetString(3);
        decimal? value = reader.IsDBNull(4) ? null : reader.GetDecimal(4);
        var isActive = reader.GetBoolean(5);

        var products = new List<PriceChannelReportProductSource>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            products.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetDecimal(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.GetString(7).Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Guid.Parse).ToArray(),
                reader.GetDecimal(8), reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10)));

        var tiers = new List<PriceChannelTierRule>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            tiers.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetString(4)));

        var exclusions = new List<PriceChannelExclusionRule>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            exclusions.Add(new(reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        return new(channelId, code, name, strategy, value, isActive,
            products, tiers, exclusions);
    }

    private static async Task ValidateProposalAsync(
        SqlConnection connection, SqlTransaction transaction, PricingUserIdentity user,
        PreparedPricePublication value, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            IF @IsManual=0 AND NOT EXISTS(
              SELECT 1 FROM dbo.PriceRevisionProposals p WITH(UPDLOCK,HOLDLOCK)
              INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              WHERE p.PriceRevisionProposalId=@ProposalId AND p.ProductId=@ProductId
                AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND p.RowVersion=@RowVersion AND p.Status IN(N'PendingReview',N'Approved'))
              THROW 51600,'The price proposal is outside scope, changed or already completed.',1;
            IF @IsManual=1 AND NOT EXISTS(
              SELECT 1 FROM dbo.ProductPricePreparations preparation WITH(UPDLOCK,HOLDLOCK)
              INNER JOIN dbo.Businesses b ON b.BusinessId=preparation.BusinessId
              WHERE preparation.ProductPricePreparationId=@ProposalId
                AND preparation.ProductId=@ProductId
                AND preparation.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND preparation.RowVersion=@RowVersion AND preparation.Status=N'Pending')
              THROW 51600,'The prepared product price is outside scope, changed or already published.',1;
            """, connection, transaction);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProposalId", value.ProposalId);
        command.Parameters.AddWithValue("@ProductId", value.ProductId);
        command.Parameters.AddWithValue("@IsManual", value.IsManual);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = value.ExpectedRowVersion;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteAsync(SqlCommand command, CancellationToken ct)
    {
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException exception) when (exception.Number == 51601)
        { throw new PricingConflictException(exception.Message); }
    }

    private static void AddScope(SqlCommand command, PricingUserIdentity user)
    {
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
    }

    private static void AddQuery(SqlCommand command, PricingUserIdentity user, PriceRevisionQuery query)
    {
        AddScope(command, user);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierId", (object?)query.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SourceDocumentId", (object?)query.SourceDocumentId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
    }
}
