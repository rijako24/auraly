using Auraly.Contracts.Sales;
using Auraly.Domain.Sales;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Application.Expenses;
using Auraly.Application.Catalog;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Authorization;

namespace Auraly.Application.Sales;

public interface IInvoiceChargeStore
{
    Task<InvoiceChargeHistoryPage> HistoryAsync(InvoiceChargeActor actor, DateOnly from, DateOnly to,
        int page, int pageSize, string? search, CancellationToken ct);
    Task<InvoiceChargePage> ListAsync(InvoiceChargeActor actor, int page, int pageSize,
        string? search, bool includeInactive, CancellationToken ct, long? throughCursor = null);
    Task<InvoiceChargeDefinition> SaveAsync(InvoiceChargeActor actor,
        SaveInvoiceChargeRequest request, CancellationToken ct);
}

public sealed class InvoiceChargeService(IInvoiceChargeStore store,
    IPosSynchronizationOutboxDispatcher synchronization, IExpenseStore expenses, ICatalogStore catalog)
{
    public Task<InvoiceChargeHistoryPage> HistoryAsync(InvoiceChargeActor actor, DateOnly from, DateOnly to,
        int page, int pageSize, string? search, CancellationToken ct = default)
    {
        Demand(actor, InvoiceChargePermissions.Read);
        if (from == default || to == DateOnly.MaxValue || to < from || to.DayNumber - from.DayNumber > 30 ||
            page is < 1 or > 1000000 || pageSize is < 1 or > 100 || search?.Length > 120)
            throw new InvoiceChargeValidationException("Consulta hasta 31 días, con una paginación válida.");
        return store.HistoryAsync(actor, from, to, page, pageSize,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(), ct);
    }

    public Task<InvoiceChargePage> PosOptionsAsync(InvoiceChargeActor actor, int page, CancellationToken ct = default)
    {
        Demand(actor, CommercePermissionCodes.SalesCreate);
        if (page is < 1 or > 1000000) throw new InvoiceChargeValidationException("Página no válida.");
        return store.ListAsync(actor, page, 100, null, false, ct);
    }

    public async Task<InvoiceChargePage> DeviceOptionsAsync(PosDeviceIdentity device, Guid businessId,
        int page, long throughCursor, CancellationToken ct = default)
    {
        if (page is < 1 or > 1000000 || throughCursor < 0)
            throw new InvoiceChargeValidationException("La página o el cursor no es válido.");
        // This canonical query also authorizes the enrolled device against the
        // active business. Never trust a business ID supplied by the device.
        var currentCursor = await catalog.PricingCursorAsync(device.DeviceId, device.TenantId, businessId, ct);
        if (throughCursor > currentCursor)
            throw new InvoiceChargeValidationException("El cursor solicitado todavía no existe.");
        return await store.ListAsync(new(device.TenantId, businessId, Guid.Empty, new HashSet<string>()),
            page, 100, null, false, ct, throughCursor);
    }

    public async Task<InvoiceChargeConfigurationOptions> OptionsAsync(InvoiceChargeActor actor, CancellationToken ct = default)
    {
        Demand(actor, InvoiceChargePermissions.Configure);
        var concepts = expenses.ListConceptsAsync(new ExpenseUserIdentity(actor.UserId, actor.TenantId,
            actor.BusinessId, actor.Permissions), false, ct);
        var taxes = catalog.ListTaxProfilesAsync(new CatalogUserIdentity(actor.UserId, actor.TenantId,
            actor.BusinessId, actor.Permissions), false, ct);
        await Task.WhenAll(concepts, taxes);
        return new(await concepts, await taxes);
    }

    public Task<InvoiceChargePage> ListAsync(InvoiceChargeActor actor, int page, int pageSize,
        string? search, bool includeInactive, CancellationToken ct = default)
    {
        Demand(actor, InvoiceChargePermissions.Read);
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100 || search?.Length > 120)
            throw new InvoiceChargeValidationException("La paginación o búsqueda no es válida.");
        return store.ListAsync(actor, page, pageSize, search?.Trim(), includeInactive, ct);
    }

    public async Task<InvoiceChargeDefinition> SaveAsync(InvoiceChargeActor actor,
        SaveInvoiceChargeRequest request, CancellationToken ct = default)
    {
        Demand(actor, InvoiceChargePermissions.Configure);
        if (request.ChargeId == Guid.Empty || request.ExpectedVersion < 0 ||
            request.ExpenseConceptId == Guid.Empty || request.SalesTaxProfileId == Guid.Empty || request.PurchaseTaxProfileId == Guid.Empty ||
            request.SortOrder < 0 || request.SupplierIds is null || request.SupplierIds.Count is < 1 or > 100 ||
            request.SupplierIds.Any(id => id == Guid.Empty) ||
            request.SupplierIds.Distinct().Count() != request.SupplierIds.Count)
            throw new InvoiceChargeValidationException("Selecciona un concepto, un impuesto y entre 1 y 100 proveedores distintos.");
        var code = Required(request.Code, 32, "Código").ToUpperInvariant();
        if (code.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvoiceChargeValidationException("El código solo admite letras, números, guion y guion bajo.");
        var normalized = request with { Code = code, Name = Required(request.Name, 120, "Nombre") };
        try
        {
            InvoiceChargeCalculation.Validate(new(normalized.CalculationMode, normalized.Value,
                normalized.InclusionMode, normalized.InvoiceAmountLimit,
                normalized.Ranges?.Select(x => x is null ? null! : new InvoiceChargeRange(x.FromInclusive, x.ToExclusive,
                    x.CalculationMode, x.Value)).ToArray()!));
        }
        catch (InvoiceChargeRuleException error) { throw new InvoiceChargeValidationException(error.Message); }
        var result = await store.SaveAsync(actor, normalized, ct);
        await synchronization.DispatchPendingAsync(actor.TenantId, actor.BusinessId, CancellationToken.None);
        return result;
    }

    private static string Required(string? value, int limit, string label) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > limit
            ? throw new InvoiceChargeValidationException($"{label} es obligatorio y admite hasta {limit} caracteres.")
            : value.Trim();

    private static void Demand(InvoiceChargeActor actor, string permission)
    {
        if (actor.TenantId == Guid.Empty || actor.BusinessId == Guid.Empty || actor.UserId == Guid.Empty ||
            !actor.Permissions.Contains(permission))
            throw new InvoiceChargeForbiddenException("No tienes permiso para esta operación.");
    }
}

public sealed class InvoiceChargeValidationException(string message) : Exception(message);
public sealed class InvoiceChargeForbiddenException(string message) : Exception(message);
public sealed class InvoiceChargeConflictException(string message) : Exception(message);
