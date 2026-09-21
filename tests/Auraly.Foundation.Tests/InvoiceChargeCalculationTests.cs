using Auraly.Domain.Sales;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Xunit;

namespace Auraly.Foundation.Tests;

public sealed class InvoiceChargeCalculationTests
{
    [Fact]
    public void Issued_snapshot_keeps_calculated_amounts_and_validates_static_references_only()
    {
        var definition = new InvoiceChargeDefinition(Guid.NewGuid(), Guid.NewGuid(), 1,
            "DOM", "Domicilio", true, 0, "Fixed", 5000, "UpToInvoiceAmount", 80000,
            Guid.NewGuid(), "Domicilios", Guid.NewGuid(), "TEST", "Gasto", null, null,
            Guid.NewGuid(), "Impuesto", "01", 0, [],
            [new(Guid.NewGuid(), "Proveedor", "TEST", 7, true, true, ["O-15"], "BOG", "InternalReceiptVoucher")],
            Guid.NewGuid(), "Impuesto", 0);
        var original = InvoiceChargeApplication.Calculate(80000,
            new InvoiceChargeSelection(Guid.NewGuid(), definition, definition.Suppliers[0].SupplierId, null));
        var transmitted = System.Text.Json.JsonSerializer.Deserialize<AppliedInvoiceCharge>(
            System.Text.Json.JsonSerializer.Serialize(original))!;
        InvoiceChargeApplication.ValidateSnapshotReferences(80000, [transmitted], [definition]);
        Assert.Throws<InvoiceChargeValidationException>(() => InvoiceChargeApplication.ValidateSnapshot(80000, [null!]));
        var altered = new[] {
            original with { Supplier = null! },
            original with { ExpenseAccountId = Guid.NewGuid() },
            original with { CostCenterId = Guid.NewGuid() },
            original with { Supplier = original.Supplier with { AppliesWithholding = false } },
            original with { Supplier = original.Supplier with { TaxResponsibilities = [] } },
            original with { Supplier = original.Supplier with { DefaultPaymentDueDays = 99 } },
            original with { Supplier = original.Supplier with { SupplierId = Guid.NewGuid() } },
            original with { Version = 2 },
            original with { TaxRate = 19 },
        };
        foreach (var value in altered)
            Assert.Throws<InvoiceChargeValidationException>(() =>
                InvoiceChargeApplication.ValidateSnapshotReferences(80000, [value], [definition]));
        var recalculatedAmountMustNotBeRequired = original with
        {
            Amount = 6000,
            InvoicedAmount = 6000,
            InvoicedUntaxedAmount = 6000,
            SupplierUntaxedAmount = 6000
        };
        InvoiceChargeApplication.ValidateSnapshotReferences(
            80000, [recalculatedAmountMustNotBeRequired], [definition]);
        var frozenCompanyExpense = original with
        {
            InvoicedAmount = 0,
            InvoicedUntaxedAmount = 0,
            ExpenseAmount = original.Amount
        };
        InvoiceChargeApplication.ValidateSnapshotReferences(
            80000, [frozenCompanyExpense], [definition]);
        Assert.Throws<InvoiceChargeValidationException>(() =>
            InvoiceChargeApplication.ValidateSnapshotReferences(80000, [original], []));
        // Deactivating a later version does not invalidate the original offline document.
        InvoiceChargeApplication.ValidateSnapshotReferences(80000, [original],
            [definition, definition with { Version = 2, IsActive = false, Value = 7000 }]);
    }

    private static InvoiceChargeRule Delivery => new("Ranges", null, "UpToInvoiceAmount", 80000m,
        [new(0, 100000, "Fixed", 5000), new(100000, 200000, "Fixed", 6000),
         new(200000, 300000, "Fixed", 7000), new(300000, 400000, "Fixed", 8000),
         new(400000, null, "Percentage", 2)]);

    // Example tariffs are fixtures, never a tenant configuration.
    [Theory]
    [InlineData(0, 5000, 5000)]
    [InlineData(79999.99, 5000, 5000)]
    [InlineData(80000, 5000, 5000)]
    [InlineData(80000.01, 5000, 0)]
    [InlineData(99999.99, 5000, 0)]
    [InlineData(100000, 6000, 0)]
    [InlineData(199999.99, 6000, 0)]
    [InlineData(200000, 7000, 0)]
    [InlineData(299999.99, 7000, 0)]
    [InlineData(300000, 8000, 0)]
    [InlineData(399999.99, 8000, 0)]
    [InlineData(400000, 8000, 0)]
    [InlineData(500000, 10000, 0)]
    public void Tariff_is_selected_before_invoice_inclusion_and_boundaries_are_half_open(
        decimal basis, decimal amount, decimal invoicedAmount)
    {
        var result = InvoiceChargeCalculation.Calculate(Delivery, basis);
        Assert.Equal(basis, result.InvoiceBase);
        Assert.Equal(amount, result.Amount);
        Assert.Equal(invoicedAmount, result.InvoicedAmount);
        Assert.Equal(amount - invoicedAmount, result.ExpenseAmount);
    }

    [Theory]
    [InlineData("Always", 7500, 0)]
    [InlineData("Never", 0, 7500)]
    public void Invoice_inclusion_is_configurable_without_a_delivery_special_case(string policy, decimal customer, decimal company)
    {
        var rule = new InvoiceChargeRule("Fixed", 7500, policy, null, []);
        var result = InvoiceChargeCalculation.Calculate(rule, 120000);
        Assert.Equal(customer, result.InvoicedAmount);
        Assert.Equal(company, result.ExpenseAmount);
    }

    [Fact]
    public void Manual_value_uses_the_same_invoice_inclusion_rule()
    {
        var rule = Delivery with { CalculationMode = "Manual", Ranges = [] };
        Assert.Equal(8500, InvoiceChargeCalculation.Calculate(rule, 80000.01m, 8500).ExpenseAmount);
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Calculate(rule, 80000));
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Calculate(Delivery, 80000, 8500));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(1000000000000)]
    public void Invalid_base_cannot_be_silently_rounded(decimal basis) =>
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Calculate(Delivery, basis));

    public static IEnumerable<object[]> InvalidRules()
    {
        yield return [Delivery with { InclusionMode = "Other" }];
        yield return [Delivery with { InvoiceAmountLimit = null }];
        yield return [Delivery with { InvoiceAmountLimit = -1 }];
        yield return [Delivery with { InclusionMode = "Always" }];
        yield return [Delivery with { Value = 1 }];
        yield return [Delivery with { Ranges = [] }];
        yield return [Delivery with { Ranges = [null!] }];
        yield return [Delivery with { Ranges = [new(1, null, "Fixed", 5)] }];
        yield return [Delivery with { Ranges = [new(0, 100, "Fixed", 5)] }];
        yield return [Delivery with { Ranges = [new(0, 100, "Fixed", 5), new(90, null, "Fixed", 5)] }];
        yield return [Delivery with { Ranges = [new(0, 100, "Fixed", 5), new(110, null, "Fixed", 5)] }];
        yield return [Delivery with { Ranges = [new(0, null, "Percentage", 101)] }];
        yield return [Delivery with { Ranges = [new(0, null, "Fixed", -1)] }];
        yield return [Delivery with { Ranges = [new(0, null, "Unknown", 5)] }];
        yield return [Delivery with { CalculationMode = "Fixed", Value = 5 }];
        yield return [Delivery with { CalculationMode = "Unknown", Value = 5, Ranges = [] }];
    }

    [Theory, MemberData(nameof(InvalidRules))]
    public void Invalid_configuration_is_rejected_before_a_sale(InvoiceChargeRule rule) =>
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Validate(rule));

    [Fact]
    public void Partial_cash_and_credit_split_exactly_without_counting_tendered_cash()
    {
        var result = InvoiceChargeCalculation.Allocate(5000, [new(1, 15000), new(0, 45000)]);
        Assert.Equal([new InvoiceChargeAllocation(0, 3750), new InvoiceChargeAllocation(1, 1250)], result);
    }

    [Fact]
    public void Mixed_payment_rounding_is_exact_and_independent_of_input_order()
    {
        var result = InvoiceChargeCalculation.Allocate(0.01m, [new(3, 1), new(1, 1), new(2, 1)]);
        Assert.Equal([new InvoiceChargeAllocation(1, 0), new InvoiceChargeAllocation(2, 0.01m), new InvoiceChargeAllocation(3, 0)], result);
        Assert.Equal(result, InvoiceChargeCalculation.Allocate(0.01m, [new(1, 1), new(2, 1), new(3, 1)]));
    }

    [Fact]
    public void Company_assumed_charge_allocates_no_payment_income()
    {
        var charge = InvoiceChargeCalculation.Calculate(Delivery, 80000.01m);
        Assert.All(InvoiceChargeCalculation.Allocate(charge.InvoicedAmount, [new(1, 80000.01m)]), x => Assert.Equal(0, x.Amount));
    }

    [Fact]
    public void Agotados_is_manual_and_company_assumed_at_any_invoice_value()
    {
        var rule = new InvoiceChargeRule("Manual", 5000, "Never", null, []);
        foreach (var basis in new[] { 1m, 79999.99m, 80000m, 80000.01m, 500000m })
        {
            var result = InvoiceChargeCalculation.Calculate(rule, basis, 6500);
            Assert.Equal(6500, result.ExpenseAmount);
            Assert.Equal(0, result.InvoicedAmount);
        }
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Calculate(rule, 80000));
    }

    [Fact]
    public void Invalid_payment_distribution_is_rejected()
    {
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Allocate(5, []));
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Allocate(5, [new(1, 4)]));
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Allocate(5, [new(1, 3), new(1, 3)]));
        Assert.Throws<InvoiceChargeRuleException>(() => InvoiceChargeCalculation.Allocate(5, [new(1, -3)]));
    }
}
