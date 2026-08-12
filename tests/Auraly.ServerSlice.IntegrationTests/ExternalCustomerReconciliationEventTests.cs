using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ExternalCustomerReconciliationEventTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Integration_signal_reconciles_once_records_receipt_and_rejects_message_reuse()
    {
        var integrationId = await CreateIntegrationAsync("Automatic external customer");
        var externalId = await CreateExternalAsync(
            integrationId,
            $"account-{Guid.NewGuid():N}",
            $"customer-{Guid.NewGuid():N}",
            "Cliente autom�tico",
            "3005550901");
        var signal = new ExternalCustomerReconciliationSignal(
            Guid.NewGuid(),
            externalId,
            fixture.BusinessId,
            DateTimeOffset.UtcNow);

        var first = await ProcessAsync(signal);
        var replay = await ProcessAsync(signal);

        Assert.Equal(ExternalCustomerReconciliationStatuses.Linked, first.Status);
        Assert.False(first.IdempotentReplay);
        Assert.Equal(ExternalCustomerReconciliationStatuses.Linked, replay.Status);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.ExternalCustomerReconciliationReceipts
            WHERE MessageId=@MessageId AND ExternalCommerceCustomerId=@ExternalId
              AND BusinessId=@BusinessId AND ResultStatus=N'Linked';
            """,
            new SqlParameter("@MessageId", signal.MessageId),
            new SqlParameter("@ExternalId", externalId),
            new SqlParameter("@BusinessId", fixture.BusinessId)));
        Assert.Equal("Integration", await ScalarAsync<string>("""
            SELECT ReconciliationOrigin FROM dbo.ExternalCommerceCustomers
            WHERE ExternalCommerceCustomerId=@ExternalId;
            """, new SqlParameter("@ExternalId", externalId)));
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.ExternalCommerceCustomers
            WHERE ExternalCommerceCustomerId=@ExternalId AND ReconciledBy IS NOT NULL;
            """, new SqlParameter("@ExternalId", externalId)));
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Parties p
            JOIN dbo.ExternalCommerceCustomers e ON e.PartyId=p.PartyId
            WHERE e.ExternalCommerceCustomerId=@ExternalId;
            """, new SqlParameter("@ExternalId", externalId)));
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Customers c
            JOIN dbo.ExternalCommerceCustomers e ON e.CustomerId=c.CustomerId
            WHERE e.ExternalCommerceCustomerId=@ExternalId;
            """, new SqlParameter("@ExternalId", externalId)));

        var otherExternalId = await CreateExternalAsync(
            integrationId,
            $"account-{Guid.NewGuid():N}",
            $"customer-{Guid.NewGuid():N}",
            "Otro cliente",
            "3005550902");
        var reusedMessage = signal with
        {
            ExternalCommerceCustomerId = otherExternalId
        };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ProcessAsync(reusedMessage));
        Assert.Equal("Pending", await ScalarAsync<string>("""
            SELECT ReconciliationStatus FROM dbo.ExternalCommerceCustomers
            WHERE ExternalCommerceCustomerId=@ExternalId;
            """, new SqlParameter("@ExternalId", otherExternalId)));
    }

    [Fact]
    public async Task Concurrent_delivery_creates_one_party_customer_receipt_and_notification()
    {
        var integrationId = await CreateIntegrationAsync("Concurrent automatic customer");
        var externalId = await CreateExternalAsync(
            integrationId,
            $"account-{Guid.NewGuid():N}",
            $"customer-{Guid.NewGuid():N}",
            "Cliente concurrente",
            "3005550910");
        var signal = new ExternalCustomerReconciliationSignal(
            Guid.NewGuid(),
            externalId,
            fixture.BusinessId,
            DateTimeOffset.UtcNow);
        var notificationsBefore = await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';
            """, new SqlParameter("@BusinessId", fixture.BusinessId));

        var results = await Task.WhenAll(ProcessAsync(signal), ProcessAsync(signal));

        Assert.All(results, result =>
            Assert.Equal(ExternalCustomerReconciliationStatuses.Linked, result.Status));
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.ExternalCustomerReconciliationReceipts
            WHERE MessageId=@MessageId;
            """, new SqlParameter("@MessageId", signal.MessageId)));
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(DISTINCT p.PartyId)
            FROM dbo.Parties p
            JOIN dbo.ExternalCommerceCustomers e ON e.PartyId=p.PartyId
            WHERE e.ExternalCommerceCustomerId=@ExternalId;
            """, new SqlParameter("@ExternalId", externalId)));
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(DISTINCT c.CustomerId)
            FROM dbo.Customers c
            JOIN dbo.ExternalCommerceCustomers e ON e.CustomerId=c.CustomerId
            WHERE e.ExternalCommerceCustomerId=@ExternalId;
            """, new SqlParameter("@ExternalId", externalId)));
        Assert.Equal(notificationsBefore + 1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';
            """, new SqlParameter("@BusinessId", fixture.BusinessId)));
    }

    private async Task<ExternalCustomerReconciliationSignalResult> ProcessAsync(
        ExternalCustomerReconciliationSignal signal)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ExternalCustomerReconciliationSystemService>()
            .ProcessAsync(signal, CancellationToken.None);
    }

    private async Task<Guid> CreateIntegrationAsync(string name)
    {
        var id = Guid.NewGuid();
        var discriminator = id.GetHashCode() & int.MaxValue;
        await ExecuteAsync("""
            INSERT dbo.IntegrationConnections
              (IntegrationConnectionId,BusinessId,ConnectionType,Provider,Capability,Name,
               SettingsJson,IsEnabled,CreatedAt)
            VALUES(@Id,@BusinessId,0,@Provider,@Capability,@Name,N'{}',1,SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@Provider", discriminator),
            new SqlParameter("@Capability", discriminator),
            new SqlParameter("@Name", name));
        return id;
    }

    private async Task<Guid> CreateExternalAsync(
        Guid integrationId,
        string externalAccountId,
        string externalCustomerId,
        string name,
        string phone)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.ExternalCommerceCustomers
              (ExternalCommerceCustomerId,BusinessId,IntegrationConnectionId,ExternalAccountId,
               ExternalCustomerId,Name,PhoneNormalized,Phone,IsActive,LastSyncedAt,CreatedAt)
            VALUES
              (@Id,@BusinessId,@IntegrationId,@AccountId,@CustomerId,@Name,@Phone,@Phone,
               1,SYSUTCDATETIME(),SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@IntegrationId", integrationId),
            new SqlParameter("@AccountId", externalAccountId),
            new SqlParameter("@CustomerId", externalCustomerId),
            new SqlParameter("@Name", name),
            new SqlParameter("@Phone", phone));
        return id;
    }

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        return value is T typed ? typed : (T)Convert.ChangeType(value!, typeof(T));
    }
}
