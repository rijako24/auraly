using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public sealed class CustomerMemoryService : ICustomerMemoryService
{
    private readonly IUnitOfWork _unitOfWork;

    public CustomerMemoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        Guid businessId, string userNumber, CancellationToken ct = default)
    {
        var rows = await GetAllRecordsAsync(businessId, userNumber, ct);
        return rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<CustomerMemoryFactRecord>> GetAllRecordsAsync(
        Guid businessId, string userNumber, CancellationToken ct = default)
    {
        var rows = await _unitOfWork.CustomerMemory.GetByBusinessAndUserNumberAsync(businessId, userNumber, ct);
        return rows
            .Select(r => new CustomerMemoryFactRecord(r.Field, r.Value, r.UpdatedAt))
            .ToList();
    }

    public async Task<string?> GetAsync(
        Guid businessId, string userNumber, string key, CancellationToken ct = default)
    {
        var all = await GetAllAsync(businessId, userNumber, ct);
        return all.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    public async Task RememberAsync(
        Guid businessId, string userNumber, string key, string value, CancellationToken ct = default)
    {
        await _unitOfWork.CustomerMemory.UpsertAsync(businessId, userNumber, key, value, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
