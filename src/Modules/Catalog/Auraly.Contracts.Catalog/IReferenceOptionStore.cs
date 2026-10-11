namespace Auraly.Contracts.Catalog;

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
