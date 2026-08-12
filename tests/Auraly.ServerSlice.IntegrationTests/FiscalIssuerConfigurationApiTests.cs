using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class FiscalIssuerConfigurationApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Habilitation_connection_is_versioned_scoped_and_contains_only_secret_references()
    {
        var businessId = Guid.NewGuid();
        await InsertBusinessAsync(businessId);
        try
        {
            using var client = fixture.CreateUserClient(
                fixture.UserId,
                FiscalPermissionCodes.ConfigurationRead,
                FiscalPermissionCodes.ConfigurationManage);
            var request = ValidRequest();
            using var savedResponse = await client.PutAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration/issuer?businessId={businessId:D}",
                request);
            Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
            var saved = await savedResponse.Content.ReadFromJsonAsync<FiscalIssuerConnectionConfiguration>();
            Assert.NotNull(saved);
            Assert.True(saved.IsConfigured);
            Assert.True(saved.IsReadyForHabilitation);
            Assert.Empty(saved.MissingRequirements);
            Assert.Equal(request.TestSetId, saved.TestSetId);
            Assert.Equal("env://AURALY_E2E_SOFTWARE_PIN", saved.SoftwarePinSecretReference);

            using var readResponse = await client.GetAsync(
                $"/api/commerce/v1/fiscal/configuration/issuer?businessId={businessId:D}");
            Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
            var read = await readResponse.Content.ReadFromJsonAsync<FiscalIssuerConnectionConfiguration>();
            Assert.Equal(saved.FiscalIssuerConfigurationId, read?.FiscalIssuerConfigurationId);
            Assert.Equal(1, read?.Version);

            Assert.Equal(1, await ScalarAsync(
                "SELECT COUNT(*) FROM dbo.FiscalIssuerConfigurations WHERE BusinessId=@BusinessId AND IsActive=1",
                businessId));
            Assert.Equal(0, await ScalarAsync(
                "SELECT COUNT(*) FROM dbo.FiscalIssuerConfigurations WHERE BusinessId=@BusinessId AND SoftwarePinSecretReference NOT LIKE 'env://%'",
                businessId));

            using var denied = fixture.CreateUserClient(fixture.UserId);
            using var deniedResponse = await denied.GetAsync(
                $"/api/commerce/v1/fiscal/configuration/issuer?businessId={businessId:D}");
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        }
        finally
        {
            await ExecuteAsync(
                "DELETE FROM dbo.FiscalIssuerConfigurations WHERE BusinessId=@BusinessId; DELETE FROM dbo.Businesses WHERE BusinessId=@BusinessId;",
                businessId);
        }
    }

    [Fact]
    public async Task Habilitation_requires_test_set_and_official_environment_endpoint()
    {
        var service = new FiscalIssuerConnectionService(new NeverCalledStore());
        var user = new FiscalConfigurationUser(
            Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { FiscalPermissionCodes.ConfigurationManage });
        var invalid = ValidRequest() with
        {
            TestSetId = null,
            DianEndpoint = "https://vpfe.dian.gov.co/WcfDianCustomerServices.svc"
        };
        var exception = await Assert.ThrowsAsync<FiscalConfigurationValidationException>(
            () => service.SaveAsync(user, Guid.NewGuid(), invalid));
        Assert.Contains("TestSetId", exception.Message, StringComparison.Ordinal);
    }

    private static SaveFiscalIssuerConnectionConfiguration ValidRequest() => new(
        "9001234567", "7", "EMISOR DE HABILITACION", "EMISOR DE HABILITACION",
        "R-99-PN", "01", "IVA", "31", "CL 1 2 3", "11001", "Bogota",
        "11", "Bogota D.C.", null, Guid.NewGuid().ToString("D"),
        "env://AURALY_E2E_SOFTWARE_PIN", 2, Guid.NewGuid(),
        "WindowsCertificateStore", "CurrentUser/My",
        "0123456789ABCDEF0123456789ABCDEF01234567",
        "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc",
        "1.9", "Auraly.Tests", DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(1));

    private async Task InsertBusinessAsync(Guid businessId) => await ExecuteAsync(
        "INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt) " +
        "VALUES(@BusinessId,@TenantId,N'DIAN E2E',N'Isolated test',N'Bogota',N'3000000000',N'dian-e2e@auraly.test',N'https://auraly.test',1,SYSUTCDATETIME())",
        businessId, fixture.TenantId);

    private async Task<int> ScalarAsync(string sql, Guid businessId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql, Guid businessId, Guid? tenantId = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        if (tenantId is not null) command.Parameters.AddWithValue("@TenantId", tenantId.Value);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NeverCalledStore : IFiscalIssuerConnectionStore
    {
        public Task<FiscalIssuerConnectionConfiguration> GetAsync(
            Guid tenantId, Guid businessId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public Task<FiscalIssuerConnectionConfiguration> SaveAsync(
            Guid tenantId, Guid businessId, Guid userId,
            SaveFiscalIssuerConnectionConfiguration request,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
