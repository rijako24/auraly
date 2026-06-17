namespace MimosBabySpa.Application.Services;

public interface ICustomerMemoryService
{
    Task<IReadOnlyList<CustomerMemoryFactRecord>> GetAllRecordsAsync(
        Guid businessId, string userNumber, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        Guid businessId, string userNumber, CancellationToken ct = default);

    Task<string?> GetAsync(
        Guid businessId, string userNumber, string key, CancellationToken ct = default);

    Task RememberAsync(
        Guid businessId, string userNumber, string key, string value, CancellationToken ct = default);
}

public sealed record CustomerMemoryFactRecord(
    string Key,
    string Value,
    DateTime UpdatedAt);
