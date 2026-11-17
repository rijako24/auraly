using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayrollVerticalSliceTests
{
    private async Task<PayrollEmploymentConfiguration> CreateProfileFixtureAsync(Guid ruleSetId)
    {
        var health=Guid.NewGuid();var pension=Guid.NewGuid();var severance=Guid.NewGuid();
        var risk=Guid.NewGuid();var compensation=Guid.NewGuid();var sena=Guid.NewGuid();var icbf=Guid.NewGuid();
        var mappings=new List<PayrollProfileMapping>();
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command=new SqlCommand("""
                INSERT dbo.Parties(PartyId,TenantId,PartyType,IdentificationCountryId,IdentificationTypeCode,
                  Identification,NormalizedIdentification,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                SELECT source.Id,@Tenant,N'Organization',country.CountryId,N'NIT',CONVERT(nvarchar(36),source.Id),
                  CONVERT(nvarchar(36),source.Id),source.Name,source.Name,N'Complete',1,@Actor,SYSUTCDATETIME()
                FROM (VALUES(@Health,N'EPS de prueba'),(@Pension,N'Pensión de prueba'),(@Severance,N'Cesantías de prueba'),
                  (@Risk,N'ARL de prueba'),(@Compensation,N'Caja de prueba'),(@Sena,N'SENA de prueba'),(@Icbf,N'ICBF de prueba')) source(Id,Name)
                CROSS JOIN dbo.Countries country WHERE country.Code=N'CO';
                SELECT c.ConceptId,role.Code FROM payroll.Concepts c JOIN payroll.CatalogOptions role ON role.OptionId=c.SystemRoleOptionId
                WHERE c.TenantId=@Tenant AND role.Code IN(N'Sena',N'Icbf');
                """,connection);
            command.Parameters.AddWithValue("@Tenant",fixture.TenantId);command.Parameters.AddWithValue("@Actor",fixture.UserId);
            command.Parameters.AddWithValue("@Health",health);command.Parameters.AddWithValue("@Pension",pension);
            command.Parameters.AddWithValue("@Severance",severance);command.Parameters.AddWithValue("@Risk",risk);
            command.Parameters.AddWithValue("@Compensation",compensation);command.Parameters.AddWithValue("@Sena",sena);command.Parameters.AddWithValue("@Icbf",icbf);
            await using var reader=await command.ExecuteReaderAsync();
            while(await reader.ReadAsync())mappings.Add(new(Guid.NewGuid(),reader.GetGuid(0),null,null,new(2026,1,1),null)
              {BeneficiaryPartyId=reader.GetString(1)=="Sena"?sena:icbf});
        }
        using var scope=fixture.CreateScope();var service=scope.ServiceProvider.GetRequiredService<PayrollService>();
        var profile=await service.SaveProfileAsync(new(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string>{PayrollPermissionCodes.Configure}),new(Guid.NewGuid(),Guid.NewGuid().ToString("N"),
                "Parámetro de contrato de prueba",ruleSetId,true,null,mappings));
        return new(profile.ProfileId,health,pension,severance,risk,compensation,null);
    }
}
