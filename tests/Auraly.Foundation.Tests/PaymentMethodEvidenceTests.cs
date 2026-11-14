using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Foundation.Tests;

public sealed class PaymentMethodEvidenceTests
{
    [Fact]
    public void Legacy_payments_do_not_require_catalog_metadata()
        => Assert.True(PaymentMethodEvidence.IsValidSet([new(null, null, null, null, null)]));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_account_or_version_without_a_form_is_not_issued_evidence(bool accountOnly)
        => Assert.False(PaymentMethodEvidence.IsValidSet([
            new(null, accountOnly ? null : 1, null, null, accountOnly ? Guid.NewGuid() : null)]));

    [Fact]
    public void Issued_forms_require_complete_distinct_frozen_identities()
    {
        var value = new PaymentMethodEvidence(Guid.NewGuid(), 1, "CASH", "Caja", Guid.NewGuid());
        Assert.True(PaymentMethodEvidence.IsValidSet([value, value with { Id = Guid.NewGuid() }]));
        Assert.False(PaymentMethodEvidence.IsValidSet([value, value]));
        Assert.False(PaymentMethodEvidence.IsValidSet([value, new(null, null, null, null, null)]));
        Assert.False(PaymentMethodEvidence.IsValidSet([value with { AccountId = null }]));
    }
}
