using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlSaleUblPartyReader
{
    public static async Task<PosSaleUblPartyContract> ReadCustomerAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid businessId,
        Guid? customerId,
        PosSaleUblAddressContract fallback,
        CancellationToken cancellationToken)
    {
        if (customerId is null) return FinalConsumer(fallback);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.PartyType,p.Identification,p.VerificationDigit,
                   p.IdentificationTypeCode,
                   COALESCE(p.LegalName,p.DisplayName,
                     NULLIF(LTRIM(RTRIM(CONCAT(p.FirstName,N' ',p.LastName))),N'')),
                   COALESCE(p.DisplayName,p.LegalName),
                   country.Code,country.Name,division.Code,division.Name,
                   city.Code,city.Name,site.AddressLine,
                   email.Value,phone.Value
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            OUTER APPLY(
              SELECT TOP(1) value.* FROM dbo.PartySites value
              WHERE value.PartyId=p.PartyId AND value.IsActive=1
              ORDER BY value.IsPrimary DESC,value.CreatedAt,value.PartySiteId) site
            LEFT JOIN dbo.Countries country ON country.CountryId=site.CountryId
            LEFT JOIN dbo.AdministrativeDivisions division
              ON division.AdministrativeDivisionId=site.AdministrativeDivisionId
            LEFT JOIN dbo.Cities city ON city.CityId=site.CityId
            OUTER APPLY(
              SELECT TOP(1) value.Value FROM dbo.PartyContacts value
              WHERE value.PartyId=p.PartyId AND value.ContactType=N'Email'
                AND value.IsActive=1 ORDER BY value.IsPrimary DESC,value.CreatedAt) email
            OUTER APPLY(
              SELECT TOP(1) value.Value FROM dbo.PartyContacts value
              WHERE value.PartyId=p.PartyId AND value.ContactType=N'Phone'
                AND value.IsActive=1 ORDER BY value.IsPrimary DESC,value.CreatedAt) phone
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId
              AND c.IsActive=1 AND p.IsActive=1;
            """;
        command.Parameters.AddWithValue("@CustomerId", customerId.Value);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(1))
            return FinalConsumer(fallback);
        return new PosSaleUblPartyContract(
            reader.GetString(1),
            reader.IsDBNull(2) ? "0" : reader.GetString(2),
            reader.IsDBNull(3) ? "13" : reader.GetString(3),
            reader.GetString(0) == "Organization" ? "1" : "2",
            reader.IsDBNull(4) ? "Consumidor final" : reader.GetString(4),
            reader.IsDBNull(5)
                ? reader.IsDBNull(4) ? "Consumidor final" : reader.GetString(4)
                : reader.GetString(5),
            "R-99-PN",
            "01",
            "IVA",
            new PosSaleUblAddressContract(
                reader.IsDBNull(10) ? fallback.MunicipalityCode : reader.GetString(10),
                reader.IsDBNull(11) ? fallback.CityName : reader.GetString(11),
                reader.IsDBNull(9) ? fallback.DepartmentName : reader.GetString(9),
                reader.IsDBNull(8) ? fallback.DepartmentCode : reader.GetString(8),
                reader.IsDBNull(12) ? fallback.AddressLine : reader.GetString(12),
                reader.IsDBNull(6) ? fallback.CountryCode : reader.GetString(6),
                reader.IsDBNull(7) ? fallback.CountryName : reader.GetString(7)),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static PosSaleUblPartyContract FinalConsumer(
        PosSaleUblAddressContract fallback) =>
        new(
            "222222222222",
            "0",
            "13",
            "2",
            "Consumidor final",
            "Consumidor final",
            "R-99-PN",
            "ZZ",
            "No aplica",
            fallback);
}

public sealed class SqlPosCreditFiscalMaterialReader(SqlServerConnectionFactory connections)
{
    public async Task<PosCreditFiscalMaterial> ReadAsync(
        Guid tenantId,
        Guid deviceId,
        Guid businessId,
        Guid customerId,
        int environment,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(2)
              issuer.FiscalIssuerConfigurationId,issuer.SupplierTaxId,
              issuer.SupplierCheckDigit,issuer.IdentificationTypeCode,
              issuer.LegalName,issuer.TradeName,issuer.TaxLevelCode,
              issuer.TaxSchemeId,issuer.TaxSchemeName,issuer.AddressLine,
              issuer.CityCode,issuer.CityName,issuer.DepartmentCode,
              issuer.DepartmentName,issuer.CountryCode,issuer.CountryName,
              issuer.SoftwareIdentificationCode
            FROM dbo.FiscalIssuerConfigurations issuer
            JOIN dbo.Businesses business ON business.BusinessId=issuer.BusinessId
            WHERE issuer.BusinessId=@BusinessId AND business.TenantId=@TenantId
              AND issuer.Environment=@Environment AND issuer.IsActive=1
              AND business.IsActive=1
              AND EXISTS(
                SELECT 1 FROM dbo.DocumentSeries series
                WHERE series.BusinessId=issuer.BusinessId
                  AND series.DeviceId=@DeviceId AND series.IsActive=1);
            """;
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@Environment", environment);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        var rows = new List<(Guid Id, string Software, PosSaleUblPartyContract Supplier)>(2);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var supplier = new PosSaleUblPartyContract(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    "1",
                    reader.GetString(4),
                    reader.IsDBNull(5) ? reader.GetString(4) : reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    new PosSaleUblAddressContract(
                        reader.GetString(10),
                        reader.GetString(11),
                        reader.GetString(13),
                        reader.GetString(12),
                        reader.GetString(9),
                        reader.GetString(14),
                        reader.GetString(15)));
                rows.Add((reader.GetGuid(0), reader.GetString(16), supplier));
            }
        }
        if (rows.Count != 1)
            throw new InvalidOperationException(
                "La sede no tiene una configuración fiscal única para la caja enrolada.");
        var material = rows[0];
        var customer = await SqlSaleUblPartyReader.ReadCustomerAsync(
            connection,
            transaction: null,
            businessId,
            customerId,
            material.Supplier.Address,
            cancellationToken);
        return new PosCreditFiscalMaterial(
            material.Id,
            material.Software,
            material.Supplier,
            customer);
    }
}
