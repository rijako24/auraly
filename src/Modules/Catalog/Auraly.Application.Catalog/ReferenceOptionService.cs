using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public sealed class ReferenceOptionService(IReferenceOptionStore store)
{
    public Task<IReadOnlyList<ReferenceOption>> ListContextualAsync(CatalogUserIdentity user,string catalogCode,
        DateOnly? from,DateOnly? to,CancellationToken cancellationToken=default, int dateRangeVersion=1)
    {
        if(dateRangeVersion is not (1 or 2)||from.HasValue!=to.HasValue||to<from||from==default(DateOnly)||to?.DayNumber-from?.DayNumber>1827
            || (dateRangeVersion==2 && to==DateOnly.MaxValue))
            throw new CatalogValidationException("El rango de opciones históricas no es válido.");
        return store.ListContextualAsync(user,NormalizeCatalogCode(catalogCode),from,to,cancellationToken,dateRangeVersion);
    }
    public Task<IReadOnlyList<ReferenceOption>> ListAsync(
        string catalogCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCatalogCode(catalogCode);

        return store.ListAsync(normalized, cancellationToken);
    }

    public Task<ReferenceOption> CreateAsync(
        CatalogUserIdentity user,
        string catalogCode,
        CreateReferenceOptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!user.Permissions.Contains(CatalogPermissionCodes.Update))
            throw new CatalogForbiddenException(
                $"No tienes permiso para modificar opciones del catálogo.");

        var normalizedCatalogCode = NormalizeCatalogCode(catalogCode);
        if(normalizedCatalogCode=="payment-method")
            throw new CatalogValidationException("Los tipos técnicos de pago son del sistema. Configure una forma de pago del tenant.");
        var code = request.Code?.Trim();
        var label = request.Label?.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 ||
            string.IsNullOrWhiteSpace(label) || label.Length > 160 ||
            description?.Length > 500)
        {
            throw new CatalogValidationException(
                "El código y el nombre son obligatorios; los valores del catálogo deben respetar las longitudes permitidas.");
        }

        return store.CreateAsync(
            normalizedCatalogCode,
            new CreateReferenceOptionRequest(code, label, description),
            cancellationToken);
    }

    private static string NormalizeCatalogCode(string catalogCode)
    {
        var normalized = catalogCode?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 64 ||
            normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new CatalogValidationException(
                "El código del catálogo no es válido.");
        }
        return normalized;
    }
}
