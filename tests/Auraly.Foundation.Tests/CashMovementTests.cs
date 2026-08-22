using Auraly.Domain.WorkSessions;

namespace Auraly.Foundation.Tests;

public sealed class CashMovementTests
{
    [Theory]
    [InlineData(CashMovementDirection.In, 25000)]
    [InlineData(CashMovementDirection.Out, -25000)]
    public void Movement_has_the_expected_drawer_sign(CashMovementDirection direction, decimal signed)
    {
        var businessId = Guid.NewGuid();
        var reason = CashMovementReasonDefinition.Create(Guid.NewGuid(), businessId, "reason",
            "Movimiento", direction, "OtherIncome", null, false, true);
        var movement = CashMovement.Create(Guid.NewGuid(), businessId, Guid.NewGuid(), reason,
            25_000m, DateTimeOffset.UtcNow, "REF-001", "Nota", null);

        Assert.Equal(signed, movement.SignedAmount);
        Assert.Equal("REASON", movement.Reason.Code);
    }

    [Fact]
    public void Required_reference_and_business_scope_are_enforced()
    {
        var businessId = Guid.NewGuid();
        var reason = CashMovementReasonDefinition.Create(Guid.NewGuid(), businessId, "bank",
            "Consignacion", CashMovementDirection.Out, "Bank", null, true, true);

        Assert.Throws<CashMovementRuleException>(() => CashMovement.Create(
            Guid.NewGuid(), businessId, Guid.NewGuid(), reason, 1m, DateTimeOffset.UtcNow,
            null, null, null));
        Assert.Throws<CashMovementRuleException>(() => CashMovement.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), reason, 1m, DateTimeOffset.UtcNow,
            "REF", null, null));
    }
}
