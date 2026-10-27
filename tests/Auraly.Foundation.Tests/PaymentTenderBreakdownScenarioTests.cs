using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Foundation.Tests;

public sealed class PaymentTenderBreakdownScenarioTests
{
    private static readonly IReadOnlySet<string> ReceivableMethods =
        PortfolioPaymentMethods.Supported;

    [Theory]
    [InlineData("reference", 120)]
    [InlineData("notes", 500)]
    [InlineData("franchise", 40)]
    [InlineData("approval", 80)]
    public void Capture_surfaces_share_the_payment_evidence_boundaries(string field, int maximum)
    {
        PaymentTender Tender(int length)
        {
            var value = new string('A', length);
            return new("CreditCard", 10m, Reference: field == "reference" ? value : null,
                Notes: field == "notes" ? value : null, CardFranchiseCode: field == "franchise" ? value : "Visa",
                ApprovalNumber: field == "approval" ? value : "APP-1");
        }
        Assert.Equal(10m, PaymentTenderBreakdown.Create([Tender(maximum)], 10m, 1, ReceivableMethods).TotalAmount);
        Assert.Throws<ArgumentException>(() => PaymentTenderBreakdown.Create([Tender(maximum + 1)], 10m, 1, ReceivableMethods));
    }

    public static IEnumerable<object[]> FiftyValidPaymentScenarios()
    {
        for(var index=1;index<=20;index++)
        {
            var amount=index*137.25m;
            yield return [index,$"single-cash-{index}",1,amount,
                new[]{new PaymentTender("Cash",amount,amount+50m)}];
        }
        for(var index=1;index<=10;index++)
        {
            var cash=index*91.5m;var transfer=index*53.25m;
            yield return [20+index,$"single-split-{index}",1,cash+transfer,
                new[]{new PaymentTender("Cash",cash,cash),new PaymentTender("Transfer",transfer,Reference:$"TR-{index}")}];
        }
        for(var index=1;index<=20;index++)
        {
            var amount=index*211.10m;
            yield return [30+index,$"multi-invoice-one-method-{index}",2+(index%7),amount,
                new[]{new PaymentTender(index%2==0?"Cash":"Transfer",amount,
                    index%2==0?amount:null,Reference:index%2==0?null:$"TR-M-{index}")}];
        }
    }

    [Theory]
    [MemberData(nameof(FiftyValidPaymentScenarios))]
    public void Accepts_the_fifty_supported_payment_shapes(
        int scenario,string name,int allocationCount,decimal expected,PaymentTender[] tenders)
    {
        var result=PaymentTenderBreakdown.Create(tenders,expected,allocationCount,ReceivableMethods);

        Assert.InRange(scenario,1,50);
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.Equal(expected,result.TotalAmount);
        Assert.Equal(tenders.Length,result.Tenders.Count);
    }

    [Fact]
    public void Rejects_multiple_tenders_when_more_than_one_invoice_is_paid() =>
        Assert.Throws<ArgumentException>(()=>PaymentTenderBreakdown.Create(
            [new("Cash",50m,50m),new("Transfer",50m,Reference:"TR")],100m,2,ReceivableMethods));

    [Fact]
    public void Rejects_a_breakdown_that_does_not_equal_allocations() =>
        Assert.Throws<ArgumentException>(()=>PaymentTenderBreakdown.Create(
            [new("Cash",99m,99m)],100m,1,ReceivableMethods));

    [Fact]
    public void Rejects_duplicate_payment_methods() =>
        Assert.Throws<ArgumentException>(()=>PaymentTenderBreakdown.Create(
            [new("Cash",50m,50m),new("Cash",50m,50m)],100m,1,ReceivableMethods));

    [Theory]
    [InlineData("Transfer", null, null, null)]
    [InlineData("Transfer", "  ", null, null)]
    [InlineData("DebitCard", null, null, "APP-1")]
    [InlineData("DebitCard", null, "Visa", null)]
    [InlineData("CreditCard", null, "  ", "APP-1")]
    [InlineData("CreditCard", null, "Visa", "  ")]
    public void Rejects_missing_payment_evidence_in_the_shared_contract(
        string method, string? reference, string? franchise, string? approval)
    {
        Assert.Throws<ArgumentException>(() => PaymentTenderBreakdown.Create(
            [new(method, 100m, Reference: reference, CardFranchiseCode: franchise, ApprovalNumber: approval)],
            100m, 1, ReceivableMethods));
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("DebitCard")]
    [InlineData("CreditCard")]
    public void Rejects_a_bank_dimension_unsupported_by_the_existing_payment_contract(string method)
    {
        Assert.Throws<ArgumentException>(() => PaymentTenderBreakdown.Create(
            [new(method, 100m, BankAccountId: Guid.NewGuid(), CardFranchiseCode: "Visa", ApprovalNumber: "APP-1")],
            100m, 1, ReceivableMethods));
    }

    [Theory]
    [InlineData("Transfer", " TR-1 ", null, null)]
    [InlineData("DebitCard", null, " Visa ", " APP-1 ")]
    [InlineData("CreditCard", null, " Mastercard ", " APP-1 ")]
    public void Preserves_and_normalizes_the_payment_evidence(
        string method, string? reference, string? franchise, string? approval)
    {
        var payment = Assert.Single(PaymentTenderBreakdown.Create(
            [new(method, 100m, Reference: reference, CardFranchiseCode: franchise, ApprovalNumber: approval)],
            100m, 1, ReceivableMethods).Tenders);
        Assert.Equal(reference?.Trim(), payment.Reference);
        Assert.Equal(franchise?.Trim(), payment.CardFranchiseCode);
        Assert.Equal(approval?.Trim(), payment.ApprovalNumber);
    }
}
