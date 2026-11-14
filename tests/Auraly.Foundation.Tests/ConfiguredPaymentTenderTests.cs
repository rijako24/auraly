using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Foundation.Tests;

public sealed class ConfiguredPaymentTenderTests
{
    [Fact]
    public void Two_bank_forms_of_the_same_type_can_settle_multiple_invoices()
    {
        var values = new[] { Transfer(Guid.NewGuid(), 30), Transfer(Guid.NewGuid(), 70) };
        var result = PaymentTenderBreakdown.Create(values, 100, 2, PortfolioPaymentMethods.Supported);
        Assert.Equal(2, result.Tenders.Count);
        Assert.Equal(100m, result.TotalAmount);
    }

    [Fact]
    public void The_same_form_cannot_be_counted_twice()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => PaymentTenderBreakdown.Create(
            [Transfer(id, 30),Transfer(id, 70)],100,2,PortfolioPaymentMethods.Supported));
    }

    [Fact]
    public void Configured_and_unidentified_tenders_cannot_be_mixed()
    {
        Assert.Throws<ArgumentException>(() => PaymentTenderBreakdown.Create(
            [Transfer(Guid.NewGuid(),30),new("Cash",70)],100,1,PortfolioPaymentMethods.Supported));
    }

    [Fact]
    public void Configured_portfolio_cash_cannot_silently_turn_an_excess_into_change()
    {
        Assert.Throws<ArgumentException>(() => PaymentTenderBreakdown.Create(
            [new("Cash",100,TenderedAmount:110,PaymentMethodId:Guid.NewGuid(),PaymentMethodVersion:1)],
            100,1,PortfolioPaymentMethods.Supported));
    }

    [Fact]
    public void Legacy_tenders_keep_their_serialized_hash_input()
    {
        var value = new PaymentTender("Cash",100);
        var previousShape = new { value.MethodCode,value.Amount,value.TenderedAmount,value.BankAccountId,
            value.Reference,value.Notes,value.CardFranchiseCode,value.ApprovalNumber };
        Assert.Equal(JsonSerializer.Serialize(previousShape),JsonSerializer.Serialize(value));
    }

    [Fact]
    public void The_existing_legacy_cash_contract_is_preserved_outside_administrative_capture()
    {
        var result = PaymentTenderBreakdown.Create([new("Cash", 100, TenderedAmount: 110)], 100, 1, PortfolioPaymentMethods.Supported);
        Assert.Equal(100m, result.TotalAmount);
        Assert.Equal(110m, Assert.Single(result.Tenders).TenderedAmount);
    }

    [Fact]
    public void Legacy_manual_payment_evidence_and_effects_keep_their_serialized_hash_input()
    {
        var evidence = new VoucherPaymentEvidence("VISA", "APPROVAL", Notes: "Captura anterior");
        Assert.Equal(JsonSerializer.Serialize(new { evidence.CardFranchiseCode, evidence.ApprovalNumber,
            evidence.TenderedAmount, evidence.Notes }), JsonSerializer.Serialize(evidence));
        var tender = new VoucherFinancialTender(Guid.NewGuid(), Guid.NewGuid(), null, "CreditCard", null,
            100m, null, "VISA", "APPROVAL", null, "Captura anterior");
        Assert.Equal(JsonSerializer.Serialize(new { tender.SourceLineId, tender.AccountId, tender.PartyId,
            tender.MethodCode, tender.BankAccountId, tender.Amount, tender.Reference, tender.CardFranchiseCode,
            tender.ApprovalNumber, tender.TenderedAmount, tender.Notes }), JsonSerializer.Serialize(tender));
    }

    private static PaymentTender Transfer(Guid id,decimal amount) => new("Transfer",amount,
        Reference:"REF",PaymentMethodId:id,PaymentMethodVersion:1);
}
