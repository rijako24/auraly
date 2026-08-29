using System.Data;
using Auraly.Application.Pricing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Pricing;
using Microsoft.Data.SqlClient;

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

              UNION ALL

              SELECT pp.ProductPriceId,x.ProductId,
                COALESCE(x.ProductCode,x.Sku,CONVERT(nvarchar(36),x.ProductId)),x.Name,
                CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier),0,
                COALESCE(supplier.Name,N'Ajuste desde producto'),supplier.SupplierId,
                NULL,COALESCE(pp.CostBasisAmount,0),pp.Amount,COALESCE(pp.PublishedAt,pp.ValidFrom,pp.CreatedAt),pp.EffectiveMarginPercent,
                pp.TargetMarginPercent,pp.PreparedAmount,COALESCE(tax.Rate,0),
                pp.EffectiveMarginPercent,N'Approved',pp.CreatedAt,pp.RowVersion,
                CAST(1 AS bit),N'Product'
              FROM dbo.ProductPrices pp
              INNER JOIN dbo.Products x ON x.ProductId=pp.ProductId
              LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
              OUTER APPLY (
                SELECT TOP(1) s.SupplierId,s.Name
                FROM dbo.SupplierProducts sp
                INNER JOIN dbo.Suppliers s ON s.SupplierId=sp.SupplierId AND s.BusinessId=sp.BusinessId
                WHERE sp.BusinessId=pp.BusinessId AND sp.ProductId=pp.ProductId AND sp.IsActive=1
                ORDER BY sp.IsPrimary DESC,sp.CreatedAt DESC
              ) supplier
              WHERE pp.BusinessId=@BusinessId AND pp.IsActive=1
                AND ABS(pp.PreparedAmount-pp.Amount)>=0.0001
                AND NOT EXISTS(
                  SELECT 1 FROM dbo.PriceRevisionProposals activeProposal
                  WHERE activeProposal.BusinessId=pp.BusinessId AND activeProposal.ProductId=pp.ProductId
                    AND activeProposal.Status IN(N'PendingReview',N'Approved'))
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
              averageCost.AverageUnitCost,latestCost.LatestUnitCost
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
                reader.IsDBNull(20) ? null : reader.GetDecimal(20),reader.IsDBNull(21) ? null : reader.GetDecimal(21)));
        return new(items, query.Page, query.PageSize, total);
    }
    public async Task<PriceProposalSource?> GetProposalAsync(
        PricingUserIdentity user, Guid proposalId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT p.PriceRevisionProposalId,p.ProductId,p.ObservedUnitCost,COALESCE(tax.Rate,0),p.Status,p.RowVersion,CAST(0 AS bit)
            FROM dbo.PriceRevisionProposals p
            INNER JOIN dbo.Products x ON x.ProductId=p.ProductId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId
            UNION ALL
            SELECT pp.ProductPriceId,pp.ProductId,pp.CostBasisAmount,COALESCE(tax.Rate,0),N'Approved',pp.RowVersion,CAST(1 AS bit)
            FROM dbo.ProductPrices pp
            INNER JOIN dbo.Products x ON x.ProductId=pp.ProductId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
            INNER JOIN dbo.Businesses b ON b.BusinessId=pp.BusinessId
            WHERE pp.ProductPriceId=@ProposalId AND pp.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND pp.IsActive=1 AND ABS(pp.PreparedAmount-pp.Amount)>=0.0001;
            """, connection);
        command.Parameters.AddWithValue("@ProposalId", proposalId);
        AddScope(command, user);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2),reader.GetDecimal(3),
                reader.GetString(4), reader.GetFieldValue<byte[]>(5),reader.GetBoolean(6))
            : null;
    }

    public async Task ReviewAsync(
        PricingUserIdentity user, Guid proposalId, PriceCalculationResult calculation,
        byte[] expectedRowVersion, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.PriceRevisionProposals WHERE PriceRevisionProposalId=@ProposalId)
            BEGIN
              DECLARE @ProductId UNIQUEIDENTIFIER,@SharesPrices BIT,@ScopeTenantId UNIQUEIDENTIFIER;
              SELECT @ProductId=p.ProductId,@SharesPrices=b.SharesProductPrices,@ScopeTenantId=b.TenantId
              FROM dbo.PriceRevisionProposals p INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId;
              UPDATE p SET TargetMarginPercent=@TargetMargin,SuggestedSalePrice=@SalePrice,
                RoundedSuggestedSalePrice=@SalePrice,EffectiveMarginAfterRounding=@EffectiveMargin,
                LastInputMode=@InputMode,Status=N'Approved',ReviewedByUserId=@UserId,ReviewedAt=SYSDATETIMEOFFSET()
              FROM dbo.PriceRevisionProposals p
              INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId
                AND b.TenantId=@TenantId AND p.RowVersion=@RowVersion
                AND p.Status IN (N'PendingReview',N'Approved');
              IF @@ROWCOUNT=0 THROW 51601,'The price proposal changed or is not reviewable.',1;
              UPDATE pp SET PreparedAmount=@SalePrice,CostBasisAmount=source.ObservedUnitCost,
                CostBasisType=N'ObservedSupplierCost',TargetMarginPercent=@TargetMargin,
                EffectiveMarginPercent=@EffectiveMargin,InputMode=@InputMode
              FROM dbo.ProductPrices pp
              INNER JOIN dbo.Businesses target ON target.BusinessId=pp.BusinessId
              INNER JOIN dbo.PriceRevisionProposals source ON source.PriceRevisionProposalId=@ProposalId
              WHERE pp.ProductId=@ProductId AND pp.IsActive=1
                AND ((@SharesPrices=1 AND target.TenantId=@ScopeTenantId
                      AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@SharesPrices=0 AND pp.BusinessId=@BusinessId));

              UPDATE proposal SET TargetMarginPercent=@TargetMargin,SuggestedSalePrice=@SalePrice,
                RoundedSuggestedSalePrice=@SalePrice,EffectiveMarginAfterRounding=@EffectiveMargin,
                LastInputMode=@InputMode,Status=N'Approved',ReviewedByUserId=@UserId,ReviewedAt=SYSDATETIMEOFFSET()
              FROM dbo.PriceRevisionProposals proposal
              INNER JOIN dbo.Businesses target ON target.BusinessId=proposal.BusinessId
              WHERE proposal.ProductId=@ProductId AND proposal.Status=N'PendingReview'
                AND ((@SharesPrices=1 AND target.TenantId=@ScopeTenantId
                      AND target.SharesProductPrices=1 AND target.IsActive=1)
                  OR (@SharesPrices=0 AND proposal.BusinessId=@BusinessId));
            END
            ELSE
            BEGIN
              UPDATE pp SET PreparedAmount=@SalePrice,TargetMarginPercent=@TargetMargin,
                EffectiveMarginPercent=@EffectiveMargin,InputMode=@InputMode
              FROM dbo.ProductPrices pp
              INNER JOIN dbo.Businesses b ON b.BusinessId=pp.BusinessId
              WHERE pp.ProductPriceId=@ProposalId AND pp.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND pp.IsActive=1 AND pp.RowVersion=@RowVersion;
              IF @@ROWCOUNT=0 THROW 51601,'The prepared product price changed or is no longer available.',1;
            END
            """, connection);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProposalId", proposalId);
        command.Parameters.AddWithValue("@TargetMargin", (object?)calculation.TargetMarginPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("@SalePrice", calculation.RoundedSalePrice);
        command.Parameters.AddWithValue("@EffectiveMargin", (object?)calculation.EffectiveMarginPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("@InputMode", calculation.InputMode);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = expectedRowVersion;
        await ExecuteAsync(command, ct);
    }

    public async Task RejectAsync(
        PricingUserIdentity user, Guid proposalId, byte[] expectedRowVersion,
        string? reason, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
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
            END
            ELSE
            BEGIN
              UPDATE pp SET PreparedAmount=Amount,TargetMarginPercent=EffectiveMarginPercent
              FROM dbo.ProductPrices pp
              INNER JOIN dbo.Businesses b ON b.BusinessId=pp.BusinessId
              WHERE pp.ProductPriceId=@ProposalId AND pp.BusinessId=@BusinessId
                AND b.TenantId=@TenantId AND pp.RowVersion=@RowVersion
                AND pp.IsActive=1 AND ABS(pp.PreparedAmount-pp.Amount)>=0.0001;
              IF @@ROWCOUNT=0 THROW 51601,'The prepared product price changed or is no longer available.',1;
            END
            """, connection);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProposalId", proposalId);
        command.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = expectedRowVersion;
        await ExecuteAsync(command, ct);
    }

    public async Task<PublishPricesResult> PublishAsync(
        PricingUserIdentity user, IReadOnlyList<PreparedPricePublication> values,
        DateTimeOffset now, CancellationToken ct)
    {
        if (values.Select(x => x.ProductId).Distinct().Count() != values.Count)
            throw new PricingConflictException("A product can only be published once per batch.");
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var published = new List<PublishedPrice>(values.Count);
        long highestCursor = 0;
        try
        {
            foreach (var value in values)
            {
                await ValidateProposalAsync(connection, transaction, user, value, ct);
                var priceId = ids.NewId();
                var notificationId = ids.NewId();
                await using var command = new SqlCommand("""
                    DECLARE @SharesPrices BIT,@TenantIdForBusiness UNIQUEIDENTIFIER;
                    SELECT @SharesPrices=SharesProductPrices,@TenantIdForBusiness=TenantId
                    FROM dbo.Businesses WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId;
                    DECLARE @Targets TABLE(BusinessId UNIQUEIDENTIFIER PRIMARY KEY,PreviousSalePrice DECIMAL(19,4));
                    INSERT @Targets
                    SELECT business.BusinessId,price.Amount
                    FROM dbo.Businesses business
                    INNER JOIN dbo.ProductPrices price WITH(UPDLOCK,HOLDLOCK)
                      ON price.BusinessId=business.BusinessId AND price.ProductId=@ProductId AND price.IsActive=1
                    WHERE (@SharesPrices=1 AND business.TenantId=@TenantIdForBusiness
                           AND business.SharesProductPrices=1 AND business.IsActive=1)
                       OR (@SharesPrices=0 AND business.BusinessId=@BusinessId);
                    IF NOT EXISTS(SELECT 1 FROM @Targets WHERE BusinessId=@BusinessId)
                      THROW 51601,'The active product price no longer exists.',1;

                    UPDATE price
                    SET IsActive=0,ValidUntil=@Now
                    FROM dbo.ProductPrices price INNER JOIN @Targets target ON target.BusinessId=price.BusinessId
                    WHERE price.ProductId=@ProductId AND price.IsActive=1;

                    INSERT dbo.ProductPrices
                      (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
                       CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
                       InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt,PublishedByUserId,PublishedAt)
                    SELECT
                      CASE WHEN target.BusinessId=@BusinessId THEN @ProductPriceId ELSE NEWID() END,
                      target.BusinessId,@ProductId,@SalePrice,@SalePrice,N'COP',
                       @CostBasisType,@CostBasis,@TargetMargin,@EffectiveMargin,
                       @InputMode,@RoundingIncrement,@RoundingMode,@Now,1,@Now,@UserId,@Now
                    FROM @Targets target;
                    IF @IsManual=0
                    BEGIN
                      UPDATE proposal
                      SET Status=N'Published',ReviewedByUserId=@UserId,ReviewedAt=@Now,
                          TargetMarginPercent=@TargetMargin,SuggestedSalePrice=@SalePrice,
                          RoundedSuggestedSalePrice=@SalePrice,EffectiveMarginAfterRounding=@EffectiveMargin,
                          LastInputMode=@InputMode
                      FROM dbo.PriceRevisionProposals proposal
                      INNER JOIN @Targets target ON target.BusinessId=proposal.BusinessId
                      WHERE proposal.ProductId=@ProductId
                        AND proposal.Status IN(N'PendingReview',N'Approved');
                    END

                    DECLARE @Change TABLE(CatalogChangeId BIGINT NOT NULL,BusinessId UNIQUEIDENTIFIER NOT NULL);
                    INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                      OUTPUT inserted.CatalogChangeId,inserted.BusinessId INTO @Change
                      SELECT BusinessId,@ProductId,N'Upsert',@Now FROM @Targets;
                    INSERT dbo.PosSynchronizationOutboxMessages
                      (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                    SELECT CASE WHEN BusinessId=@BusinessId THEN @NotificationId ELSE NEWID() END,
                           BusinessId,N'Catalog',CatalogChangeId,@Now FROM @Change;

                    INSERT dbo.PricePublicationAudits
                      (PricePublicationAuditId,BusinessId,ProductId,ProductPriceId,
                       ProposalId,PublicationOrigin,PreviousSalePrice,PublishedSalePrice,CostBasisAmount,
                       EffectiveMarginPercent,InputMode,PublishedByUserId,PublishedAt)
                    SELECT CASE WHEN target.BusinessId=@BusinessId THEN @AuditId ELSE NEWID() END,
                           target.BusinessId,@ProductId,price.ProductPriceId,
                           CASE WHEN target.BusinessId=@BusinessId THEN @AuditProposalId ELSE NULL END,
                           @PublicationOrigin,target.PreviousSalePrice,@SalePrice,@CostBasis,
                           @EffectiveMargin,@InputMode,@UserId,@Now
                    FROM @Targets target
                    INNER JOIN dbo.ProductPrices price ON price.BusinessId=target.BusinessId
                      AND price.ProductId=@ProductId AND price.IsActive=1;

                    SELECT MAX(CatalogChangeId) FROM @Change;
                    """, connection, transaction);
                AddScope(command, user);
                command.Parameters.AddWithValue("@ProductPriceId", priceId);
                command.Parameters.AddWithValue("@AuditId", ids.NewId());
                command.Parameters.AddWithValue("@NotificationId", notificationId);
                command.Parameters.AddWithValue("@ProposalId", value.ProposalId);
                command.Parameters.AddWithValue("@IsManual", value.IsManual);
                command.Parameters.Add("@AuditProposalId", SqlDbType.UniqueIdentifier).Value =
                    value.IsManual ? DBNull.Value : value.ProposalId;
                command.Parameters.AddWithValue("@PublicationOrigin", value.IsManual ? "Manual" : "ReceiptProposal");
                command.Parameters.AddWithValue("@ProductId", value.ProductId);
                command.Parameters.Add("@CostBasis", SqlDbType.Decimal).Value =
                    (object?)value.CostBasisAmount ?? DBNull.Value;
                command.Parameters["@CostBasis"].Precision = 19;
                command.Parameters["@CostBasis"].Scale = 4;
                command.Parameters.AddWithValue("@CostBasisType", value.IsManual ? "Manual" : "ObservedSupplierCost");
                command.Parameters.AddWithValue("@TargetMargin", (object?)value.TargetMarginPercent ?? DBNull.Value);
                command.Parameters.AddWithValue("@EffectiveMargin", (object?)value.EffectiveMarginPercent ?? DBNull.Value);
                command.Parameters.AddWithValue("@InputMode", value.InputMode);
                command.Parameters.AddWithValue("@RoundingIncrement", value.RoundingIncrement);
                command.Parameters.AddWithValue("@RoundingMode", value.RoundingMode);
                command.Parameters.AddWithValue("@SalePrice", value.SalePrice);
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = value.ExpectedRowVersion;
                var cursor = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
                highestCursor = Math.Max(highestCursor, cursor);
                published.Add(new(priceId, value.ProposalId, value.ProductId,
                    value.SalePrice, value.EffectiveMarginPercent, cursor, now));
                var linked = await PrepareLinkedCostsAsync(connection, transaction, user, value, ct);
                published.AddRange(linked);
                if (linked.Count > 0) highestCursor = Math.Max(highestCursor, linked.Max(item => item.CatalogCursor));
            }
            await transaction.CommitAsync(ct);
            return new(published, highestCursor);
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

    private static async Task<IReadOnlyList<PublishedPrice>> PrepareLinkedCostsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PricingUserIdentity user,
        PreparedPricePublication source,
        CancellationToken ct)
    {
        var links = new List<(Guid ProductId, decimal Factor)>();
        await using (var query = new SqlCommand("""
            SELECT ChildProductId,PriceFactor
            FROM dbo.ProductLinks WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND ParentProductId=@ProductId
              AND SharesPrice=1 AND IsActive=1
            ORDER BY ChildProductId;
            """, connection, transaction))
        {
            query.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            query.Parameters.AddWithValue("@ProductId", source.ProductId);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                links.Add((reader.GetGuid(0), reader.GetDecimal(1)));
        }

        var result = new List<PublishedPrice>();
        foreach (var link in links)
        {
            if (source.CostBasisAmount is null or <= 0)
                throw new PricingConflictException(
                    "El producto principal necesita un costo válido para actualizar sus productos vinculados.");
            var costBasis = decimal.Round(
                source.CostBasisAmount.Value * link.Factor, 6,
                MidpointRounding.AwayFromZero);
            await using var command = new SqlCommand("""
                DECLARE @SharesPrices BIT;
                SELECT @SharesPrices=SharesProductPrices FROM dbo.Businesses WHERE BusinessId=@BusinessId;
                IF NOT EXISTS(
                  SELECT 1 FROM dbo.ProductPrices price WITH(UPDLOCK,HOLDLOCK)
                  JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
                  WHERE price.ProductId=@ProductId AND price.IsActive=1
                    AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                      OR (@SharesPrices=0 AND target.BusinessId=@BusinessId)))
                  THROW 51600,'El producto vinculado necesita un margen válido para preparar su precio.',1;
                IF EXISTS(
                  SELECT 1 FROM dbo.ProductPrices price WITH(UPDLOCK,HOLDLOCK)
                  JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
                  WHERE price.ProductId=@ProductId AND price.IsActive=1
                    AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                      OR (@SharesPrices=0 AND target.BusinessId=@BusinessId))
                    AND (COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent) IS NULL
                      OR COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)<0
                      OR COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)>=100))
                  THROW 51600,'El producto vinculado necesita un margen válido para preparar su precio.',1;
                UPDATE price
                SET CostBasisType=N'LinkedProduct',CostBasisAmount=@CostBasis,
                    TargetMarginPercent=COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent),
                    PreparedAmount=ROUND((@CostBasis/(1-(COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)/100)))
                      *(1+(COALESCE(tax.Rate,0)/100)),4)
                FROM dbo.ProductPrices price
                JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
                JOIN dbo.Products product ON product.ProductId=price.ProductId AND product.TenantId=@TenantId
                LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=product.TaxProfileId
                WHERE price.ProductId=@ProductId AND price.IsActive=1
                  AND COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)>=0
                  AND COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)<100
                  AND ((@SharesPrices=1 AND target.TenantId=@TenantId AND target.SharesProductPrices=1 AND target.IsActive=1)
                    OR (@SharesPrices=0 AND target.BusinessId=@BusinessId));
                """, connection, transaction);
            AddScope(command, user);
            command.Parameters.AddWithValue("@ProductId", link.ProductId);
            command.Parameters.Add("@CostBasis", SqlDbType.Decimal).Value = costBasis;
            command.Parameters["@CostBasis"].Precision = 19;
            command.Parameters["@CostBasis"].Scale = 6;
            await command.ExecuteNonQueryAsync(ct);
        }
        return result;
    }

    public async Task<ProductPricingContext?> GetProductContextAsync(
        PricingUserIdentity user, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT x.ProductId,x.Name,COALESCE(price.PreparedAmount,price.Amount,0),
              COALESCE(price.Amount,0),
              COALESCE(cost.LatestUnitCost,price.CostBasisAmount),
              CASE WHEN cost.LatestUnitCost IS NOT NULL THEN N'ObservedSupplierCost'
                   ELSE price.CostBasisType END,
              price.EffectiveMarginPercent,COALESCE(tax.Rate,0),
              COALESCE(price.RoundingIncrement,1),COALESCE(price.RoundingMode,N'Nearest')
            FROM dbo.Products x
            INNER JOIN dbo.Businesses b ON b.BusinessId=@BusinessId
            LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=x.TaxProfileId
            OUTER APPLY (
              SELECT TOP(1) p.Amount,p.PreparedAmount,p.CostBasisAmount,p.CostBasisType,p.EffectiveMarginPercent,p.RoundingIncrement,p.RoundingMode
              FROM dbo.ProductPrices p
              WHERE p.BusinessId=@BusinessId AND p.ProductId=x.ProductId
                AND p.IsActive=1 AND p.ValidFrom<=SYSDATETIMEOFFSET()
                AND (p.ValidUntil IS NULL OR p.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY p.ValidFrom DESC,p.ProductPriceId
            ) price
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
                reader.GetDecimal(8),reader.GetString(9))
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

                DECLARE @SharesPrices BIT,@ScopeTenantId UNIQUEIDENTIFIER;
                SELECT @SharesPrices=SharesProductPrices,@ScopeTenantId=TenantId
                FROM dbo.Businesses WHERE BusinessId=@BusinessId;

                IF EXISTS(SELECT 1 FROM dbo.ProductPrices WITH(UPDLOCK,HOLDLOCK)
                          WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1)
                  UPDATE price
                  SET PreparedAmount=@PreparedAmount,CostBasisType=@CostBasisType,
                      CostBasisAmount=@CostBasis,TargetMarginPercent=@TargetMargin,
                      EffectiveMarginPercent=@EffectiveMargin,InputMode=@InputMode,
                      RoundingIncrement=@RoundingIncrement,RoundingMode=@RoundingMode
                  FROM dbo.ProductPrices price INNER JOIN dbo.Businesses target ON target.BusinessId=price.BusinessId
                  WHERE price.ProductId=@ProductId AND price.IsActive=1
                    AND ((@SharesPrices=1 AND target.TenantId=@ScopeTenantId
                          AND target.SharesProductPrices=1 AND target.IsActive=1)
                      OR (@SharesPrices=0 AND price.BusinessId=@BusinessId));
                ELSE
                  INSERT dbo.ProductPrices
                    (ProductPriceId,BusinessId,ProductId,Amount,PreparedAmount,CurrencyCode,
                     CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
                     InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt)
                  VALUES
                    (@ProductPriceId,@BusinessId,@ProductId,0,@PreparedAmount,N'COP',
                     @CostBasisType,@CostBasis,@TargetMargin,@EffectiveMargin,@InputMode,
                     @RoundingIncrement,@RoundingMode,@Now,1,@Now);

                SELECT ProductPriceId,PreparedAmount,Amount
                FROM dbo.ProductPrices
                WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;
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
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var priceId = reader.GetGuid(0);
            var preparedAmount = reader.GetDecimal(1);
            var publicAmount = reader.GetDecimal(2);
            await reader.CloseAsync();
            await transaction.CommitAsync(ct);
            return new(priceId,value.ProductId,preparedAmount,publicAmount,
                value.CostBasisAmount,value.EffectiveMarginPercent,now);
        }
        catch (SqlException exception) when (exception.Number is 51600 or 2601 or 2627)
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

    public async Task<IReadOnlyList<ProductPriceHistoryItem>> HistoryAsync(
        PricingUserIdentity user, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT p.ProductPriceId,p.ProductId,p.Amount,p.CurrencyCode,p.CostBasisAmount,
              p.EffectiveMarginPercent,p.InputMode,p.ValidFrom,p.ValidUntil,
              p.PublishedByUserId,p.PublishedAt,p.IsActive
            FROM dbo.ProductPrices p
            INNER JOIN dbo.Products x ON x.ProductId=p.ProductId
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            WHERE p.ProductId=@ProductId AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId
            ORDER BY p.ValidFrom DESC;
            """, connection);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProductId", productId);
        var items = new List<ProductPriceHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDateTimeOffset(7), reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
                reader.IsDBNull(9) ? null : reader.GetGuid(9),
                reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10), reader.GetBoolean(11)));
        return items;
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
              SELECT 1 FROM dbo.ProductPrices pp WITH(UPDLOCK,HOLDLOCK)
              INNER JOIN dbo.Businesses b ON b.BusinessId=pp.BusinessId
              WHERE pp.ProductPriceId=@ProposalId AND pp.ProductId=@ProductId
                AND pp.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND pp.RowVersion=@RowVersion AND pp.IsActive=1
                AND ABS(pp.PreparedAmount-pp.Amount)>=0.0001)
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
