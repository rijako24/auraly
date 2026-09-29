using System.Text.Json;
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

    public async Task<IReadOnlyList<ExternalCommerceCustomer>> GetByExternalKeysAsync(
        Guid businessId, Guid integrationConnectionId,
        IReadOnlyCollection<ExternalCommerceCustomerKey> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0) return [];
        var keysJson = JsonSerializer.Serialize(keys.DistinctBy(key => new ExternalCommerceCustomerKey(
            key.ExternalAccountId.ToUpperInvariant(), key.ExternalCustomerId.ToUpperInvariant())));
        return await _context.ExternalCommerceCustomers.FromSqlInterpolated($"""
            SELECT customer.*
            FROM dbo.ExternalCommerceCustomers customer
            JOIN OPENJSON({keysJson}) WITH (
              ExternalAccountId NVARCHAR(150) '$.ExternalAccountId',
              ExternalCustomerId NVARCHAR(150) '$.ExternalCustomerId') requested
              ON requested.ExternalAccountId=customer.ExternalAccountId
             AND requested.ExternalCustomerId=customer.ExternalCustomerId
            WHERE customer.BusinessId={businessId}
              AND customer.IntegrationConnectionId={integrationConnectionId}
            """).ToListAsync(ct);
    }

    public Task<ExternalCommerceCustomer> CreateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default)
    {
        ResetPendingReconciliation(customer);
        _context.ExternalCommerceCustomers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task CreateManyAsync(IReadOnlyCollection<ExternalCommerceCustomer> customers, CancellationToken ct = default)
    {
        foreach (var customer in customers)
            ResetPendingReconciliation(customer);
        _context.ExternalCommerceCustomers.AddRange(customers);
        return Task.CompletedTask;
    }

    public Task<ExternalCommerceCustomer> UpdateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default)
    {
        _context.ExternalCommerceCustomers.Update(customer);
        if (string.Equals(customer.ReconciliationStatus, "Linked", StringComparison.Ordinal))
            return Task.FromResult(customer);

        ResetPendingReconciliation(customer);
        return Task.FromResult(customer);
    }

    public Task UpdateManyAsync(IReadOnlyCollection<ExternalCommerceCustomer> customers, CancellationToken ct = default)
    {
        foreach (var customer in customers)
        {
            if (string.Equals(customer.ReconciliationStatus, "Linked", StringComparison.Ordinal)) continue;
            ResetPendingReconciliation(customer);
        }
        _context.ExternalCommerceCustomers.UpdateRange(customers);
        return Task.CompletedTask;
    }

    private static void ResetPendingReconciliation(ExternalCommerceCustomer customer)
    {
        customer.ReconciliationStatus = "Pending";
        customer.ReconciliationError = null;
        customer.ReconciledAt = null;
        customer.ReconciledBy = null;
        customer.ReconciliationOrigin = null;
    }
}
