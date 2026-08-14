using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;
using Auraly.Domain.Parties;

namespace Auraly.Application.Parties;

public interface ICommercialPartyRoleStore
{
    Task<CommercialRoleAcceptance> CreateSellerAsync(PartyActorIdentity actor, Guid partyId, Guid roleId, Guid siteId, CreateSellerRequest request, string normalizedIdentification, DateTimeOffset now, CancellationToken ct);
    Task<CommercialRoleAcceptance> CreateCarrierAsync(PartyActorIdentity actor, Guid partyId, Guid roleId, Guid siteId, CreateCarrierRequest request, string normalizedIdentification, DateTimeOffset now, CancellationToken ct);
    Task<CustomerPricingOptions> PricingOptionsAsync(PartyActorIdentity actor, CancellationToken ct);
}

public sealed class CommercialPartyRoleService(ICommercialPartyRoleStore store, IAuralyIdGenerator ids, TimeProvider time)
{
    public Task<CustomerPricingOptions> PricingOptionsAsync(PartyActorIdentity actor, CancellationToken ct)
    {
        RequireAny(actor, PartyPermissionCodes.CustomerCreate, PartyPermissionCodes.ManagePricing);
        return store.PricingOptionsAsync(actor, ct);
    }

    public Task<CommercialRoleAcceptance> CreateSellerAsync(PartyActorIdentity actor, CreateSellerRequest request, CancellationToken ct)
    {
        RequireAny(actor, PartyWorkspacePermissionCodes.SellerCreate);
        ValidateCommon(actor, request.BusinessId, request.Party, request.PrimarySite);

        if (request.DefaultCommissionPercent is < 0 or > 100)
            throw new PartyValidationException("Commission must be between 0 and 100.");
        if (request.CommissionBasis is not ("SaleBeforeTax" or "SaleAfterTax" or "GrossMargin"))
            throw new PartyValidationException("Commission basis is invalid.");
        if (request.CommissionTrigger is not ("Sale" or "Collection"))
            throw new PartyValidationException("Commission trigger is invalid.");
        var roleId = ids.NewId();
        return store.CreateSellerAsync(actor, ids.NewId(), roleId, ids.NewId(),
            request with { Code = InternalCode("VEN", roleId) },
            Normalize(request.Party), time.GetUtcNow(), ct);
    }

    public Task<CommercialRoleAcceptance> CreateCarrierAsync(PartyActorIdentity actor, CreateCarrierRequest request, CancellationToken ct)
    {
        RequireAny(actor, PartyWorkspacePermissionCodes.CarrierCreate);
        ValidateCommon(actor, request.BusinessId, request.Party, request.PrimarySite);

        if (request.TransportationMode is not ("Road" or "Air" or "Maritime" or "Other"))
            throw new PartyValidationException("Transportation mode is invalid.");
        var roleId = ids.NewId();
        return store.CreateCarrierAsync(actor, ids.NewId(), roleId, ids.NewId(),
            request with { Code = InternalCode("TRA", roleId) },
            Normalize(request.Party), time.GetUtcNow(), ct);
    }

    private static void ValidateCommon(PartyActorIdentity actor, Guid businessId, PartyInput party, PartySiteInput site)
    {
        if (businessId != actor.BusinessId) throw new PartyForbiddenException("Business does not match the authenticated identity.");
        if (party.IdentificationCountryId == Guid.Empty || site.CountryId == Guid.Empty || site.AdministrativeDivisionId == Guid.Empty || site.CityId == Guid.Empty)
            throw new PartyValidationException("Country, administrative division and city are required.");
        if (party.PartyType is not PartyTypes.NaturalPerson and not PartyTypes.Organization)
            throw new PartyValidationException("Party type is invalid.");
        if (string.IsNullOrWhiteSpace(party.DisplayName) || party.DisplayName.Length > 200)
            throw new PartyValidationException("Display name is required.");
        if (party.PartyType == PartyTypes.Organization && string.IsNullOrWhiteSpace(party.LegalName))
            throw new PartyValidationException("Legal name is required for an organization.");
        if (string.IsNullOrWhiteSpace(site.Code) || string.IsNullOrWhiteSpace(site.Name) || string.IsNullOrWhiteSpace(site.AddressLine))
            throw new PartyValidationException("Site code, name and address are required.");
        if ((site.Latitude is null) != (site.Longitude is null))
            throw new PartyValidationException("Latitude and longitude must be provided together.");
        if (site.Latitude is < -90 or > 90 || site.Longitude is < -180 or > 180)
            throw new PartyValidationException("The site coordinates are outside the valid range.");
        if (site.GoogleMapsUrl?.Length > 1000 || site.GooglePlaceId?.Length > 255)
            throw new PartyValidationException("The Google Maps location is too long.");
    }

    private static string InternalCode(string prefix, Guid id) =>
        $"{prefix}-{id.ToString("N")[^12..].ToUpperInvariant()}";
    private static string Normalize(PartyInput party)
    {
        try { return PartyIdentityNormalizer.Normalize(party.IdentificationTypeCode, party.Identification); }
        catch (ArgumentException ex) { throw new PartyValidationException(ex.Message); }
    }

    private static void RequireAny(PartyActorIdentity actor, params string[] permissions)
    {
        if (!permissions.Any(actor.Permissions.Contains))
            throw new PartyForbiddenException($"One of these permissions is required: {string.Join(", ", permissions)}.");
    }
}
