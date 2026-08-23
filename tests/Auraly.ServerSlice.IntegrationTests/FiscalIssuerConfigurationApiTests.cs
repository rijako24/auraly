using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Fiscal")]
public sealed class FiscalIssuerConfigurationApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Onboarding_is_scoped_and_legacy_issuer_endpoint_is_not_exposed()
    {
        var businessId = Guid.NewGuid();
        var legalScope = await InsertBusinessAsync(businessId);
        try
        {
            using var client = fixture.CreateUserClient(
                fixture.UserId,
                FiscalPermissionCodes.ConfigurationRead,
                FiscalPermissionCodes.ConfigurationManage);
            using var readResponse = await client.GetAsync(
                $"/api/commerce/v1/fiscal/configuration/onboarding?businessId={businessId:D}");
            var readBody = await readResponse.Content.ReadAsStringAsync();
            Assert.True(
                readResponse.StatusCode == HttpStatusCode.OK,
                $"Expected OK but received {readResponse.StatusCode}: {readBody}");
            var read = await readResponse.Content.ReadFromJsonAsync<FiscalOnboardingConfiguration>();
            Assert.NotNull(read);
            Assert.Equal(businessId, read.BusinessId);
            Assert.Equal(FiscalOnboardingStages.NotConfigured, read.Stage);
            Assert.Contains("Certificado", read.MissingRequirements);

            using var legacyResponse = await client.GetAsync(
                $"/api/commerce/v1/fiscal/configuration/issuer?businessId={businessId:D}");
            Assert.Equal(HttpStatusCode.NotFound, legacyResponse.StatusCode);

            using var denied = fixture.CreateUserClient(fixture.UserId);
            using var deniedResponse = await denied.GetAsync(
                $"/api/commerce/v1/fiscal/configuration/onboarding?businessId={businessId:D}");
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        }
        finally
        {
            await DeleteBusinessAsync(businessId, legalScope);
        }
    }

    private async Task<LegalProfileScope> InsertBusinessAsync(Guid businessId)
    {
        var scope = new LegalProfileScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            INSERT dbo.Countries(CountryId,Code,Name,IsActive,CreatedAt)
            VALUES(@CountryId,@CountryCode,N'País fiscal',1,SYSDATETIMEOFFSET());
            INSERT dbo.AdministrativeDivisions(
                AdministrativeDivisionId,CountryId,Code,Name,DivisionType,IsActive,CreatedAt)
            VALUES(@DivisionId,@CountryId,N'11',N'Departamento fiscal',N'Department',1,SYSDATETIMEOFFSET());
            INSERT dbo.Cities(CityId,AdministrativeDivisionId,Code,Name,IsActive,CreatedAt)
            VALUES(@CityId,@DivisionId,N'11001',N'Bogotá',1,SYSDATETIMEOFFSET());
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES(@BusinessId,@TenantId,N'DIAN E2E',N'Isolated test',N'Bogota',N'3000000000',N'dian-e2e@auraly.test',N'https://auraly.test',1,SYSUTCDATETIME());
            INSERT dbo.TenantLegalProfiles(
                TenantId,LegalName,TradeName,Nit,NormalizedNit,VerificationDigit,
                CountryId,AdministrativeDivisionId,CityId,Address,Phone,Email,
                TaxResponsibilities,PrimaryBusinessId,CreatedAt)
            VALUES(@TenantId,N'DIAN E2E SAS',N'DIAN E2E',@Nit,@Nit,N'1',
                @CountryId,@DivisionId,@CityId,N'CL 1 2 3',N'3000000000',
                N'dian-e2e@auraly.test',N'R-99-PN',@BusinessId,SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@CountryId", scope.CountryId);
        command.Parameters.AddWithValue("@DivisionId", scope.DivisionId);
        command.Parameters.AddWithValue("@CityId", scope.CityId);
        command.Parameters.AddWithValue("@CountryCode", businessId.ToString("N")[..2]);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Nit", $"9{businessId:N}"[..16]);
        await command.ExecuteNonQueryAsync();
        return scope;
    }

    private async Task DeleteBusinessAsync(Guid businessId, LegalProfileScope scope)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            DELETE dbo.FiscalIssuerConfigurations WHERE BusinessId=@BusinessId;
            DELETE dbo.TenantLegalProfiles WHERE TenantId=@TenantId;
            DELETE dbo.Businesses WHERE BusinessId=@BusinessId;
            DELETE dbo.Cities WHERE CityId=@CityId;
            DELETE dbo.AdministrativeDivisions WHERE AdministrativeDivisionId=@DivisionId;
            DELETE dbo.Countries WHERE CountryId=@CountryId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@CountryId", scope.CountryId);
        command.Parameters.AddWithValue("@DivisionId", scope.DivisionId);
        command.Parameters.AddWithValue("@CityId", scope.CityId);
        await command.ExecuteNonQueryAsync();
    }

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

    private sealed record LegalProfileScope(Guid CountryId, Guid DivisionId, Guid CityId);
}
