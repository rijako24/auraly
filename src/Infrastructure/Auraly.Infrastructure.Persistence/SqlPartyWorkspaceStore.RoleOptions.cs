using Auraly.Application.Parties;
using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPartyWorkspaceStore
{
    public async Task<PartySiteRoleOptionPage> SiteRoleOptionsAsync(
        PartyActorIdentity actor, int page, PartyRoleOptionQuery query, CancellationToken ct)
    {
        var role = query.Role == "Customer" ? (Table: "dbo.Customers", Id: "CustomerId") :
            (Table: "dbo.Suppliers", Id: "SupplierId");
        var supplierPolicy = query.Role == "Supplier" ? "partyRole.PurchaseEvidencePolicy" : "CAST(NULL AS NVARCHAR(40))";
        var supplierDueDays = query.Role == "Supplier" ? "partyRole.DefaultPaymentDueDays" : "CAST(NULL AS INT)";
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT_BIG(1)
            FROM dbo.Parties party
            JOIN {role.Table} partyRole ON partyRole.PartyId=party.PartyId
              AND partyRole.TenantId=@TenantId AND partyRole.IsActive=1
            JOIN dbo.PartySites site ON site.PartyId=party.PartyId AND site.IsActive=1
            WHERE party.TenantId=@TenantId AND party.IsActive=1
              AND (@RoleId IS NULL OR partyRole.{role.Id}=@RoleId)
              AND (@PartySiteId IS NULL OR site.PartySiteId=@PartySiteId)
              AND (@Search IS NULL OR party.DisplayName LIKE N'%'+@Search+N'%'
                OR party.LegalName LIKE N'%'+@Search+N'%'
                OR party.Identification LIKE N'%'+@Search+N'%'
                OR site.Name LIKE N'%'+@Search+N'%' OR site.Code LIKE N'%'+@Search+N'%');
            SELECT party.PartyId,partyRole.{role.Id},site.PartySiteId,
              COALESCE(NULLIF(party.DisplayName,N''),NULLIF(party.LegalName,N''),N'Sin nombre'),
              COALESCE(party.Identification,N''),site.Name,site.IsPrimary,
              {supplierPolicy},{supplierDueDays}
            FROM dbo.Parties party
            JOIN {role.Table} partyRole ON partyRole.PartyId=party.PartyId
              AND partyRole.TenantId=@TenantId AND partyRole.IsActive=1
            JOIN dbo.PartySites site ON site.PartyId=party.PartyId AND site.IsActive=1
            WHERE party.TenantId=@TenantId AND party.IsActive=1
              AND (@RoleId IS NULL OR partyRole.{role.Id}=@RoleId)
              AND (@PartySiteId IS NULL OR site.PartySiteId=@PartySiteId)
              AND (@Search IS NULL OR party.DisplayName LIKE N'%'+@Search+N'%'
                OR party.LegalName LIKE N'%'+@Search+N'%'
                OR party.Identification LIKE N'%'+@Search+N'%'
                OR site.Name LIKE N'%'+@Search+N'%' OR site.Code LIKE N'%'+@Search+N'%')
            ORDER BY party.DisplayName,party.PartyId,site.IsPrimary DESC,site.Name,site.PartySiteId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        command.Parameters.AddRange([
            new SqlParameter("@TenantId", actor.TenantId),
            new SqlParameter("@RoleId", (object?)query.RoleId ?? DBNull.Value),
            new SqlParameter("@PartySiteId", (object?)query.PartySiteId ?? DBNull.Value),
            new SqlParameter("@Search", (object?)Empty(query.Search) ?? DBNull.Value),
            new SqlParameter("@Offset", (page - 1) * query.PageSize),
            new SqlParameter("@PageSize", query.PageSize)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var total = checked((int)reader.GetInt64(0));
        await reader.NextResultAsync(ct);
        var items = new List<PartySiteRoleOption>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetBoolean(6),
                S(reader, 7), reader.IsDBNull(8) ? null : reader.GetInt32(8)));
        return new(items, page, query.PageSize, total,
            (int)Math.Ceiling(total / (double)query.PageSize));
    }

    public async Task<PartyRoleOptionPage> RoleOptionsAsync(
        PartyActorIdentity actor, int page, PartyRoleOptionQuery query, CancellationToken ct)
    {
        var role = RoleSource(query.Role);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT_BIG(1)
            FROM dbo.Parties party
            INNER JOIN {role.Table} partyRole
              ON partyRole.PartyId=party.PartyId AND {role.Scope}
            WHERE party.TenantId=@TenantId AND partyRole.IsActive=1
              AND (@RoleId IS NULL OR partyRole.{role.IdColumn}=@RoleId)
              AND (@PartyId IS NULL OR party.PartyId=@PartyId)
              AND (@Search IS NULL OR party.DisplayName LIKE N'%'+@Search+N'%'
                   OR party.LegalName LIKE N'%'+@Search+N'%'
                   OR party.FirstName LIKE N'%'+@Search+N'%'
                   OR party.LastName LIKE N'%'+@Search+N'%'
                   OR party.Identification LIKE N'%'+@Search+N'%'
                   OR party.NormalizedIdentification LIKE N'%'+@Search+N'%');

            SELECT party.PartyId,partyRole.{role.IdColumn},
                   COALESCE(NULLIF(party.DisplayName,N''),NULLIF(party.LegalName,N''),N'Sin nombre'),
                   party.Identification,
                   {role.SupplierPolicy},
                   {role.SupplierDueDays}
            FROM dbo.Parties party
            INNER JOIN {role.Table} partyRole
              ON partyRole.PartyId=party.PartyId AND {role.Scope}
            WHERE party.TenantId=@TenantId AND partyRole.IsActive=1
              AND (@RoleId IS NULL OR partyRole.{role.IdColumn}=@RoleId)
              AND (@PartyId IS NULL OR party.PartyId=@PartyId)
              AND (@Search IS NULL OR party.DisplayName LIKE N'%'+@Search+N'%'
                   OR party.LegalName LIKE N'%'+@Search+N'%'
                   OR party.FirstName LIKE N'%'+@Search+N'%'
                   OR party.LastName LIKE N'%'+@Search+N'%'
                   OR party.Identification LIKE N'%'+@Search+N'%'
                   OR party.NormalizedIdentification LIKE N'%'+@Search+N'%')
            ORDER BY party.DisplayName,party.PartyId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        command.Parameters.AddRange([
            new SqlParameter("@TenantId", actor.TenantId),
            new SqlParameter("@BusinessId", actor.BusinessId),
            new SqlParameter("@RoleId", (object?)query.RoleId ?? DBNull.Value),
            new SqlParameter("@PartyId", (object?)query.PartyId ?? DBNull.Value),
            new SqlParameter("@Search", (object?)Empty(query.Search) ?? DBNull.Value),
            new SqlParameter("@Offset", (page - 1) * query.PageSize),
            new SqlParameter("@PageSize", query.PageSize)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var total = checked((int)reader.GetInt64(0));
        await reader.NextResultAsync(ct);
        var items = new List<PartyRoleOption>();
        while (await reader.ReadAsync(ct))
            items.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), query.Role, reader.GetString(2), S(reader, 3),
                S(reader, 4), reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        return new(items, page, query.PageSize, total,
            (int)Math.Ceiling(total / (double)query.PageSize));
    }

    private static RoleOptionSource RoleSource(string role) => role switch
    {
        "Any" => new("""
            (SELECT scoped.PartyId,scoped.PartyId AS RoleId,CAST(1 AS bit) AS IsActive
             FROM (
               SELECT PartyId FROM dbo.Customers WHERE TenantId=@TenantId AND IsActive=1
               UNION SELECT PartyId FROM dbo.Suppliers WHERE TenantId=@TenantId AND IsActive=1
               UNION SELECT PartyId FROM dbo.CommerceSellers WHERE TenantId=@TenantId AND IsActive=1
               UNION SELECT PartyId FROM dbo.Carriers WHERE TenantId=@TenantId AND IsActive=1
               UNION SELECT PartyId FROM dbo.Employees WHERE TenantId=@TenantId AND IsActive=1
               UNION SELECT PartyId FROM dbo.AppUsers WHERE TenantId=@TenantId AND IsActive=1 AND PartyId IS NOT NULL
             ) scoped)
            """, "RoleId", "1=1", "CAST(NULL AS NVARCHAR(40))", "CAST(NULL AS INT)"),
        "Customer" => new("dbo.Customers", "CustomerId", "partyRole.TenantId=@TenantId", "CAST(NULL AS NVARCHAR(40))", "CAST(NULL AS INT)"),
        "Supplier" => new("dbo.Suppliers", "SupplierId", "partyRole.TenantId=@TenantId", "partyRole.PurchaseEvidencePolicy", "partyRole.DefaultPaymentDueDays"),
        "Seller" => new("dbo.CommerceSellers", "SellerId", "partyRole.TenantId=@TenantId", "CAST(NULL AS NVARCHAR(40))", "CAST(NULL AS INT)"),
        "Carrier" => new("dbo.Carriers", "CarrierId", "partyRole.TenantId=@TenantId", "CAST(NULL AS NVARCHAR(40))", "CAST(NULL AS INT)"),
        "Employee" => new("dbo.Employees", "EmployeeId", "partyRole.TenantId=@TenantId", "CAST(NULL AS NVARCHAR(40))", "CAST(NULL AS INT)"),
        "User" => new("dbo.AppUsers", "UserId", "partyRole.TenantId=@TenantId", "CAST(NULL AS NVARCHAR(40))", "CAST(NULL AS INT)"),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private sealed record RoleOptionSource(
        string Table, string IdColumn, string Scope, string SupplierPolicy, string SupplierDueDays);
}
