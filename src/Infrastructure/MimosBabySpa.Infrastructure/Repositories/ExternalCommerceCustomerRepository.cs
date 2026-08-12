using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;
using MimosBabySpa.Infrastructure.Commerce;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ExternalCommerceCustomerRepository : IExternalCommerceCustomerRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ExternalCustomerReconciliationCommitState _commitState;
    private readonly IAuralyIdGenerator _ids;

    public ExternalCommerceCustomerRepository(
        ApplicationDbContext context,
        ExternalCustomerReconciliationCommitState commitState,
        IAuralyIdGenerator ids)
    {
        _context = context;
        _commitState = commitState;
        _ids = ids;
    }

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
        StageMessage(customer, DateTimeOffset.UtcNow);
        return Task.FromResult(customer);
    }

    public async Task<ExternalCommerceCustomer> UpdateAsync(
        ExternalCommerceCustomer customer,
        CancellationToken ct = default)
    {
        _context.ExternalCommerceCustomers.Update(customer);
        if (string.Equals(customer.ReconciliationStatus, "Linked", StringComparison.Ordinal))
            return customer;

        customer.ReconciliationStatus = "Pending";
        customer.ReconciliationError = null;
        customer.ReconciledAt = null;
        customer.ReconciledBy = null;
        customer.ReconciliationOrigin = null;
        var hasPendingMessage = _context.ExternalCustomerReconciliationOutboxMessages.Local
            .Any(message =>
                message.ExternalCommerceCustomerId == customer.ExternalCommerceCustomerId &&
                message.PublishedAt == null) ||
            await _context.ExternalCustomerReconciliationOutboxMessages.AnyAsync(
                message =>
                    message.ExternalCommerceCustomerId == customer.ExternalCommerceCustomerId &&
                    message.PublishedAt == null,
                ct);
        if (!hasPendingMessage)
            StageMessage(customer, DateTimeOffset.UtcNow);
        return customer;
    }

    private void StageMessage(
        ExternalCommerceCustomer customer,
        DateTimeOffset occurredAt)
    {
        _context.ExternalCustomerReconciliationOutboxMessages.Add(
            new ExternalCustomerReconciliationOutboxMessage
            {
                MessageId = _ids.NewId(),
                ExternalCommerceCustomerId = customer.ExternalCommerceCustomerId,
                BusinessId = customer.BusinessId,
                OccurredAt = occurredAt,
                AvailableAt = occurredAt
            });
        _commitState.MarkPending();
    }
}
