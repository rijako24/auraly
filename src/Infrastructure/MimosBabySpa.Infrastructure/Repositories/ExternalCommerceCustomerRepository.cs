using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ExternalCommerceCustomerRepository : IExternalCommerceCustomerRepository
{
    private readonly ApplicationDbContext _context;

    public ExternalCommerceCustomerRepository(ApplicationDbContext context) => _context = context;

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
        _context.ExternalCommerceCustomers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task<ExternalCommerceCustomer> UpdateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default)
    {
        _context.ExternalCommerceCustomers.Update(customer);
        return Task.FromResult(customer);
    }
}
