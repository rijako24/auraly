namespace MimosBabySpa.Application.Services;

public interface ILeadService
{
    Task<Domain.Entities.Lead> GetOrCreateLeadAsync(Guid businessId, string userNumber, string? customerName = null);
    Task UpdateLeadAsync(Guid leadId, string? status = null, string? notes = null);
    Task<Domain.Entities.Lead?> GetLeadByBusinessIdAndUserNumberAsync(Guid businessId, string userNumber);
    Task SyncCustomerIdentityAsync(
        Guid businessId,
        string userNumber,
        string? customerName = null,
        string? customerEmail = null,
        CancellationToken ct = default);
}
