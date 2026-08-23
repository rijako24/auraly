using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Parties;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ExternalCustomerReconciliationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Explicit_sync_reconciles_immediately_and_bot_reads_canonical_customer()
    {
        var integrationId = await CreateIntegrationAsync("Synchronous customer reconciliation");
        var externalId = await CreateExternalAsync(
            integrationId,
            "account-sync",
            "customer-sync",
            "Cliente canónico",
            "+57 300 555 0198",
            "573005550198");

        using var scope = fixture.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<
            IExternalCustomerReconciliationRunner>();
        Assert.Equal(1, await runner.ReconcilePendingAsync(fixture.BusinessId));

        var lookup = scope.ServiceProvider.GetRequiredService<
            ICanonicalCommerceCustomerLookup>();
        var customer = await lookup.FindAsync(
            fixture.BusinessId,
            integrationId,
            CommerceProvider.Mantis,
            "+57 300 555 0198");

        Assert.NotNull(customer);
        Assert.Equal("account-sync", customer.ExternalAccountId);
        Assert.Equal("customer-sync", customer.ExternalCustomerId);
        Assert.Equal("Cliente canónico", customer.Name);
        Assert.Equal("Linked", await ScalarAsync<string>(
            "SELECT ReconciliationStatus FROM dbo.ExternalCommerceCustomers WHERE ExternalCommerceCustomerId=@Id;",
            new SqlParameter("@Id", externalId)));
    }

    [Fact]
    public async Task Pending_external_customer_is_linked_once_and_reuses_the_party_by_phone()
    {
        var integrationId = await CreateIntegrationAsync("External customer reconciliation");
        var firstExternalId = await CreateExternalAsync(
            integrationId,
            "account-1",
            "customer-1",
            "Cliente importado",
            "300 555 0101",
            "3005550101");
        var notificationsBefore = await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';
            """, new SqlParameter("@BusinessId", fixture.BusinessId));

        using var admin = fixture.CreateAdminClient(
            ExternalCustomerReconciliationPermissionCodes.Read,
            ExternalCustomerReconciliationPermissionCodes.Reconcile);
        var requests = new[]
        {
            admin.PostAsync($"/api/commerce/v1/external-customers/{firstExternalId:D}/reconcile", null),
            admin.PostAsync($"/api/commerce/v1/external-customers/{firstExternalId:D}/reconcile", null)
        };
        var responses = await Task.WhenAll(requests);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var results = await Task.WhenAll(responses.Select(async response =>
            await response.Content.ReadFromJsonAsync<ExternalCustomerReconciliationResult>()
            ?? throw new InvalidOperationException("Reconciliation returned no body.")));
        Assert.Single(results.Where(result => !result.IdempotentReplay));
        Assert.Single(results.Where(result => result.IdempotentReplay));
        var linked = results[0];
        Assert.Equal(ExternalCustomerReconciliationStatuses.Linked, linked.Status);
        Assert.NotNull(linked.PartyId);
        Assert.NotNull(linked.CustomerId);

        Assert.Equal("Incomplete", await ScalarAsync<string>(
            "SELECT CompletionStatus FROM dbo.Parties WHERE PartyId=@PartyId;",
            new SqlParameter("@PartyId", linked.PartyId)));
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Parties
            WHERE PartyId=@PartyId AND NormalizedIdentification IS NOT NULL;
            """, new SqlParameter("@PartyId", linked.PartyId)));
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.PartyContacts
            WHERE PartyId=@PartyId AND ContactType=N'Phone' AND NormalizedValue=N'3005550101';
            """, new SqlParameter("@PartyId", linked.PartyId)));

        var secondExternalId = await CreateExternalAsync(
            integrationId,
            "account-2",
            "customer-2",
            "Cliente importado actualizado",
            "3005550101",
            "3005550101");
        using var secondResponse = await admin.PostAsync(
            $"/api/commerce/v1/external-customers/{secondExternalId:D}/reconcile",
            null);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<ExternalCustomerReconciliationResult>();
        Assert.NotNull(second);
        Assert.Equal(linked.PartyId, second.PartyId);
        Assert.Equal(linked.CustomerId, second.CustomerId);
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Customers
            WHERE BusinessId=@BusinessId AND PartyId=@PartyId;
            """,
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@PartyId", linked.PartyId)));

        var page = await admin.GetFromJsonAsync<ExternalCustomerReconciliationPage>(
            "/api/commerce/v1/external-customers?page=1&pageSize=10&status=Linked&search=3005550101");
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, item => Assert.Equal(
            ExternalCustomerReconciliationStatuses.Linked,
            item.Status));
        Assert.Equal(notificationsBefore + 2, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';
            """, new SqlParameter("@BusinessId", fixture.BusinessId)));

        var thirdExternalId = await CreateExternalAsync(
            integrationId,
            "account-3",
            "customer-3",
            "Cliente por lote",
            "3005550103",
            "3005550103");
        using var bulkResponse = await admin.PostAsJsonAsync(
            "/api/commerce/v1/external-customers/reconcile-pending",
            new ReconcilePendingExternalCustomersRequest(100));
        Assert.Equal(HttpStatusCode.OK, bulkResponse.StatusCode);
        var bulk = await bulkResponse.Content.ReadFromJsonAsync<ReconcilePendingExternalCustomersResult>();
        Assert.NotNull(bulk);
        Assert.True(bulk.Requested >= 1);
        Assert.True(bulk.Linked >= 1);
        Assert.Equal("Linked", await ScalarAsync<string>(
            "SELECT ReconciliationStatus FROM dbo.ExternalCommerceCustomers WHERE ExternalCommerceCustomerId=@Id;",
            new SqlParameter("@Id", thirdExternalId)));

        using var deniedRead = fixture.CreateAdminClient(
            ExternalCustomerReconciliationPermissionCodes.Reconcile);
        using var deniedReadResponse = await deniedRead.GetAsync(
            "/api/commerce/v1/external-customers?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Forbidden, deniedReadResponse.StatusCode);

        using var denied = fixture.CreateAdminClient(
            ExternalCustomerReconciliationPermissionCodes.Read);
        using var deniedResponse = await denied.PostAsync(
            $"/api/commerce/v1/external-customers/{secondExternalId:D}/reconcile",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        using var outsideResponse = await admin.PostAsync(
            $"/api/commerce/v1/external-customers/{Guid.NewGuid():D}/reconcile",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, outsideResponse.StatusCode);
    }

    [Fact]
    public async Task Ambiguous_phone_is_conflict_and_can_be_reconciled_after_review()
    {
        var integrationId = await CreateIntegrationAsync("Conflict reconciliation");
        var phone = "3005550299";
        var firstParty = Guid.NewGuid();
        var secondParty = Guid.NewGuid();
        var firstContact = Guid.NewGuid();
        var secondContact = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.Parties
              (PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES
              (@FirstParty,@TenantId,N'NaturalPerson',N'Persona uno',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET()),
              (@SecondParty,@TenantId,N'NaturalPerson',N'Persona dos',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.PartyContacts
              (PartyContactId,PartyId,ContactType,Value,NormalizedValue,IsPrimary,IsActive,CreatedAt)
            VALUES
              (@FirstContact,@FirstParty,N'Phone',@Phone,@Phone,1,1,SYSDATETIMEOFFSET()),
              (@SecondContact,@SecondParty,N'Phone',@Phone,@Phone,1,1,SYSDATETIMEOFFSET());
            """,
            new SqlParameter("@FirstParty", firstParty),
            new SqlParameter("@SecondParty", secondParty),
            new SqlParameter("@FirstContact", firstContact),
            new SqlParameter("@SecondContact", secondContact),
            new SqlParameter("@TenantId", fixture.TenantId),
            new SqlParameter("@UserId", fixture.UserId),
            new SqlParameter("@Phone", phone));
        var externalId = await CreateExternalAsync(
            integrationId,
            "ambiguous-account",
            "ambiguous-customer",
            "Cliente ambiguo",
            phone,
            phone);
        using var admin = fixture.CreateAdminClient(
            ExternalCustomerReconciliationPermissionCodes.Read,
            ExternalCustomerReconciliationPermissionCodes.Reconcile);

        using var conflictResponse = await admin.PostAsync(
            $"/api/commerce/v1/external-customers/{externalId:D}/reconcile",
            null);
        Assert.Equal(HttpStatusCode.OK, conflictResponse.StatusCode);
        var conflict = await conflictResponse.Content.ReadFromJsonAsync<ExternalCustomerReconciliationResult>();
        Assert.NotNull(conflict);
        Assert.Equal(ExternalCustomerReconciliationStatuses.Conflict, conflict.Status);
        Assert.Null(conflict.PartyId);
        Assert.Contains("more than one Party", conflict.Error);
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Customers WHERE BusinessId=@BusinessId AND PartyId IN (@First,@Second);",
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@First", firstParty),
            new SqlParameter("@Second", secondParty)));

        await ExecuteAsync(
            "UPDATE dbo.PartyContacts SET IsActive=0,IsPrimary=0 WHERE PartyContactId=@ContactId;",
            new SqlParameter("@ContactId", secondContact));
        using var retryResponse = await admin.PostAsync(
            $"/api/commerce/v1/external-customers/{externalId:D}/reconcile",
            null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retried = await retryResponse.Content.ReadFromJsonAsync<ExternalCustomerReconciliationResult>();
        Assert.NotNull(retried);
        Assert.Equal(ExternalCustomerReconciliationStatuses.Linked, retried.Status);
        Assert.Equal(firstParty, retried.PartyId);
        Assert.NotNull(retried.CustomerId);
        Assert.Null(retried.Error);
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
        string phone,
        string normalizedPhone)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT dbo.ExternalCommerceCustomers
              (ExternalCommerceCustomerId,BusinessId,IntegrationConnectionId,ExternalAccountId,
               ExternalCustomerId,Name,PhoneNormalized,Phone,IsActive,LastSyncedAt,CreatedAt)
            VALUES
              (@Id,@BusinessId,@IntegrationId,@AccountId,@CustomerId,@Name,@NormalizedPhone,@Phone,
               1,SYSUTCDATETIME(),SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", id),
            new SqlParameter("@BusinessId", fixture.BusinessId),
            new SqlParameter("@IntegrationId", integrationId),
            new SqlParameter("@AccountId", externalAccountId),
            new SqlParameter("@CustomerId", externalCustomerId),
            new SqlParameter("@Name", name),
            new SqlParameter("@NormalizedPhone", normalizedPhone),
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
