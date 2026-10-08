using System.Data;
using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Contracts.Pricing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Repositories;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ApiPersistenceProcedureTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Explicit_price_publication_replaces_the_active_branch_price_atomically()
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var repository = new ProductRepository(db);
        var product = await repository.GetByIdAsync(fixture.BusinessId, fixture.ProductId);
        Assert.NotNull(product);
        var newAmount = product.UnitPrice + 1m;

        await repository.PublishPriceAsync(product, newAmount, product.Currency);
        await db.SaveChangesAsync();

        var active = await db.PublishedProductPrices.SingleAsync(price =>
            price.ProductId == product.ProductId && price.BusinessId == fixture.BusinessId && price.IsActive);
        Assert.Equal(newAmount, active.Amount);
        await transaction.RollbackAsync();
    }

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
    public async Task Permanent_recipient_failure_exhausts_only_its_claimed_message()
    {
        var messageId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var seed = new SqlCommand("""
                INSERT dbo.TenantProvisioningOutboxMessages
                    (MessageId,TenantId,Type,Payload,OccurredAt,AvailableAt)
                VALUES (@MessageId,@TenantId,N'FiscalInvoiceDelivery',N'{}',
                        '1900-01-01T00:00:00+00:00','1900-01-01T00:00:00+00:00');
                """, connection, transaction))
            {
                seed.Parameters.AddWithValue("@MessageId", messageId);
                seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                await seed.ExecuteNonQueryAsync();
            }

            Assert.Equal(messageId, await ClaimAsync(connection, transaction, leaseId));
            await using (var fail = Procedure("dbo.AuthenticationEmailOutboxRetry", connection, transaction))
            {
                fail.Parameters.AddWithValue("@MessageId", messageId);
                fail.Parameters.AddWithValue("@LeaseId", leaseId);
                fail.Parameters.AddWithValue("@Delay", 15);
                fail.Parameters.AddWithValue("@Error", "EmailDroppedAllRecipientsSuppressed");
                fail.Parameters.AddWithValue("@Permanent", true);
                await fail.ExecuteNonQueryAsync();
            }

            await using var state = new SqlCommand("""
                SELECT AttemptCount,ProcessedAt,LeaseId,LastError
                FROM dbo.TenantProvisioningOutboxMessages WHERE MessageId=@MessageId;
                """, connection, transaction);
            state.Parameters.AddWithValue("@MessageId", messageId);
            await using var reader = await state.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(10, reader.GetInt32(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            Assert.Equal("EmailDroppedAllRecipientsSuppressed", reader.GetString(3));
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
                    (PriceChannelId,TenantId,Code,Name,Strategy,Value,IsActive,CreatedAt)
                VALUES
                    (@Id,@TenantId,@Code,N'Canal integración',N'TieredProductPrice',NULL,1,SYSDATETIMEOFFSET());
                """, connection);
            seed.Parameters.AddWithValue("@Id", channelId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
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

            var report = await client.GetFromJsonAsync<PriceChannelProductReport>(
                $"/api/commerce/v1/pricing/segments/{channelId:D}/product-price-report");
            Assert.NotNull(report);
            Assert.Equal(channelId, report.PriceChannelId);
            var productRow = Assert.Single(
                report.Items,
                item => item.ProductId == fixture.ProductId && item.MinimumQuantity == 1m);
            Assert.Equal(
                decimal.Round(productRow.PublicAmount * 1.075m, 2, MidpointRounding.ToEven),
                productRow.ChannelAmount);
            Assert.Equal("Canal", productRow.PriceSource);

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
                "DELETE dbo.PriceChannels WHERE PriceChannelId=@Id AND TenantId=@TenantId;",
                connection);
            cleanup.Parameters.AddWithValue("@Id", channelId);
            cleanup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
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
                INSERT dbo.ProductCategories(ProductCategoryId,TenantId,Name,IsActive,IsBrowsable)
                VALUES(@AreaId,@TenantId,N'Área exclusión',1,1);
                INSERT dbo.ProductCategories(ProductCategoryId,TenantId,ParentProductCategoryId,Name,IsActive,IsBrowsable)
                VALUES(@LineId,@TenantId,@AreaId,N'Línea exclusión',1,1);
                """, setup);
            command.Parameters.AddWithValue("@AreaId", areaId);
            command.Parameters.AddWithValue("@LineId", lineId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
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
            var report = await client.GetFromJsonAsync<PriceChannelProductReport>(
                $"/api/commerce/v1/pricing/segments/{channel.Id:D}/product-price-report");
            Assert.NotNull(report);
            Assert.DoesNotContain(report.Items, item => item.ProductId == fixture.ProductId);
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
    public async Task Price_channel_applies_to_all_current_and_future_tenant_businesses()
    {
        var sharedOne = Guid.NewGuid();
        var sharedTwo = Guid.NewGuid();
        var independent = Guid.NewGuid();
        var future = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var foreignBusiness = Guid.NewGuid();
        Guid? channelId = null;
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var setup = new SqlCommand("""
                INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,SharesProductPrices,IsActive,CreatedAt)
                VALUES(@SharedOne,@TenantId,N'Sede compartida A',N'',N'',N'',CONCAT(@SharedOne,N'@auraly.test'),N'',1,1,SYSUTCDATETIME()),
                      (@SharedTwo,@TenantId,N'Sede compartida B',N'',N'',N'',CONCAT(@SharedTwo,N'@auraly.test'),N'',1,1,SYSUTCDATETIME()),
                      (@Independent,@TenantId,N'Sede independiente',N'',N'',N'',CONCAT(@Independent,N'@auraly.test'),N'',0,1,SYSUTCDATETIME());
                DECLARE @RoleId UNIQUEIDENTIFIER=(SELECT TOP(1) RoleId FROM dbo.UserRoles WHERE UserId=@UserId);
                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
                VALUES(NEWID(),@UserId,@RoleId,@SharedOne,SYSUTCDATETIME()),
                      (NEWID(),@UserId,@RoleId,@SharedTwo,SYSUTCDATETIME()),
                      (NEWID(),@UserId,@RoleId,@Independent,SYSUTCDATETIME());
                """, connection);
            setup.Parameters.AddWithValue("@SharedOne", sharedOne);
            setup.Parameters.AddWithValue("@SharedTwo", sharedTwo);
            setup.Parameters.AddWithValue("@Independent", independent);
            setup.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            setup.Parameters.AddWithValue("@UserId", fixture.UserId);
            await setup.ExecuteNonQueryAsync();
        }
        try
        {
            using (var scope = fixture.CreateScope())
            {
                var access = scope.ServiceProvider.GetRequiredService<SqlExecutionContextDirectory>();
                Assert.True(await access.HasPermissionForBusinessesAsync(
                    fixture.UserId, fixture.TenantId, [sharedOne, sharedTwo],
                    "sales.returns.create", CancellationToken.None));
                Assert.False(await access.HasPermissionForBusinessesAsync(
                    fixture.UserId, fixture.TenantId, [sharedOne, Guid.NewGuid()],
                    "sales.returns.create", CancellationToken.None));
            }
            using var current = fixture.CreateAdminClient("pricing.segments.read", "pricing.segments.manage");
            using var created = await current.PostAsJsonAsync("/api/commerce/v1/pricing/segments/",
                new SavePriceSegmentRequest("Canal tenant", "PercentageOverBasePrice", 5m, null));
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
            var channel = (await created.Content.ReadFromJsonAsync<PriceSegmentSummary>())!;
            channelId = channel.Id;

            using var first = fixture.CreateAdminClientWithBusinessHeader(sharedOne,
                "pricing.segments.read", "pricing.segments.manage");
            using var second = fixture.CreateAdminClientWithBusinessHeader(sharedTwo,
                "pricing.segments.read", "pricing.segments.manage");
            using var third = fixture.CreateAdminClientWithBusinessHeader(independent,
                "pricing.segments.read", "pricing.segments.manage");
            Assert.Contains((await first.GetFromJsonAsync<PriceSegmentSummary[]>("/api/commerce/v1/pricing/segments/"))!, item => item.Id == channelId);
            Assert.Contains((await second.GetFromJsonAsync<PriceSegmentSummary[]>("/api/commerce/v1/pricing/segments/"))!, item => item.Id == channelId);
            Assert.Contains((await third.GetFromJsonAsync<PriceSegmentSummary[]>("/api/commerce/v1/pricing/segments/"))!, item => item.Id == channelId);

            using var edited = await first.PutAsJsonAsync(
                $"/api/commerce/v1/pricing/segments/{channelId:D}/settings",
                new SavePriceChannelSettingsRequest("Canal actualizado", "PercentageOverBasePrice", 6m));
            Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
            Assert.Contains((await first.GetFromJsonAsync<PriceSegmentSummary[]>("/api/commerce/v1/pricing/segments/"))!, item => item.Id == channelId && item.Value == 6m);
            Assert.Contains((await second.GetFromJsonAsync<PriceSegmentSummary[]>("/api/commerce/v1/pricing/segments/"))!, item => item.Id == channelId && item.Value == 6m);
            Assert.Contains((await third.GetFromJsonAsync<PriceSegmentSummary[]>("/api/commerce/v1/pricing/segments/"))!, item => item.Id == channelId);
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var addFuture = new SqlCommand("""
                    INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,SharesProductPrices,IsActive,CreatedAt)
                    VALUES(@Future,@TenantId,N'Sede futura',N'',N'',N'',CONCAT(@Future,N'@auraly.test'),N'',0,1,SYSUTCDATETIME());
                    DECLARE @RoleId UNIQUEIDENTIFIER=(SELECT TOP(1) RoleId FROM dbo.UserRoles WHERE UserId=@UserId);
                    INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
                    VALUES(NEWID(),@UserId,@RoleId,@Future,SYSUTCDATETIME());
                    """, connection);
                addFuture.Parameters.AddWithValue("@Future", future);
                addFuture.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                addFuture.Parameters.AddWithValue("@UserId", fixture.UserId);
                await addFuture.ExecuteNonQueryAsync();
            }
            using var fourth = fixture.CreateAdminClientWithBusinessHeader(future, "pricing.segments.read");
            Assert.Contains((await fourth.GetFromJsonAsync<PriceSegmentSummary[]>("/api/commerce/v1/pricing/segments/"))!, item => item.Id == channelId);
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var foreign = new SqlCommand("""
                    INSERT dbo.Tenants(TenantId,Name,Email,IsActive)
                    VALUES(@ForeignTenant,N'Tenant ajeno',CONCAT(@ForeignTenant,N'@auraly.test'),1);
                    INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,SharesProductPrices,IsActive,CreatedAt)
                    VALUES(@ForeignBusiness,@ForeignTenant,N'Sede ajena',N'',N'',N'',CONCAT(@ForeignBusiness,N'@auraly.test'),N'',0,1,SYSUTCDATETIME());
                    """, connection);
                foreign.Parameters.AddWithValue("@ForeignTenant", foreignTenant);
                foreign.Parameters.AddWithValue("@ForeignBusiness", foreignBusiness);
                await foreign.ExecuteNonQueryAsync();

                await using var list = new SqlCommand("dbo.PriceSegmentsList", connection)
                { CommandType = CommandType.StoredProcedure };
                list.Parameters.AddWithValue("@BusinessId", foreignBusiness);
                await using var reader = await list.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    Assert.NotEqual(channelId, reader.GetGuid(0));
            }
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var cleanup = new SqlCommand("""
                DELETE dbo.PriceChannels WHERE PriceChannelId=@ChannelId;
                DELETE dbo.PosSynchronizationOutboxMessages WHERE BusinessId IN(@SharedOne,@SharedTwo,@Independent,@Future);
                DELETE dbo.UserRoles WHERE BusinessId IN(@SharedOne,@SharedTwo,@Independent,@Future);
                DELETE dbo.Businesses WHERE BusinessId IN(@SharedOne,@SharedTwo,@Independent,@Future);
                DELETE dbo.Businesses WHERE BusinessId=@ForeignBusiness;
                DELETE dbo.Tenants WHERE TenantId=@ForeignTenant;
                """, connection);
            cleanup.Parameters.AddWithValue("@ChannelId", (object?)channelId ?? Guid.Empty);
            cleanup.Parameters.AddWithValue("@SharedOne", sharedOne);
            cleanup.Parameters.AddWithValue("@SharedTwo", sharedTwo);
            cleanup.Parameters.AddWithValue("@Independent", independent);
            cleanup.Parameters.AddWithValue("@Future", future);
            cleanup.Parameters.AddWithValue("@ForeignBusiness", foreignBusiness);
            cleanup.Parameters.AddWithValue("@ForeignTenant", foreignTenant);
            await cleanup.ExecuteNonQueryAsync();
        }
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
