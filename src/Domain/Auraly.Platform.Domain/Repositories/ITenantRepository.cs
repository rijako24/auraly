using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<Tenant?> GetByIdForCapacityUpdateAsync(Guid tenantId, CancellationToken ct = default);
    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search, CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    void Update(Tenant tenant);
    Task RevokeActiveAuthenticationSessionsAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> UpdateLegalIdentityAsync(Guid tenantId, string legalName, string identification,
        string? verificationDigit, string entityType, string identificationTypeCode,
        DateTimeOffset now, CancellationToken ct = default);
    Task<bool> UpdateLogoAsync(Guid tenantId, string logoMediaRef, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> IsReferenceOptionActiveAsync(string catalogCode, string code, CancellationToken ct = default);
    Task<IReadOnlyList<TenantFiscalCertificateExpiry>> GetFiscalCertificateExpirationsAsync(
        DateTimeOffset? expiresOnOrBefore = null, CancellationToken ct = default);
}

public sealed record TenantFiscalCertificateExpiry(
    Guid TenantId, string TenantName, DateTimeOffset ValidTo);
