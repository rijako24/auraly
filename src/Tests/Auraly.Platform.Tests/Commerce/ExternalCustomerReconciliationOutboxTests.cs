using Xunit;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Commerce;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Repositories;

namespace Auraly.Platform.Tests.Commerce;

public sealed class ExternalCustomerReconciliationOutboxTests
{
    [Fact]
    public async Task Creating_external_customer_commits_source_and_outbox_before_waking_dispatcher()
    {
        await using var context = CreateContext();
        var state = new ExternalCustomerReconciliationCommitState();
        var signal = new ExternalCustomerReconciliationOutboxSignal();
        using var unitOfWork = new UnitOfWork(
            context, state, signal, new Uuid7AuralyIdGenerator(TimeProvider.System));
        var customer = Customer();

        await unitOfWork.ExternalCommerceCustomers.CreateAsync(customer);

        Assert.Equal(0, await context.ExternalCommerceCustomers.CountAsync());
        Assert.Equal(0, await context.ExternalCustomerReconciliationOutboxMessages.CountAsync());
        using (var beforeCommit = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await signal.WaitAsync(beforeCommit.Token));

        await unitOfWork.SaveChangesAsync();

        Assert.Equal(1, await context.ExternalCommerceCustomers.CountAsync());
        var message = Assert.Single(
            await context.ExternalCustomerReconciliationOutboxMessages.ToListAsync());
        Assert.Equal(customer.ExternalCommerceCustomerId, message.ExternalCommerceCustomerId);
        Assert.Equal(customer.BusinessId, message.BusinessId);
        Assert.Equal(7, message.MessageId.ToByteArray(bigEndian: true)[6] >> 4);
        Assert.Null(message.PublishedAt);
        using var afterCommit = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await signal.WaitAsync(afterCommit.Token);
    }

    [Fact]
    public async Task Pending_update_coalesces_unpublished_message_and_linked_update_does_not_requeue()
    {
        await using var context = CreateContext();
        var state = new ExternalCustomerReconciliationCommitState();
        var signal = new ExternalCustomerReconciliationOutboxSignal();
        using var unitOfWork = new UnitOfWork(
            context, state, signal, new Uuid7AuralyIdGenerator(TimeProvider.System));
        var customer = Customer();
        await unitOfWork.ExternalCommerceCustomers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        using (var committed = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            await signal.WaitAsync(committed.Token);

        var firstMessage = await context.ExternalCustomerReconciliationOutboxMessages.SingleAsync();
        firstMessage.PublishedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync();

        customer.ReconciliationStatus = "Conflict";
        await unitOfWork.ExternalCommerceCustomers.UpdateAsync(customer);
        await unitOfWork.ExternalCommerceCustomers.UpdateAsync(customer);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(
            1,
            await context.ExternalCustomerReconciliationOutboxMessages.CountAsync(
                message => message.PublishedAt == null));
        using (var updateCommitted = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            await signal.WaitAsync(updateCommitted.Token);

        var pending = await context.ExternalCustomerReconciliationOutboxMessages
            .SingleAsync(message => message.PublishedAt == null);
        pending.PublishedAt = DateTimeOffset.UtcNow;
        customer.ReconciliationStatus = "Linked";
        await unitOfWork.ExternalCommerceCustomers.UpdateAsync(customer);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal(
            0,
            await context.ExternalCustomerReconciliationOutboxMessages.CountAsync(
                message => message.PublishedAt == null));
        using var noSignal = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await signal.WaitAsync(noSignal.Token));
    }

    [Fact]
    public async Task Delayed_wait_does_not_consume_the_next_commit_notification()
    {
        var signal = new ExternalCustomerReconciliationOutboxSignal();

        await signal.WaitOrDelayAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None);
        signal.Notify();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await signal.WaitAsync(cancellation.Token);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ExternalCommerceCustomer Customer() => new()
    {
        ExternalCommerceCustomerId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        IntegrationConnectionId = Guid.NewGuid(),
        ExternalAccountId = Guid.NewGuid().ToString("N"),
        ExternalCustomerId = Guid.NewGuid().ToString("N"),
        Name = "Cliente del adaptador",
        Phone = "3005550950",
        PhoneNormalized = "3005550950",
        IsActive = true,
        LastSyncedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };
}
