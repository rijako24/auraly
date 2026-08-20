using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public interface IReferenceOptionStore
{
    Task<IReadOnlyList<ReferenceOption>> ListAsync(
        string catalogCode,
        CancellationToken cancellationToken);
}

public sealed class ReferenceOptionService(IReferenceOptionStore store)
{
    public Task<IReadOnlyList<ReferenceOption>> ListAsync(
        string catalogCode,
        CancellationToken cancellationToken = default)
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

        return store.ListAsync(normalized, cancellationToken);
    }
}
