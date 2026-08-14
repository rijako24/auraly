using Auraly.Contracts.Parties;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPartyWorkspaceStore
{
    public async Task<IReadOnlyCollection<CustomerMapSite>> CustomerMapAsync(
        PartyActorIdentity actor, CustomerMapQuery query, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT TOP(2000)
              customer.CustomerId,party.PartyId,party.DisplayName,party.Identification,
              site.PartySiteId,site.Name,site.AddressLine,site.Neighborhood,city.Name,
              COALESCE(site.Phone,phone.Value),site.GoogleMapsUrl,site.Latitude,site.Longitude,
              route.RouteId,route.Name,seller.SellerId,sellerParty.DisplayName
            FROM dbo.Customers customer
            INNER JOIN dbo.Parties party ON party.PartyId=customer.PartyId AND party.TenantId=@TenantId AND party.IsActive=1
            INNER JOIN dbo.PartySites site ON site.PartyId=party.PartyId AND site.IsActive=1
            INNER JOIN dbo.Cities city ON city.CityId=site.CityId
            OUTER APPLY(SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact
                        WHERE contact.PartyId=party.PartyId AND contact.ContactType=N'Phone' AND contact.IsActive=1
                        ORDER BY contact.IsPrimary DESC,contact.CreatedAt) phone
            LEFT JOIN dbo.SalesRouteStops stop ON stop.PartySiteId=site.PartySiteId AND stop.CustomerId=customer.CustomerId AND stop.IsActive=1
            LEFT JOIN dbo.SalesRoutes route ON route.RouteId=stop.RouteId AND route.BusinessId=customer.BusinessId AND route.IsActive=1
            LEFT JOIN dbo.CommerceSellers seller ON seller.SellerId=route.SellerId AND seller.BusinessId=customer.BusinessId AND seller.IsActive=1
            LEFT JOIN dbo.Parties sellerParty ON sellerParty.PartyId=seller.PartyId
            WHERE customer.BusinessId=@BusinessId AND customer.IsActive=1
              AND (@Search IS NULL OR party.DisplayName LIKE N'%'+@Search+N'%' OR party.Identification LIKE N'%'+@Search+N'%'
                   OR site.Name LIKE N'%'+@Search+N'%' OR site.AddressLine LIKE N'%'+@Search+N'%')
              AND (@RouteId IS NULL OR route.RouteId=@RouteId)
              AND (@SellerId IS NULL OR seller.SellerId=@SellerId)
              AND (@OnlyUnassigned=0 OR route.RouteId IS NULL)
              AND (@ReadAllRoutes=1 OR EXISTS(
                    SELECT 1 FROM dbo.AppUsers currentUser
                    INNER JOIN dbo.CommerceSellers ownSeller ON ownSeller.PartyId=currentUser.PartyId AND ownSeller.BusinessId=@BusinessId AND ownSeller.IsActive=1
                    WHERE currentUser.UserId=@ActorId AND currentUser.TenantId=@TenantId AND ownSeller.SellerId=route.SellerId))
            ORDER BY party.DisplayName,site.IsPrimary DESC,site.Name,route.Name;
            """, connection);
        command.Parameters.AddRange([
            P("@TenantId", actor.TenantId), P("@BusinessId", actor.BusinessId), P("@ActorId", actor.ActorId),
            P("@Search", Empty(query.Search)), P("@RouteId", query.RouteId), P("@SellerId", query.SellerId),
            P("@OnlyUnassigned", query.OnlyUnassigned), P("@ReadAllRoutes", actor.Permissions.Contains("routes.read-all"))]);

        var sites = new Dictionary<Guid, SiteBuilder>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var siteId = reader.GetGuid(4);
            if (!sites.TryGetValue(siteId, out var site))
            {
                site = new SiteBuilder(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), siteId, reader.GetString(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                    reader.IsDBNull(12) ? null : reader.GetDecimal(12));
                sites.Add(siteId, site);
            }
            if (!reader.IsDBNull(13))
            {
                var routeId = reader.GetGuid(13);
                if (site.Assignments.All(value => value.RouteId != routeId))
                    site.Assignments.Add(new(routeId, reader.GetString(14), reader.GetGuid(15), reader.GetString(16)));
            }
        }
        return sites.Values.Select(site => site.Build()).ToArray();
    }

    private sealed class SiteBuilder(
        Guid customerId, Guid partyId, string customerName, string? identification,
        Guid partySiteId, string siteName, string addressLine, string? neighborhood,
        string cityName, string? phone, string? googleMapsUrl, decimal? latitude, decimal? longitude)
    {
        public List<CustomerMapAssignment> Assignments { get; } = [];
        public CustomerMapSite Build() => new(customerId, partyId, customerName, identification, partySiteId,
            siteName, addressLine, neighborhood, cityName, phone, googleMapsUrl, latitude, longitude, Assignments);
    }
}
