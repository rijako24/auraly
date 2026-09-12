using Auraly.Domain.Orders;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class OrderRulesTests
{
    [Fact]
    public void Stock_review_status_is_exposed_to_the_orders_workspace()
    {
        Assert.Equal("InReview", OrderRules.CanonicalStatus(5, false));
        Assert.False(OrderRules.CanInvoice(5, true, false));
    }

    [Theory]
    [InlineData("Completed", "Completed", "Invoiced")]
    [InlineData("Received", "Pending", "ProcessingEmission")]
    [InlineData("Received", "DeadLettered", "EmissionFailed")]
    public void Linked_order_only_reports_invoiced_after_engine_completion(
        string processingStatus,
        string jobStatus,
        string expected)
    {
        Assert.Equal(
            expected,
            OrderRules.CanonicalStatus(2, true, processingStatus, jobStatus));
    }
}
