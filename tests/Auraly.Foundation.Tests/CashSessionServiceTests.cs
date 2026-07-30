using Auraly.Application.Cash;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Cash;
using Auraly.Domain.Cash;

namespace Auraly.Foundation.Tests;

public sealed class CashSessionServiceTests
{
    [Fact]
    public async Task Sales_user_can_enter_an_open_register_without_a_cash_open_permission()
    {
        var actor = Actor(CommercePermissionCodes.SalesCreate);
        var store = new RecordingCashStore(actor.UserId);
        var service = new CashSessionService(store);

        var result = await service.OpenOrResumeAsync(
            actor,
            store.RegisterId,
            new OpenCashSessionRequest(
                store.BusinessId,
                store.LocationId,
                0m,
                "login-one"));

        Assert.Equal(actor.UserId, result.ResponsibleUserId);
        Assert.Equal(1, store.OpenCalls);
    }

    [Fact]
    public async Task Handoff_is_optional_but_requires_a_one_action_supervisor_grant()
    {
        var actor = Actor(CommercePermissionCodes.SalesCreate);
        var store = new RecordingCashStore(actor.UserId);
        var service = new CashSessionService(store);
        var request = new HandoffCashRequest(
            Guid.NewGuid(),
            [new CashCountLineInput("Cash", 0m)],
            null,
            null,
            string.Empty,
            "handoff-one");

        await Assert.ThrowsAsync<CashValidationException>(
            () => service.HandoffAsync(actor, store.RegisterId, request));
        Assert.Equal(0, store.HandoffCalls);
    }

    [Fact]
    public async Task Cashier_requests_handoff_but_does_not_receive_supervisor_permission()
    {
        var actor = Actor(CommercePermissionCodes.SalesCreate);
        var store = new RecordingCashStore(actor.UserId);
        var service = new CashSessionService(store);

        await service.HandoffAsync(
            actor,
            store.RegisterId,
            new HandoffCashRequest(
                Guid.NewGuid(),
                [new CashCountLineInput("Cash", 0m)],
                null,
                null,
                "short-lived-grant",
                "handoff-two"));

        Assert.Equal(1, store.HandoffCalls);
        Assert.DoesNotContain(
            CommercePermissionCodes.CashHandoffApprove,
            actor.Permissions);
    }

    [Fact]
    public void Reconciliation_groups_expected_and_counted_methods_without_losing_differences()
    {
        var result = CashReconciliation.Calculate(
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cash"] = 100_000m,
                ["Card"] = 25_000m
            },
            [
                new CashCountLineInput("cash", 99_500m),
                new CashCountLineInput("Transfer", 10_000m)
            ]);

        Assert.Collection(
            result,
            card =>
            {
                Assert.Equal("Card", card.PaymentMethodCode);
                Assert.Equal(-25_000m, card.DifferenceAmount);
            },
            cash =>
            {
                Assert.Equal("Cash", cash.PaymentMethodCode);
                Assert.Equal(-500m, cash.DifferenceAmount);
            },
            transfer =>
            {
                Assert.Equal("Transfer", transfer.PaymentMethodCode);
                Assert.Equal(10_000m, transfer.DifferenceAmount);
            });
    }

    private static CashUserIdentity Actor(params string[] permissions) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            permissions.ToHashSet(StringComparer.Ordinal));

    private sealed class RecordingCashStore(Guid userId) : ICashSessionStore
    {
        public Guid BusinessId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Guid RegisterId { get; } = Guid.NewGuid();
        public int OpenCalls { get; private set; }
        public int HandoffCalls { get; private set; }

        public Task<CashSessionView> OpenOrResumeAsync(
            CashUserIdentity actor,
            Guid registerId,
            OpenCashSessionRequest request,
            CancellationToken ct)
        {
            OpenCalls++;
            return Task.FromResult(Session(actor.UserId));
        }

        public Task<CashHandoffResult> HandoffAsync(
            CashUserIdentity actor,
            Guid registerId,
            HandoffCashRequest request,
            CancellationToken ct)
        {
            HandoffCalls++;
            return Task.FromResult(
                new CashHandoffResult(Guid.NewGuid(), Session(request.ReceivedByUserId), []));
        }

        public Task<CashSessionView?> CurrentAsync(
            CashUserIdentity actor, Guid registerId, CancellationToken ct) =>
            Task.FromResult<CashSessionView?>(Session(userId));

        public Task<CashClosureReceipt> CloseAsync(
            CashUserIdentity actor,
            Guid registerId,
            CloseCashSessionRequest request,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CashClosureReceipt?> ReceiptAsync(
            CashUserIdentity actor, Guid cashCountId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CashDailySummary> DailyAsync(
            CashUserIdentity actor,
            Guid registerId,
            DateOnly businessDate,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SupervisorAuthorizationGrant> AuthorizeHandoffAsync(
            CashUserIdentity actor,
            Guid registerId,
            SupervisorAuthorizationRequest request,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ProvisionSupervisorCredentialResult> ProvisionSupervisorCredentialAsync(
            CashUserIdentity actor,
            ProvisionSupervisorCredentialRequest request,
            CancellationToken ct) =>
            throw new NotSupportedException();

        private CashSessionView Session(Guid responsibleUserId) =>
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                BusinessId,
                LocationId,
                RegisterId,
                responsibleUserId,
                "Usuario",
                DateTimeOffset.Parse("2026-07-29T08:00:00-05:00"),
                DateTimeOffset.Parse("2026-07-29T08:00:00-05:00"),
                0m,
                CashSessionStatuses.Open);
    }
}
