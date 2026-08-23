using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class ExternalCommerceCustomerRepository : IExternalCommerceCustomerRepository
{
    private readonly ApplicationDbContext _context;
    public ExternalCommerceCustomerRepository(ApplicationDbContext context) =>
        _context = context;

    public async Task<IReadOnlyList<ExternalCommerceCustomer>> FindActiveByPhoneAsync(
        Guid businessId,
        Guid integrationConnectionId,
        string phoneNormalized,
        CancellationToken ct = default) =>
        await _context.ExternalCommerceCustomers
            .Where(customer =>
                customer.BusinessId == businessId
                && customer.IntegrationConnectionId == integrationConnectionId
                && customer.PhoneNormalized == phoneNormalized
                && customer.IsActive)
            .OrderBy(customer => customer.ExternalAccountId)
            .ThenBy(customer => customer.ExternalCustomerId)
            .ToListAsync(ct);

    public Task<ExternalCommerceCustomer?> GetByExternalKeysAsync(
        Guid businessId,
        Guid integrationConnectionId,
        string externalAccountId,
        string externalCustomerId,
        CancellationToken ct = default) =>
        _context.ExternalCommerceCustomers.FirstOrDefaultAsync(customer =>
            customer.BusinessId == businessId
            && customer.IntegrationConnectionId == integrationConnectionId
            && customer.ExternalAccountId == externalAccountId
            && customer.ExternalCustomerId == externalCustomerId,
            ct);

    public Task<ExternalCommerceCustomer> CreateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default)
    {
        customer.ReconciliationStatus = "Pending";
        customer.ReconciliationError = null;
        customer.ReconciledAt = null;
        customer.ReconciledBy = null;
        customer.ReconciliationOrigin = null;
        _context.ExternalCommerceCustomers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task<ExternalCommerceCustomer> UpdateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default)
    {
        _context.ExternalCommerceCustomers.Update(customer);
        if (string.Equals(customer.ReconciliationStatus, "Linked", StringComparison.Ordinal))
            return Task.FromResult(customer);

        customer.ReconciliationStatus = "Pending";
        customer.ReconciliationError = null;
        customer.ReconciledAt = null;
        customer.ReconciledBy = null;
        customer.ReconciliationOrigin = null;
        return Task.FromResult(customer);
    }
}
