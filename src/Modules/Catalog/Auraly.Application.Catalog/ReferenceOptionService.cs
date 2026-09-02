using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public interface IReferenceOptionStore
{
    Task<IReadOnlyList<ReferenceOption>> ListAsync(
        string catalogCode,
        CancellationToken cancellationToken);

    Task<ReferenceOption> CreateAsync(
        string catalogCode,
        CreateReferenceOptionRequest request,
        CancellationToken cancellationToken);
}

public sealed class ReferenceOptionService(IReferenceOptionStore store)
{
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
                $"Permission '{CatalogPermissionCodes.Update}' is required.");

        var normalizedCatalogCode = NormalizeCatalogCode(catalogCode);
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
                "Code and label are required and the reference option values must fit their allowed lengths.");
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
                "The reference catalog code is invalid.");
        }
        return normalized;
    }
}
