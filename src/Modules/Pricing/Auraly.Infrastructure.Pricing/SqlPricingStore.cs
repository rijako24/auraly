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
        const string where = """
            FROM dbo.PriceRevisionProposals p
            INNER JOIN dbo.Products x ON x.ProductId=p.ProductId AND x.BusinessId=p.BusinessId
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            INNER JOIN dbo.GoodsReceipts g ON g.GoodsReceiptId=p.SourceDocumentId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=g.SupplierId
            WHERE b.TenantId=@TenantId AND p.BusinessId=@BusinessId
              AND (@Status IS NULL OR p.Status=@Status)
              AND (@SupplierId IS NULL OR s.SupplierId=@SupplierId)
              AND (@SourceDocumentId IS NULL OR p.SourceDocumentId=@SourceDocumentId)
              AND (@Search IS NULL OR x.ProductCode LIKE '%'+@Search+'%'
                   OR x.Name LIKE '%'+@Search+'%' OR s.Name LIKE '%'+@Search+'%')
            """;
        int total;
        await using (var count = new SqlCommand("SELECT COUNT(*) "+where, connection))
        {
            AddQuery(count, user, query);
            total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
        }
        const string select = """
            SELECT p.PriceRevisionProposalId,p.ProductId,COALESCE(x.ProductCode,x.Sku,CONVERT(nvarchar(36),x.ProductId)),x.Name,
              p.SourceDocumentId,p.SourceLineNumber,s.Name,p.PreviousObservedUnitCost,
              p.ObservedUnitCost,p.CurrentSalePrice,p.CurrentMarginPercent,
              p.TargetMarginPercent,p.SuggestedSalePrice,p.EffectiveMarginAfterRounding,
              p.Status,p.CreatedAt,p.RowVersion
            """;
        await using var command = new SqlCommand(
            select + Environment.NewLine + where + Environment.NewLine + """
            ORDER BY p.CreatedAt DESC,p.PriceRevisionProposalId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """, connection);
        AddQuery(command, user, query);
        command.Parameters.AddWithValue("@Skip", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@Take", query.PageSize);
        var items = new List<PriceRevisionListItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.GetGuid(4), reader.GetInt32(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7), reader.GetDecimal(8),
                reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11), reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13), reader.GetString(14),
                reader.GetDateTimeOffset(15), Convert.ToBase64String(reader.GetFieldValue<byte[]>(16))));
        }
        return new(items, query.Page, query.PageSize, total);
    }

    public async Task<PriceProposalSource?> GetProposalAsync(
        PricingUserIdentity user, Guid proposalId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT p.PriceRevisionProposalId,p.ProductId,p.ObservedUnitCost,p.Status,p.RowVersion
            FROM dbo.PriceRevisionProposals p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            WHERE p.PriceRevisionProposalId=@ProposalId
              AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@ProposalId", proposalId);
        AddScope(command, user);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetString(3), reader.GetFieldValue<byte[]>(4))
            : null;
    }

    public async Task ReviewAsync(
        PricingUserIdentity user, Guid proposalId, PriceCalculationResult calculation,
        byte[] expectedRowVersion, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE p SET TargetMarginPercent=@TargetMargin,
              SuggestedSalePrice=@SalePrice,
              RoundedSuggestedSalePrice=@SalePrice,
              EffectiveMarginAfterRounding=@EffectiveMargin,
              LastInputMode=@InputMode,
              Status=N'Approved',ReviewedByUserId=@UserId,ReviewedAt=SYSDATETIMEOFFSET()
            FROM dbo.PriceRevisionProposals p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId
              AND b.TenantId=@TenantId AND p.RowVersion=@RowVersion
              AND p.Status IN (N'PendingReview',N'Approved');
            IF @@ROWCOUNT=0 THROW 51601,'The price proposal changed or is not reviewable.',1;
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
            UPDATE p SET Status=N'Rejected',ReviewedByUserId=@UserId,
              ReviewedAt=SYSDATETIMEOFFSET(),RejectReason=@Reason
            FROM dbo.PriceRevisionProposals p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            WHERE p.PriceRevisionProposalId=@ProposalId AND p.BusinessId=@BusinessId
              AND b.TenantId=@TenantId AND p.RowVersion=@RowVersion
              AND p.Status IN (N'PendingReview',N'Approved');
            IF @@ROWCOUNT=0 THROW 51601,'The price proposal changed or is not reviewable.',1;
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
                    UPDATE dbo.ProductPrices
                    SET IsActive=0,ValidUntil=@Now
                    WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;

                    INSERT dbo.ProductPrices
                      (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,
                       CostBasisType,CostBasisAmount,TargetMarginPercent,EffectiveMarginPercent,
                       InputMode,RoundingIncrement,RoundingMode,ValidFrom,IsActive,CreatedAt,PublishedByUserId,PublishedAt)
                    VALUES
                      (@ProductPriceId,@BusinessId,@ProductId,@SalePrice,N'COP',
                       N'ObservedSupplierCost',@CostBasis,@TargetMargin,@EffectiveMargin,
                       @InputMode,@RoundingIncrement,@RoundingMode,@Now,1,@Now,@UserId,@Now);

                    UPDATE dbo.PriceRevisionProposals
                    SET Status=N'Published',ReviewedByUserId=@UserId,ReviewedAt=@Now,
                        TargetMarginPercent=@TargetMargin,SuggestedSalePrice=@SalePrice,
                        RoundedSuggestedSalePrice=@SalePrice,
                        EffectiveMarginAfterRounding=@EffectiveMargin,
                        LastInputMode=@InputMode
                    WHERE PriceRevisionProposalId=@ProposalId AND BusinessId=@BusinessId
                      AND RowVersion=@RowVersion AND Status IN(N'PendingReview',N'Approved');
                    IF @@ROWCOUNT=0 THROW 51601,'The price proposal changed before publication.',1;

                    DECLARE @Change TABLE(CatalogChangeId BIGINT NOT NULL);
                    INSERT dbo.CatalogChanges(BusinessId,ProductId,ChangeKind,OccurredAt)
                      OUTPUT inserted.CatalogChangeId INTO @Change
                      VALUES(@BusinessId,@ProductId,N'Upsert',@Now);
                    INSERT dbo.PosSynchronizationOutboxMessages
                      (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
                    SELECT @NotificationId,@BusinessId,N'Catalog',CatalogChangeId,@Now FROM @Change;

                    INSERT dbo.PricePublicationAudits
                      (PricePublicationAuditId,BusinessId,ProductId,ProductPriceId,
                       ProposalId,PreviousSalePrice,PublishedSalePrice,CostBasisAmount,
                       EffectiveMarginPercent,InputMode,PublishedByUserId,PublishedAt)
                    SELECT @AuditId,@BusinessId,@ProductId,@ProductPriceId,@ProposalId,
                      old.Amount,@SalePrice,@CostBasis,@EffectiveMargin,@InputMode,@UserId,@Now
                    FROM (SELECT TOP(1) Amount FROM dbo.ProductPrices
                          WHERE BusinessId=@BusinessId AND ProductId=@ProductId
                            AND ProductPriceId<>@ProductPriceId
                          ORDER BY ValidFrom DESC) old;

                    SELECT CatalogChangeId FROM @Change;
                    """, connection, transaction);
                AddScope(command, user);
                command.Parameters.AddWithValue("@ProductPriceId", priceId);
                command.Parameters.AddWithValue("@AuditId", ids.NewId());
                command.Parameters.AddWithValue("@NotificationId", notificationId);
                command.Parameters.AddWithValue("@ProposalId", value.ProposalId);
                command.Parameters.AddWithValue("@ProductId", value.ProductId);
                command.Parameters.AddWithValue("@CostBasis", value.CostBasisAmount);
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
            IF NOT EXISTS(
              SELECT 1 FROM dbo.PriceRevisionProposals p WITH(UPDLOCK,HOLDLOCK)
              INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
              WHERE p.PriceRevisionProposalId=@ProposalId AND p.ProductId=@ProductId
                AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId
                AND p.RowVersion=@RowVersion AND p.Status IN(N'PendingReview',N'Approved'))
              THROW 51600,'The price proposal is outside scope, changed or already completed.',1;
            """, connection, transaction);
        AddScope(command, user);
        command.Parameters.AddWithValue("@ProposalId", value.ProposalId);
        command.Parameters.AddWithValue("@ProductId", value.ProductId);
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
