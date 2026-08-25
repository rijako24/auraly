using Auraly.Domain.Orders;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class OrderRulesTests
{
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
