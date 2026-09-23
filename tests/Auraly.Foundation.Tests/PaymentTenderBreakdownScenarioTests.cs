using Auraly.BuildingBlocks.Domain.Payments;

namespace Auraly.Foundation.Tests;

public sealed class PaymentTenderBreakdownScenarioTests
{
    private static readonly IReadOnlySet<string> ReceivableMethods =
        new HashSet<string>(["Cash","BankTransfer","DebitCard","CreditCard"],StringComparer.Ordinal);

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
                new[]{new PaymentTender("Cash",cash,cash),new PaymentTender("BankTransfer",transfer,Reference:$"TR-{index}")}];
        }
        for(var index=1;index<=20;index++)
        {
            var amount=index*211.10m;
            yield return [30+index,$"multi-invoice-one-method-{index}",2+(index%7),amount,
                new[]{new PaymentTender(index%2==0?"Cash":"BankTransfer",amount,
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
            [new("Cash",50m,50m),new("BankTransfer",50m,Reference:"TR")],100m,2,ReceivableMethods));

    [Fact]
    public void Rejects_a_breakdown_that_does_not_equal_allocations() =>
        Assert.Throws<ArgumentException>(()=>PaymentTenderBreakdown.Create(
            [new("Cash",99m,99m)],100m,1,ReceivableMethods));

    [Fact]
    public void Rejects_duplicate_payment_methods() =>
        Assert.Throws<ArgumentException>(()=>PaymentTenderBreakdown.Create(
            [new("Cash",50m,50m),new("Cash",50m,50m)],100m,1,ReceivableMethods));
}
