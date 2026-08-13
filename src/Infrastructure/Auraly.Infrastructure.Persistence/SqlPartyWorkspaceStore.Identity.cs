using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPartyWorkspaceStore
{
    public Task<PartyWorkspaceDetail?> FindIdentityAsync(
        PartyActorIdentity actor,
        Guid countryId,
        string identificationType,
        string normalizedIdentification,
        CancellationToken ct) =>
        LoadDetailAsync(
            actor,
            """
            p.IdentificationTypeCode=@IdentificationType
            AND p.NormalizedIdentification=@NormalizedIdentification
            AND EXISTS(
              SELECT 1
              FROM dbo.Countries requested
              JOIN dbo.Countries existing
                ON UPPER(LTRIM(RTRIM(existing.Name)))=UPPER(LTRIM(RTRIM(requested.Name)))
              WHERE requested.CountryId=@CountryId
                AND existing.CountryId=p.IdentificationCountryId)
            """,
            [
                P("@CountryId", countryId),
                P("@IdentificationType", identificationType),
                P("@NormalizedIdentification", normalizedIdentification)
            ],
            requireBusinessRole: false,
            ct);

    public Task<PartyWorkspaceDetail?> GetDetailAsync(
        PartyActorIdentity actor,
        Guid partyId,
        CancellationToken ct) =>
        LoadDetailAsync(
            actor,
            "p.PartyId=@PartyId",
            [P("@PartyId", partyId)],
            requireBusinessRole: true,
            ct);

    private async Task<PartyWorkspaceDetail?> LoadDetailAsync(
        PartyActorIdentity actor,
        string identityPredicate,
        SqlParameter[] identityParameters,
        bool requireBusinessRole,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP(1)
              p.PartyId,p.PartyType,p.IdentificationCountryId,
              p.IdentificationTypeCode,p.Identification,p.VerificationDigit,
              COALESCE(p.DisplayName,p.LegalName,N'Sin nombre'),
              p.LegalName,p.FirstName,p.LastName,
              email.Value,phone.Value,
              site.PartySiteId,site.Code,site.Name,site.CountryId,
              site.AdministrativeDivisionId,site.CityId,site.AddressLine,
              site.Neighborhood,site.PostalCode,site.Email,site.Phone,site.IsPrimary,
              customer.CustomerId,customer.IsActive,pricing.PriceListId,pricing.PriceChannelId,
              supplier.SupplierId,supplier.IsActive,
              seller.SellerId,seller.Code,seller.DefaultCommissionPercent,
              seller.CommissionBasis,seller.CommissionTrigger,seller.IsActive,
              carrier.CarrierId,carrier.Code,carrier.TransportationMode,carrier.IsActive,
              employee.EmployeeId,employee.IsActive,
              appUser.UserId,appUser.Username,appUser.Email,appUser.IsActive,
              p.RowVersion
            FROM dbo.Parties p
            OUTER APPLY(
              SELECT TOP(1) pc.Value
              FROM dbo.PartyContacts pc
              WHERE pc.PartyId=p.PartyId AND pc.ContactType=N'Email' AND pc.IsActive=1
              ORDER BY pc.IsPrimary DESC,pc.CreatedAt
            ) email
            OUTER APPLY(
              SELECT TOP(1) pc.Value
              FROM dbo.PartyContacts pc
              WHERE pc.PartyId=p.PartyId AND pc.ContactType=N'Phone' AND pc.IsActive=1
              ORDER BY pc.IsPrimary DESC,pc.CreatedAt
            ) phone
            OUTER APPLY(
              SELECT TOP(1)
                ps.PartySiteId,ps.Code,ps.Name,ps.CountryId,
                ps.AdministrativeDivisionId,ps.CityId,ps.AddressLine,
                ps.Neighborhood,ps.PostalCode,ps.Email,ps.Phone,ps.IsPrimary
              FROM dbo.PartySites ps
              WHERE ps.PartyId=p.PartyId AND ps.IsActive=1
              ORDER BY ps.IsPrimary DESC,ps.CreatedAt
            ) site
            LEFT JOIN dbo.Customers customer
              ON customer.PartyId=p.PartyId AND customer.BusinessId=@BusinessId
            LEFT JOIN dbo.CustomerPricingSettings pricing
              ON pricing.CustomerId=customer.CustomerId
            LEFT JOIN dbo.Suppliers supplier
              ON supplier.PartyId=p.PartyId AND supplier.BusinessId=@BusinessId
            LEFT JOIN dbo.CommerceSellers seller
              ON seller.PartyId=p.PartyId AND seller.BusinessId=@BusinessId
            LEFT JOIN dbo.Carriers carrier
              ON carrier.PartyId=p.PartyId AND carrier.BusinessId=@BusinessId
            LEFT JOIN dbo.Employees employee
              ON employee.PartyId=p.PartyId AND employee.BusinessId=@BusinessId
            LEFT JOIN dbo.AppUsers appUser
              ON appUser.PartyId=p.PartyId AND appUser.TenantId=@TenantId
            WHERE p.TenantId=@TenantId
              AND {identityPredicate}
              AND (
                @RequireBusinessRole=0 OR
                customer.CustomerId IS NOT NULL OR supplier.SupplierId IS NOT NULL OR
                seller.SellerId IS NOT NULL OR carrier.CarrierId IS NOT NULL OR
                employee.EmployeeId IS NOT NULL OR appUser.UserId IS NOT NULL)
            ORDER BY p.CreatedAt,p.PartyId;
            """;
        command.Parameters.AddRange([
            P("@TenantId", actor.TenantId),
            P("@BusinessId", actor.BusinessId),
            P("@RequireBusinessRole", requireBusinessRole)
        ]);
        command.Parameters.AddRange(identityParameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var customer = reader.IsDBNull(24)
            ? null
            : new CustomerRoleDetail(
                reader.GetGuid(24),
                G(reader, 26),
                G(reader, 27),
                reader.GetBoolean(25));
        var supplier = reader.IsDBNull(28)
            ? null
            : new SupplierRoleDetail(reader.GetGuid(28), reader.GetBoolean(29));
        var seller = reader.IsDBNull(30)
            ? null
            : new SellerRoleDetail(
                reader.GetGuid(30),
                reader.GetString(31),
                D(reader, 32),
                reader.GetString(33),
                reader.GetString(34),
                reader.GetBoolean(35));
        var carrier = reader.IsDBNull(36)
            ? null
            : new CarrierRoleDetail(
                reader.GetGuid(36),
                reader.GetString(37),
                reader.GetString(38),
                reader.GetBoolean(39));
        var employee = reader.IsDBNull(40)
            ? null
            : new EmployeeRoleDetail(reader.GetGuid(40), reader.GetBoolean(41));
        var user = reader.IsDBNull(42)
            ? null
            : new UserRoleDetail(
                reader.GetGuid(42),
                reader.GetString(43),
                reader.GetString(44),
                reader.GetBoolean(45));
        var roles = new List<string>(6);
        if (customer is not null) roles.Add("Customer");
        if (supplier is not null) roles.Add("Supplier");
        if (seller is not null) roles.Add("Seller");
        if (carrier is not null) roles.Add("Carrier");
        if (employee is not null) roles.Add("Employee");
        if (user is not null) roles.Add("User");

        var site = reader.IsDBNull(12)
            ? null
            : new PartyWorkspaceSiteDetail(
                reader.GetGuid(12),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetGuid(15),
                reader.GetGuid(16),
                reader.GetGuid(17),
                reader.GetString(18),
                S(reader, 19),
                S(reader, 20),
                S(reader, 21),
                S(reader, 22),
                reader.GetBoolean(23));

        return new PartyWorkspaceDetail(
            reader.GetGuid(0),
            reader.GetString(1),
            G(reader, 2),
            S(reader, 3),
            S(reader, 4),
            S(reader, 5),
            reader.GetString(6),
            S(reader, 7),
            S(reader, 8),
            S(reader, 9),
            S(reader, 10),
            S(reader, 11),
            roles,
            site,
            customer,
            supplier,
            seller,
            carrier,
            employee,
            user,
            Convert.ToBase64String((byte[])reader[46]));
    }

    private static Guid? G(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static decimal? D(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}