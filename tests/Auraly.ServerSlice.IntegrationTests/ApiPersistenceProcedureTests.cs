using System.Data;
using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ApiPersistenceProcedureTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Authentication_email_outbox_procedures_preserve_lease_retry_and_completion()
    {
        var retryMessageId = Guid.NewGuid();
        var completeMessageId = Guid.NewGuid();
        var firstLeaseId = Guid.NewGuid();
        var secondLeaseId = Guid.NewGuid();

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var seed = new SqlCommand("""
                INSERT dbo.TenantProvisioningOutboxMessages
                    (MessageId,TenantId,Type,Payload,OccurredAt,AvailableAt)
                VALUES
                    (@RetryId,@TenantId,N'PasswordRecoveryEmail',N'{}','1900-01-01T00:00:00+00:00','1900-01-01T00:00:00+00:00'),
                    (@CompleteId,@TenantId,N'TenantAdministratorInvitation',N'{}','1900-01-02T00:00:00+00:00','1900-01-02T00:00:00+00:00');
                """, connection, transaction))
            {
                seed.Parameters.AddWithValue("@RetryId", retryMessageId);
                seed.Parameters.AddWithValue("@CompleteId", completeMessageId);
                seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                await seed.ExecuteNonQueryAsync();
            }

            Assert.Equal(retryMessageId, await ClaimAsync(connection, transaction, firstLeaseId));

            await using (var retry = Procedure(
                "dbo.AuthenticationEmailOutboxRetry", connection, transaction))
            {
                retry.Parameters.AddWithValue("@MessageId", retryMessageId);
                retry.Parameters.AddWithValue("@LeaseId", firstLeaseId);
                retry.Parameters.AddWithValue("@Delay", 60);
                retry.Parameters.AddWithValue("@Error", "Transient test failure");
                await retry.ExecuteNonQueryAsync();
            }

            Assert.Equal(completeMessageId, await ClaimAsync(connection, transaction, secondLeaseId));

            await using (var complete = Procedure(
                "dbo.AuthenticationEmailOutboxComplete", connection, transaction))
            {
                complete.Parameters.AddWithValue("@MessageId", completeMessageId);
                complete.Parameters.AddWithValue("@LeaseId", secondLeaseId);
                await complete.ExecuteNonQueryAsync();
            }

            await using (var state = new SqlCommand("""
                SELECT MessageId,ProcessedAt,LeaseId,LastError
                FROM dbo.TenantProvisioningOutboxMessages
                WHERE MessageId IN(@RetryId,@CompleteId)
                ORDER BY MessageId;
                """, connection, transaction))
            {
                state.Parameters.AddWithValue("@RetryId", retryMessageId);
                state.Parameters.AddWithValue("@CompleteId", completeMessageId);
                var values = new Dictionary<Guid, (bool Processed, bool Leased, string? Error)>();
                await using var reader = await state.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    values.Add(reader.GetGuid(0),
                        (!reader.IsDBNull(1), !reader.IsDBNull(2),
                            reader.IsDBNull(3) ? null : reader.GetString(3)));

                Assert.False(values[retryMessageId].Processed);
                Assert.False(values[retryMessageId].Leased);
                Assert.Equal("Transient test failure", values[retryMessageId].Error);
                Assert.True(values[completeMessageId].Processed);
                Assert.False(values[completeMessageId].Leased);
                Assert.Null(values[completeMessageId].Error);
            }

            await using var recipient = Procedure(
                "dbo.AuthenticationInvitationRecipientGet", connection, transaction);
            recipient.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            Assert.False(string.IsNullOrWhiteSpace((string?)await recipient.ExecuteScalarAsync()));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task Price_channel_settings_endpoint_uses_the_versioned_procedure_and_restores_state()
    {
        using var client = fixture.CreateAdminClient(
            "pricing.segments.read", "pricing.segments.manage");
        var channelId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand("""
                INSERT dbo.PriceChannels
                    (PriceChannelId,BusinessId,Code,Name,Strategy,Value,IsActive,CreatedAt)
                VALUES
                    (@Id,@BusinessId,@Code,N'Canal integración',N'TieredProductPrice',NULL,1,SYSDATETIMEOFFSET());
                """, connection);
            seed.Parameters.AddWithValue("@Id", channelId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@Code", $"IT-{channelId:N}"[..12]);
            await seed.ExecuteNonQueryAsync();
        }
        var changedName = $"Canal integración {Guid.NewGuid():N}";

        try
        {
            using var update = await client.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/segments/{channelId:D}/settings",
                new SavePriceChannelSettingsRequest(
                    changedName, "PercentageOverBasePrice", 7.5m));
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

            var updated = Assert.Single(
                (await client.GetFromJsonAsync<PriceSegmentSummary[]>(
                    "/api/commerce/v1/pricing/segments/"))!
                .Where(segment => segment.Id == channelId));
            Assert.Equal(changedName, updated.Name);
            Assert.Equal("PercentageOverBasePrice", updated.Strategy);
            Assert.Equal(7.5m, updated.Value);

            using var negativeCostPercentage = await client.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/segments/{channelId:D}/settings",
                new SavePriceChannelSettingsRequest(
                    changedName, "MarginOverLatestCost", -1m));
            Assert.Equal(HttpStatusCode.BadRequest, negativeCostPercentage.StatusCode);

            using var negativeFixedMargin = await client.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/segments/{channelId:D}/settings",
                new SavePriceChannelSettingsRequest(
                    changedName, "FixedMarginOverAverageCost", -1m));
            Assert.Equal(HttpStatusCode.BadRequest, negativeFixedMargin.StatusCode);

            using var redundantStrategy = await client.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/segments/{channelId:D}/settings",
                new SavePriceChannelSettingsRequest(
                    changedName, "PercentageBelowBasePrice", 10m));
            Assert.Equal(HttpStatusCode.BadRequest, redundantStrategy.StatusCode);

            using var marginAdjustment = await client.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/segments/{channelId:D}/settings",
                new SavePriceChannelSettingsRequest(
                    changedName, "ProductMarginAdjustment", -10m));
            Assert.Equal(HttpStatusCode.NoContent, marginAdjustment.StatusCode);
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand(
                "DELETE dbo.PriceChannels WHERE PriceChannelId=@Id AND BusinessId=@BusinessId;",
                connection);
            cleanup.Parameters.AddWithValue("@Id", channelId);
            cleanup.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Price_channel_creation_persists_product_and_line_exclusions_in_the_same_request()
    {
        using var client = fixture.CreateAdminClient("pricing.segments.read", "pricing.segments.manage");
        var name = $"Canal con excluidos {Guid.NewGuid():N}";
        var areaId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        Guid? channelId = null;
        await using (var setup = new SqlConnection(fixture.ConnectionString))
        {
            await setup.OpenAsync();
            await using var command = new SqlCommand("""
                INSERT dbo.ProductCategories(ProductCategoryId,BusinessId,Name,IsActive,IsBrowsable)
                VALUES(@AreaId,@BusinessId,N'Área exclusión',1,1);
                INSERT dbo.ProductCategories(ProductCategoryId,BusinessId,ParentProductCategoryId,Name,IsActive,IsBrowsable)
                VALUES(@LineId,@BusinessId,@AreaId,N'Línea exclusión',1,1);
                """, setup);
            command.Parameters.AddWithValue("@AreaId", areaId);
            command.Parameters.AddWithValue("@LineId", lineId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await command.ExecuteNonQueryAsync();
        }
        try
        {
            using var create = await client.PostAsJsonAsync(
                "/api/commerce/v1/pricing/segments/",
                new SavePriceSegmentRequest(name, "PercentageOverBasePrice", -5m, null,
                [
                    new CreatePriceChannelExclusionRequest("Product", fixture.ProductId),
                    new CreatePriceChannelExclusionRequest("Category", lineId)
                ]));
            Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
            var channel = (await create.Content.ReadFromJsonAsync<PriceSegmentSummary>())!;
            channelId = channel.Id;
            var exclusions = (await client.GetFromJsonAsync<PriceChannelExclusion[]>(
                $"/api/commerce/v1/pricing/segments/{channel.Id:D}/exclusions"))!;
            Assert.Equal(2, exclusions.Length);
            Assert.Contains(exclusions, exclusion =>
                exclusion.ScopeType == "Product" && exclusion.ScopeId == fixture.ProductId);
            Assert.Contains(exclusions, exclusion =>
                exclusion.ScopeType == "Category" && exclusion.ScopeId == lineId
                && exclusion.CategoryDepth == 1 && exclusion.ScopeName.Contains("Línea exclusión"));
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand(
                """
                DELETE dbo.PriceChannelExclusions WHERE PriceChannelId=@Id;
                DELETE dbo.PriceChannels WHERE PriceChannelId=@Id;
                DELETE dbo.ProductCategories WHERE ProductCategoryId=@LineId;
                DELETE dbo.ProductCategories WHERE ProductCategoryId=@AreaId;
                """,
                connection);
            cleanup.Parameters.AddWithValue("@Id", (object?)channelId ?? Guid.Empty);
            cleanup.Parameters.AddWithValue("@LineId", lineId);
            cleanup.Parameters.AddWithValue("@AreaId", areaId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Canonical_price_channel_formula_adjusts_product_margin_and_never_goes_below_average_cost()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT dbo.PriceChannelAmountCalculate(N'ProductMarginAdjustment',10,150,100,110,20,NULL)
            UNION ALL SELECT dbo.PriceChannelAmountCalculate(N'ProductMarginAdjustment',-30,150,100,110,20,NULL)
            UNION ALL SELECT dbo.PriceChannelAmountCalculate(N'PercentageOverBasePrice',-50,150,100,110,NULL,NULL)
            UNION ALL SELECT dbo.PriceChannelAmountCalculate(N'TieredProductPrice',NULL,150,100,110,NULL,80)
            UNION ALL SELECT dbo.PriceChannelAmountCalculate(N'MarginOverLatestCost',20,150,100,120,NULL,NULL);
            """, connection);

        var amounts = new List<decimal>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) amounts.Add(reader.GetDecimal(0));

        Assert.Equal([171.4286m, 120m, 100m, 100m, 150m], amounts);
    }

    private static async Task<Guid> ClaimAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid leaseId)
    {
        await using var claim = Procedure(
            "dbo.AuthenticationEmailOutboxClaim", connection, transaction);
        claim.Parameters.AddWithValue("@LeaseId", leaseId);
        await using var reader = await claim.ExecuteReaderAsync(CommandBehavior.SingleRow);
        Assert.True(await reader.ReadAsync());
        Assert.Equal(leaseId, reader.GetGuid(5));
        return reader.GetGuid(0);
    }

    private static SqlCommand Procedure(
        string name,
        SqlConnection connection,
        SqlTransaction transaction) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure };
}
