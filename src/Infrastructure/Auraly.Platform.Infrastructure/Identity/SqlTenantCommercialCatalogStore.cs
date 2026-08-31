using System.Text.Json;
using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Auraly.Platform.Infrastructure.Identity;

public sealed class SqlTenantCommercialCatalogStore(ApplicationDbContext db)
    : ITenantCommercialCatalogStore
{
    public async Task<TenantCommercialCatalogDto> GetAsync(CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        var plans = new List<TenantCommercialPlanDto>();
        await using (var command = new SqlCommand("""
            SELECT planValue.TenantCommercialPlanId,serviceValue.Code,serviceValue.Name,
                   serviceValue.UnitPrice,tax.Rate,planValue.AnnualDiscountRate,
                   IncludedFullUsers,IncludedSellerUsers,IncludedPosDevices,
                   IncludedDianDocuments,IncludedPayrollEmployees,IsRecommended,IsCustom,FeaturesJson
            FROM billing.TenantCommercialPlans planValue
            JOIN billing.BillableServices serviceValue ON serviceValue.BillableServiceId=planValue.BillableServiceId
            JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=serviceValue.SalesTaxProfileId
            WHERE planValue.IsActive=1 AND serviceValue.IsActive=1 AND tax.IsActive=1
            ORDER BY planValue.IsCustom,serviceValue.UnitPrice,serviceValue.Name;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                plans.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),
                    reader.GetDecimal(4),reader.GetDecimal(5),reader.GetInt32(6),reader.GetInt32(7),
                    reader.GetInt32(8),reader.GetInt32(9),reader.GetInt32(10),reader.GetBoolean(11),reader.GetBoolean(12),
                    JsonSerializer.Deserialize<string[]>(reader.GetString(13)) ?? []));
        var addOns = new List<TenantCommercialAddOnDto>();
        await using (var command = new SqlCommand("""
            SELECT addon.TenantCommercialAddOnId,serviceValue.Code,serviceValue.Name,
                   serviceValue.UnitLabel,serviceValue.UnitSize,serviceValue.UnitPrice,tax.Rate
            FROM billing.TenantCommercialAddOns addon
            JOIN billing.BillableServices serviceValue ON serviceValue.BillableServiceId=addon.BillableServiceId
            JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=serviceValue.SalesTaxProfileId
            WHERE addon.IsActive=1 AND serviceValue.IsActive=1 AND tax.IsActive=1
            ORDER BY serviceValue.Name;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                addOns.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),
                    reader.GetString(3),reader.GetInt32(4),reader.GetDecimal(5),reader.GetDecimal(6)));
        return new(plans, addOns);
    }

    public Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetCountriesAsync(CancellationToken cancellationToken) =>
        GeographyAsync("SELECT CountryId,Code,Name FROM dbo.Countries WHERE IsActive=1 ORDER BY Name;", null, cancellationToken);

    public Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetDivisionsAsync(Guid countryId, CancellationToken cancellationToken) =>
        GeographyAsync("SELECT AdministrativeDivisionId,Code,Name FROM dbo.AdministrativeDivisions WHERE CountryId=@ParentId AND IsActive=1 ORDER BY Name;", countryId, cancellationToken);

    public Task<IReadOnlyList<TenantProvisioningGeographyDto>> GetCitiesAsync(Guid divisionId, CancellationToken cancellationToken) =>
        GeographyAsync("SELECT CityId,Code,Name FROM dbo.Cities WHERE AdministrativeDivisionId=@ParentId AND IsActive=1 ORDER BY Name;", divisionId, cancellationToken);

    private async Task<IReadOnlyList<TenantProvisioningGeographyDto>> GeographyAsync(
        string sql, Guid? parentId, CancellationToken cancellationToken)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        if (parentId.HasValue) command.Parameters.AddWithValue("@ParentId", parentId.Value);
        var values = new List<TenantProvisioningGeographyDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return values;
    }
}
