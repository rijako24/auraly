using Auraly.Contracts.Returns;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Parties;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Sales;
using Auraly.Domain.Parties;

namespace Auraly.Application.Parties;

public interface IPartyWorkspaceStore
{
    Task<PartyWorkspacePage> PageAsync(
        PartyActorIdentity actor, int page, PartyWorkspaceQuery query, CancellationToken ct);
    Task<PartyRoleOptionPage> RoleOptionsAsync(
        PartyActorIdentity actor, int page, PartyRoleOptionQuery query, CancellationToken ct);
    Task<PartySiteRoleOptionPage> SiteRoleOptionsAsync(
        PartyActorIdentity actor, int page, PartyRoleOptionQuery query, CancellationToken ct);
    Task<IReadOnlyCollection<CustomerMapSite>> CustomerMapAsync(
        PartyActorIdentity actor, CustomerMapQuery query, CancellationToken ct);
    Task<PartyWorkspaceDetail?> FindIdentityAsync(
        PartyActorIdentity actor, Guid countryId, string identificationType,
        string normalizedIdentification, CancellationToken ct);
    Task<PartyWorkspaceDetail?> GetDetailAsync(
        PartyActorIdentity actor, Guid partyId, CancellationToken ct);
    Task<PartyIdentityAcceptance> CreateIdentityAsync(
        PartyActorIdentity actor, Guid partyId, Guid siteId,
        CreatePartyIdentityRequest request, string normalizedIdentification,
        DateTimeOffset now, CancellationToken ct);
    Task<SupplierAcceptance> CreateSupplierAsync(
        PartyActorIdentity actor, Guid partyId, Guid supplierId, Guid siteId,
        CreateSupplierRequest request, string normalizedIdentification,
        DateTimeOffset now, CancellationToken ct);
    Task<PartyWorkspaceItem> UpdateAsync(
        PartyActorIdentity actor, Guid partyId, UpdatePartyRequest request,
        byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
    Task<PartyWorkspaceItem> SetStatusAsync(
        PartyActorIdentity actor, Guid partyId, SetPartyBusinessStatusRequest request,
        byte[] rowVersion, DateTimeOffset now, CancellationToken ct);
}

public sealed class PartyWorkspaceService(
    IPartyWorkspaceStore store,
    IAuralyIdGenerator ids,
    TimeProvider time,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<PartyWorkspacePage> PageAsync(
        PartyActorIdentity actor, int page, PartyWorkspaceQuery query, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Read, PartyPermissionCodes.CustomerRead, PartyWorkspacePermissionCodes.SupplierRead);
        if (page < 1 || query.PageSize is < 1 or > 100)
            throw new PartyValidationException("La página o la cantidad de registros supera el rango permitido.");
        var role = query.Role?.Trim();
        if (role is not null && role is not ("Customer" or "Supplier" or "Seller" or "Carrier" or "Employee" or "User" or "Waiter"))
            throw new PartyValidationException("Selecciona un rol válido: cliente, proveedor, vendedor, transportador, empleado, usuario o mesero.");
        return store.PageAsync(actor, page, query with { Search = query.Search?.Trim(), Role = role }, ct);
    }

    public Task<PartyRoleOptionPage> RoleOptionsAsync(
        PartyActorIdentity actor, int page, PartyRoleOptionQuery query, CancellationToken ct)
    {
        // Expense readers need the scoped supplier picker for filters; expense
        // creators use the same picker without full third-party workspace access.
        var returnCustomer = query.Role?.Trim() == "Customer" &&
            (actor.Permissions.Contains(SalesReturnPermissionCodes.Read) ||
             actor.Permissions.Contains(SalesReturnPermissionCodes.Create) ||
             actor.Permissions.Contains("restaurant.attend") ||
             actor.Permissions.Contains("restaurant.tables.open_without_waiter"));
        var salesSeller=query.Role?.Trim()=="Seller" && actor.Permissions.Contains("sales.create");
        var invoiceFilter=query.Role?.Trim() is "Seller" or "User" && actor.Permissions.Contains("sales.reprint");
        var chargeHistoryFilter=query.Role?.Trim() is "Customer" or "User" && actor.Permissions.Contains(InvoiceChargePermissions.Read);
        if(query.IncludeInactive && !invoiceFilter && !chargeHistoryFilter)
            throw new PartyForbiddenException("La consulta de identidades inactivas solo está disponible para los filtros de facturas.");
        var dispatchCarrier=query.Role?.Trim()=="Carrier" && actor.Permissions.Contains("dispatches.create");
        var accountingParty = query.Role?.Trim() == "Any" && actor.Permissions.Contains("accounting.read");
        var payrollParty = query.Role?.Trim() == "Any" && query.PartyType=="Organization" &&
            (actor.Permissions.Contains("payroll.manage") || actor.Permissions.Contains("payroll.configure"))
            || query.Role?.Trim()=="Employee" && actor.Permissions.Contains("payroll.manage");
        if (!accountingParty && !payrollParty && !returnCustomer && !dispatchCarrier && !salesSeller && !invoiceFilter && !chargeHistoryFilter && (query.Role?.Trim() != "Supplier" ||
            (!actor.Permissions.Contains(ExpensePermissionCodes.Create) &&
             !actor.Permissions.Contains(ExpensePermissionCodes.Read) &&
             !actor.Permissions.Contains(InvoiceChargePermissions.Configure))))
            Require(actor, PartyWorkspacePermissionCodes.Read, PartyPermissionCodes.CustomerRead, PartyWorkspacePermissionCodes.SupplierRead);
        // Explicit key resolution is bounded by its requested keys, rather
        // than loading a tenant directory. Browsing retains its 100-row limit.
        var maximumPageSize = Math.Max(100, query.RoleIds?.Count ?? 0);
        if (query.RoleIds is not null && (query.RoleIds.Count < 1 ||
            query.RoleIds.Any(id => id == Guid.Empty) || query.RoleIds.Distinct().Count() != query.RoleIds.Count))
            throw new PartyValidationException("Indica roles distintos y válidos para la consulta.");
        if (page < 1 || query.PageSize < 1 || query.PageSize > maximumPageSize || ((long)page-1)*query.PageSize>int.MaxValue)
            throw new PartyValidationException("La página o la cantidad de registros supera el rango permitido.");
        var role = query.Role?.Trim();
        if (role is not ("Any" or "Customer" or "Supplier" or "Seller" or "Carrier" or "Employee" or "User" or "Waiter"))
            throw new PartyValidationException("Selecciona todos los terceros o un rol válido: cliente, proveedor, vendedor, transportador, empleado, usuario o mesero.");
        if(query.PartyType is not (null or "Organization" or "NaturalPerson"))
            throw new PartyValidationException("El tipo de tercero no es válido.");
        return store.RoleOptionsAsync(
            actor, page, query with { Role = role, Search = query.Search?.Trim() }, ct);
    }

    public Task<PartySiteRoleOptionPage> SiteRoleOptionsAsync(
        PartyActorIdentity actor, int page, PartyRoleOptionQuery query, CancellationToken ct)
    {
        if (query.Role is not ("Customer" or "Supplier") || page < 1 ||
            query.PageSize is < 1 or > 100)
            throw new PartyValidationException("Rol o paginación inválidos para sedes de terceros.");
        Require(actor, query.Role == "Customer" ? PartyPermissionCodes.CustomerRead :
            PartyWorkspacePermissionCodes.SupplierRead);
        return store.SiteRoleOptionsAsync(actor, page, query with { Search = query.Search?.Trim() }, ct);
    }

    public Task<IReadOnlyCollection<CustomerMapSite>> CustomerMapAsync(
        PartyActorIdentity actor, CustomerMapQuery query, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Read, PartyPermissionCodes.CustomerRead);
        return store.CustomerMapAsync(actor, query with { Search = query.Search?.Trim() }, ct);
    }

    public async Task<PartyIdentityLookupResult> FindIdentityAsync(
        PartyActorIdentity actor,
        Guid countryId,
        string identificationTypeCode,
        string identification,
        string requestedRole,
        CancellationToken ct)
    {
        Require(actor,
            PartyWorkspacePermissionCodes.Read,
            PartyPermissionCodes.CustomerCreate,
            PartyWorkspacePermissionCodes.SupplierCreate,
            PartyWorkspacePermissionCodes.SellerCreate,
            PartyWorkspacePermissionCodes.CarrierCreate,
            PartyWorkspacePermissionCodes.EmployeeCreate,
            PartyWorkspacePermissionCodes.WaiterCreate,
            PartyWorkspacePermissionCodes.UserCreate);
        if (countryId == Guid.Empty)
            throw new PartyValidationException("Selecciona el país de la identificación.");
        var role = requestedRole?.Trim();
        if (role is not ("Customer" or "Supplier" or "Seller" or "Carrier" or "Employee" or "User" or "Waiter"))
            throw new PartyValidationException("El rol solicitado no es válido.");
        if (string.IsNullOrWhiteSpace(identificationTypeCode))
            throw new PartyValidationException("Selecciona un tipo de identificación.");
        var type = identificationTypeCode.Trim().ToUpperInvariant();
        var normalized = Translate(() =>
            PartyIdentityNormalizer.Normalize(type, identification));
        var party = await store.FindIdentityAsync(
            actor, countryId, type, normalized, ct);
        return new PartyIdentityLookupResult(
            party is not null,
            party?.Roles.Contains(role) == true,
            party);
    }

    public async Task<PartyWorkspaceDetail> GetDetailAsync(
        PartyActorIdentity actor, Guid partyId, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Read, PartyPermissionCodes.CustomerRead, PartyWorkspacePermissionCodes.SupplierRead);
        if (partyId == Guid.Empty)
            throw new PartyValidationException("Se requiere un tercero válido.");
        return await store.GetDetailAsync(actor, partyId, ct)
            ?? throw new PartyForbiddenException(
                "El tercero no pertenece a la sede autorizada.");
    }

    public Task<PartyIdentityAcceptance> CreateIdentityAsync(
        PartyActorIdentity actor, CreatePartyIdentityRequest request, CancellationToken ct)
    {
        var permission = request.TargetRole switch
        {
            "Employee" => PartyWorkspacePermissionCodes.EmployeeCreate,
            "User" => PartyWorkspacePermissionCodes.UserCreate,
            _ => throw new PartyValidationException("El rol de destino debe ser empleado o usuario.")
        };
        Require(actor, permission);
        if (request.BusinessId != actor.BusinessId)
            throw new PartyForbiddenException("La empresa del tercero no corresponde al contexto autorizado.");
        ValidateParty(request.Party);
        ValidateSite(request.PrimarySite);
        var normalized = Translate(() => PartyIdentityNormalizer.Normalize(
            request.Party.IdentificationTypeCode, request.Party.Identification));
        return store.CreateIdentityAsync(
            actor, ids.NewId(), ids.NewId(), request, normalized, time.GetUtcNow(), ct);
    }
    public Task<SupplierAcceptance> CreateSupplierAsync(
        PartyActorIdentity actor, CreateSupplierRequest request, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.SupplierCreate);
        if (request.BusinessId != actor.BusinessId)
            throw new PartyForbiddenException("La empresa del proveedor no corresponde al contexto autorizado.");
        ValidateParty(request.Party);
        ValidateSite(request.PrimarySite);
        ValidatePurchaseEvidencePolicy(request.PurchaseEvidencePolicy);
        if (request.DefaultPaymentDueDays is < 0 or > 3650)
            throw new PartyValidationException("El plazo de pago predeterminado debe estar entre 0 y 3.650 días.");
        var normalized = Translate(() => PartyIdentityNormalizer.Normalize(
            request.Party.IdentificationTypeCode, request.Party.Identification));
        return store.CreateSupplierAsync(
            actor, ids.NewId(), ids.NewId(), ids.NewId(), request, normalized, time.GetUtcNow(), ct);
    }

    public async Task<PartyWorkspaceItem> UpdateAsync(
        PartyActorIdentity actor, Guid partyId, UpdatePartyRequest request, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Update);
        if (partyId == Guid.Empty) throw new PartyValidationException("Se requiere un tercero válido.");
        if (request.PartyType is not PartyTypes.NaturalPerson and not PartyTypes.Organization)
            throw new PartyValidationException("El tipo de tercero no es válido.");
        Translate(() => { PartyValidation.RequireText(request.DisplayName, "Nombre del tercero", 200); return true; });
        if (request.PartyType == PartyTypes.Organization && string.IsNullOrWhiteSpace(request.LegalName))
            throw new PartyValidationException("La razón social es obligatoria para una organización.");
        if (request.LegalName?.Length > 200 || request.FirstName?.Length > 100 || request.LastName?.Length > 100 ||
            request.Email?.Length > 254 || request.Phone?.Length > 254 || request.VerificationDigit?.Length > 4)
            throw new PartyValidationException("El nombre o los datos de contacto del tercero superan la longitud permitida.");
        if (request.Sites is not null)
        {
            Require(actor, PartyPermissionCodes.ManageSites);
            if (request.Sites.Count == 0) throw new PartyValidationException("Un tercero debe conservar al menos una sede.");
            if (request.Sites.Count(site => site.Site.IsPrimary) != 1)
                throw new PartyValidationException("El tercero debe tener exactamente una sede principal.");
            foreach (var site in request.Sites) ValidateSite(site.Site);
        }
        if (request.Customer?.PriceChannelId == Guid.Empty)
            throw new PartyValidationException("El canal de precios debe estar vacío o tener una identificación válida.");
        if (request.Customer?.ValidUntil is { } validUntil &&
            request.Customer.ValidFrom is { } validFrom && validUntil <= validFrom)
            throw new PartyValidationException("La fecha final del canal debe ser posterior a la fecha inicial.");
        if (request.Customer is not null)
            Require(actor, PartyPermissionCodes.ManagePricing);
        if (request.Supplier is not null)
        {
            ValidatePurchaseEvidencePolicy(request.Supplier.PurchaseEvidencePolicy);
            if (request.Supplier.DefaultPaymentDueDays is < 0 or > 3650)
                throw new PartyValidationException("El plazo de pago predeterminado debe estar entre 0 y 3.650 días.");
        }
        if (request.Seller is not null)
        {
            Translate(() => PartyValidation.NormalizeCode(request.Seller.Code, "Código del vendedor", 32));
            if (request.Seller.DefaultCommissionPercent is < 0 or > 100)
                throw new PartyValidationException("La comisión predeterminada debe estar entre 0 % y 100 %.");
            if (request.Seller.CommissionBasis is not "SaleBeforeTax" and not "SaleAfterTax" and not "GrossMargin")
                throw new PartyValidationException("La base de cálculo de la comisión no es válida.");
            if (request.Seller.CommissionTrigger is not "Sale" and not "Collection")
                throw new PartyValidationException("El momento de causación de la comisión no es válido.");
        }
        if (request.Carrier is not null)
        {
            Translate(() => PartyValidation.NormalizeCode(request.Carrier.Code, "Código del transportador", 32));
            if (request.Carrier.TransportationMode is not "Road" and not "Air" and not "Maritime" and not "Other")
                throw new PartyValidationException("El modo de transporte no es válido.");
        }
        if (request.Waiter is not null)
        {
            Require(actor, PartyWorkspacePermissionCodes.WaiterConfigure);
            if (request.Waiter.ShortName?.Trim().Length > 80)
                throw new PartyValidationException("El nombre corto admite máximo 80 caracteres.");
        }
        var updated = await store.UpdateAsync(actor, partyId, request, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
        await synchronization.DispatchTenantPendingAsync(actor.TenantId, CancellationToken.None);
        return updated;
    }

    public async Task<PartyWorkspaceItem> SetStatusAsync(
        PartyActorIdentity actor, Guid partyId, SetPartyBusinessStatusRequest request, CancellationToken ct)
    {
        Require(actor, PartyWorkspacePermissionCodes.Deactivate);
        if (partyId == Guid.Empty) throw new PartyValidationException("Se requiere un tercero válido.");
        var updated = await store.SetStatusAsync(actor, partyId, request, RowVersion(request.RowVersion), time.GetUtcNow(), ct);
        await synchronization.DispatchTenantPendingAsync(actor.TenantId, CancellationToken.None);
        return updated;
    }

    private static void ValidateParty(PartyInput party)
    {
        if (party.IdentificationCountryId == Guid.Empty)
            throw new PartyValidationException("Selecciona el país de la identificación.");
        if (party.PartyType is not PartyTypes.NaturalPerson and not PartyTypes.Organization)
            throw new PartyValidationException("El tipo de tercero no es válido.");
        Translate(() => { PartyValidation.RequireText(party.DisplayName, "Nombre del tercero", 200); return true; });
        if (party.PartyType == PartyTypes.Organization && string.IsNullOrWhiteSpace(party.LegalName))
            throw new PartyValidationException("La razón social es obligatoria para una organización.");
    }

    private static void ValidateSite(PartySiteInput site)
    {
        if (site.CountryId == Guid.Empty || site.AdministrativeDivisionId == Guid.Empty || site.CityId == Guid.Empty)
            throw new PartyValidationException("Selecciona país, departamento o división administrativa y ciudad.");
        Translate(() => PartyValidation.NormalizeCode(site.Code, "Código de la sede", 32));
        Translate(() => { PartyValidation.RequireText(site.Name, "Nombre de la sede", 160); return true; });
        Translate(() => { PartyValidation.RequireText(site.AddressLine, "Dirección", 300); return true; });
        if ((site.Latitude is null) != (site.Longitude is null))
            throw new PartyValidationException("La latitud y la longitud deben indicarse juntas.");
        if (site.Latitude is < -90 or > 90 || site.Longitude is < -180 or > 180)
            throw new PartyValidationException("Las coordenadas de la sede están fuera del rango válido.");
        if (site.GoogleMapsUrl?.Length > 1000 || site.GooglePlaceId?.Length > 255)
            throw new PartyValidationException("La ubicación de Google Maps supera la longitud permitida.");
    }

    private static void ValidatePurchaseEvidencePolicy(string? value)
    {
        if (value is not null && value is not
            ("SupplierElectronicInvoice" or "BuyerElectronicSupportDocument" or "InternalReceiptVoucher"))
            throw new PartyValidationException("La política de soporte de compras no es válida.");
    }

    private static byte[] RowVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PartyValidationException("Se requiere la versión del documento. Vuelve a abrirlo antes de continuar.");
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 8) throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new PartyValidationException("La versión del documento no es válida. Vuelve a abrirlo antes de continuar.");
        }
    }

    private static T Translate<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArgumentException exception) { throw new PartyValidationException(exception.Message); }
    }

    private static void Require(PartyActorIdentity actor, params string[] permissions)
    {
        if (!permissions.Any(actor.Permissions.Contains))
            throw new PartyForbiddenException("No tienes permiso para realizar esta acción sobre terceros.");
    }
}

