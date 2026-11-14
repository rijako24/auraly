using Auraly.Domain.Receivables;

namespace Auraly.Foundation.Tests;

public sealed class CustomerCreditPolicyTests
{
    [Theory]
    [InlineData(null, 8, true)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, false)]
    public void Overdue_limit_is_inclusive_and_optional(int? maximum, int overdue, bool allowed)
        => Assert.Equal(allowed, CustomerCreditPolicy.Evaluate(true, 1000, 100, overdue, maximum, 100).IsAllowed);

    [Fact]
    public void Monetary_limit_is_still_required_when_the_overdue_count_is_allowed()
        => Assert.False(CustomerCreditPolicy.Evaluate(true, 1000, 900, 2, 2, 101).IsAllowed);

    [Fact]
    public void Cash_sales_do_not_require_credit_eligibility()
        => Assert.True(CustomerCreditPolicy.Evaluate(false, 0, 1000, 10, 0, 0).IsAllowed);

    [Fact]
    public void A_disabled_customer_cannot_take_new_credit()
        => Assert.False(CustomerCreditPolicy.Evaluate(false, null, 0, 0, null, 1).IsAllowed);
}
