using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;

namespace Auraly.Foundation.Tests;

public sealed class GoodsReceiptLandedCostTests
{
    [Theory]
    [InlineData(true, 95, 285)]
    [InlineData(false, 100, 300)]
    public void Financial_discount_is_allocated_after_commercial_discount_without_changing_invoice_or_tax(
        bool anticipate, decimal firstCost, decimal secondCost)
    {
        var request = Request([
            Line(1, 1m, 100m), Line(2, 1m, 300m)
        ], []) with
        {
            FinancialDiscount = new(5m, new DateOnly(2026, 9, 20), anticipate)
        };

        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));

        Assert.Equal(400m, result.FunctionalGrandTotal);
        Assert.Equal(20m, result.FinancialDiscount!.Amount);
        Assert.Equal([5m, 15m], result.FinancialDiscount.Lines.Select(line => line.Amount));
        Assert.Equal([firstCost, secondCost],
            result.ReceiptLines.Select(line => line.RecognizedInventoryCostAmount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100.000001)]
    [InlineData(1.1234567)]
    public void Financial_discount_rejects_invalid_percentage(decimal percentage)
    {
        var request = Request([Line(1, 1m, 100m)], []) with
        {
            FinancialDiscount = new(percentage, null, false)
        };

        Assert.Throws<PurchasingValidationException>(() =>
            GoodsReceiptCostCalculator.Calculate(request, Merchandise(request)));
    }

    [Theory]
    [InlineData(PurchaseCostAllocationMethods.Value, 7.5, 22.5)]
    [InlineData(PurchaseCostAllocationMethods.Weight, 20, 10)]
    public void Existing_voucher_partial_cost_uses_the_same_allocation_and_does_not_change_the_invoice(
        string method, decimal first, decimal second)
    {
        var request = Request([Line(1, 1m, 100m) with { TotalGrossWeightKg = 2m },
            Line(2, 1m, 300m) with { TotalGrossWeightKg = 1m }], []) with
        {
            ReferencedCostDocuments = [new(Guid.NewGuid(), Guid.NewGuid(),
                GoodsReceiptCostAssociationModes.Distribute, [new(1, 30m, method)])]
        };
        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));
        Assert.Equal(400m, result.FunctionalGrandTotal);
        Assert.Empty(result.AdditionalDocuments);
        Assert.Equal(first, result.ReceiptLines[0].AllocatedLandedCostAmount);
        Assert.Equal(second, result.ReceiptLines[1].AllocatedLandedCostAmount);
        Assert.Equal(30m, result.ReferencedDocuments!.Single().Allocations.Sum(line => line.FunctionalAmount));
    }

    [Fact]
    public void Later_voucher_allocation_uses_the_frozen_receipt_weights_and_preserves_the_original_invoice()
    {
        var request = Request([Line(1, 1m, 100m) with { TotalGrossWeightKg = 2m },
            Line(2, 1m, 300m) with { TotalGrossWeightKg = 1m }], []);
        var original = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));
        var voucher = new GoodsReceiptVoucherCostRequest(Guid.NewGuid(), Guid.NewGuid(),
            GoodsReceiptCostAssociationModes.Distribute,
            [new(1, 30m, PurchaseCostAllocationMethods.Weight)]);

        var allocation = GoodsReceiptCostCalculator.CalculateReferenced([voucher], original.ReceiptLines);

        Assert.Equal([20m, 10m], allocation.Single().Allocations
            .OrderBy(line => line.ReceiptLineNumber).Select(line => line.FunctionalAmount));
        Assert.Equal(400m, original.FunctionalGrandTotal);
        Assert.All(original.ReceiptLines, line => Assert.Equal(0m, line.AllocatedLandedCostAmount));
    }

    [Fact]
    public void Informative_association_does_not_distribute_and_cannot_hide_a_cost_amount()
    {
        var association = new GoodsReceiptVoucherCostRequest(Guid.NewGuid(), Guid.NewGuid(),
            GoodsReceiptCostAssociationModes.Associate, []);
        var request = Request([Line(1, 1m, 100m)], []) with { ReferencedCostDocuments = [association] };
        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));
        Assert.Equal(100m, result.ReceiptLines.Single().RecognizedInventoryCostAmount);
        Assert.Empty(result.ReferencedDocuments!.Single().Allocations);
        var invalid = request with { ReferencedCostDocuments = [association with { Lines = [new(1, 30m, "Value")] }] };
        Assert.Throws<PurchasingValidationException>(() => GoodsReceiptCostCalculator.Calculate(invalid, Merchandise(invalid)));
    }

    [Fact]
    public void Captured_invoice_and_reference_cannot_share_the_same_cost_header_identity()
    {
        var invoice = CostDocument([new(1, PurchaseCostKinds.Freight, "Flete", 10m,
            0m, "00", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
            PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)]);
        var request = Request([Line(1, 1m, 100m)], [invoice]) with
        {
            ReferencedCostDocuments = [new(invoice.CostDocumentId, Guid.NewGuid(),
                GoodsReceiptCostAssociationModes.Associate, [])]
        };
        Assert.Throws<PurchasingValidationException>(() => GoodsReceiptCostCalculator.Calculate(request, Merchandise(request)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Manual_reference_allocation_rejects_missing_items_or_unpersistable_precision(bool missingItem)
    {
        var request = Request([Line(1, 1m, 100m), Line(2, 1m, 100m)], []) with
        {
            ReferencedCostDocuments = [new(Guid.NewGuid(), Guid.NewGuid(),
                GoodsReceiptCostAssociationModes.Distribute, [new(1, 1m, PurchaseCostAllocationMethods.Manual,
                    missingItem ? [null!, new(2, 1m)] : [new(1, .50001m), new(2, .49999m)])])]
        };
        Assert.Throws<PurchasingValidationException>(() => GoodsReceiptCostCalculator.Calculate(request, Merchandise(request)));
    }

    [Fact]
    public void Referenced_cost_rounding_conserves_the_chosen_amount_across_all_products()
    {
        var request = Request([Line(1, 1m, 100m), Line(2, 1m, 100m), Line(3, 1m, 100m)], []) with
        {
            ReferencedCostDocuments = [new(Guid.NewGuid(), Guid.NewGuid(),
                GoodsReceiptCostAssociationModes.Distribute, [new(1, 1m, "Equal")])]
        };
        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));
        Assert.Equal(1m, result.ReceiptLines.Sum(line => line.AllocatedLandedCostAmount));
        Assert.All(result.ReceiptLines, line => Assert.InRange(line.AllocatedLandedCostAmount, .3333m, .3334m));
    }

    [Fact]
    public void Freight_is_allocated_by_value_and_its_deductible_vat_is_not_capitalized()
    {
        var request = Request([
            Line(1, 1m, 100m), Line(2, 1m, 300m)
        ], [CostDocument([
            new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.Freight, "Flete", 40m,
                40m, "01", 19m, 7.6m, PurchasingTaxTreatments.DeductibleInputVat,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Value)
        ])]);

        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));

        Assert.Equal(10m, result.ReceiptLines[0].AllocatedLandedCostAmount);
        Assert.Equal(30m, result.ReceiptLines[1].AllocatedLandedCostAmount);
        Assert.Equal(110m, result.ReceiptLines[0].RecognizedInventoryCostAmount);
        Assert.Equal(330m, result.ReceiptLines[1].RecognizedInventoryCostAmount);
        Assert.Equal(7.6m, result.AdditionalDocuments.Single().FunctionalTaxAmount);
    }

    [Fact]
    public void Import_declaration_capitalizes_duty_but_keeps_import_vat_separate()
    {
        var declaration = CostDocument([
            new GoodsReceiptCostLineRequest(1, PurchaseCostKinds.CustomsDuty, "Arancel", 20m,
                0m, "00", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
                PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Quantity),
            new GoodsReceiptCostLineRequest(2, PurchaseCostKinds.ImportVat, "IVA importación", 0m,
                420m, "01", 19m, 79.8m, PurchasingTaxTreatments.DeductibleInputVat,
                PurchaseCostTreatments.Expense, PurchaseCostAllocationMethods.None)
        ]) with { PurchaseEvidenceType = PurchaseEvidenceTypes.ImportDeclaration };
        var request = Request([Line(1, 2m, 100m)], [declaration]);

        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));

        Assert.Equal(20m, result.ReceiptLines.Single().AllocatedLandedCostAmount);
        Assert.Equal(220m, result.ReceiptLines.Single().RecognizedInventoryCostAmount);
        Assert.Equal(79.8m, result.AdditionalDocuments.Single().FunctionalTaxAmount);
    }

    [Fact]
    public void Foreign_merchandise_is_recognized_in_functional_currency_at_document_rate()
    {
        var request = Request([Line(1, 1m, 2m)], []) with
        {
            CurrencyCode = "USD", ExchangeRate = 4_000m,
            ExchangeRateDate = new DateOnly(2026, 9, 1), ExchangeRateSource = "TRM"
        };

        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));

        Assert.Equal(8_000m, result.FunctionalGrandTotal);
        Assert.Equal(8_000m, result.ReceiptLines.Single().RecognizedInventoryCostAmount);
    }

    [Fact]
    public void Manual_allocation_must_reconcile_and_is_applied_exactly_as_entered()
    {
        var manualLine = new GoodsReceiptCostLineRequest(
            1, PurchaseCostKinds.OtherDirectCost, "Costo directo", 30m,
            30m, "00", 0m, 0m, PurchasingTaxTreatments.NotApplicable,
            PurchaseCostTreatments.Capitalize, PurchaseCostAllocationMethods.Manual,
            [1, 2],
            [new GoodsReceiptCostManualAllocationRequest(1, 12m),
             new GoodsReceiptCostManualAllocationRequest(2, 18m)]);
        var request = Request([Line(1, 1m, 100m), Line(2, 1m, 100m)],
            [CostDocument([manualLine])]);

        var result = GoodsReceiptCostCalculator.Calculate(request, Merchandise(request));

        Assert.Equal(12m, result.ReceiptLines[0].AllocatedLandedCostAmount);
        Assert.Equal(18m, result.ReceiptLines[1].AllocatedLandedCostAmount);

        var invalid = request with
        {
            AdditionalCostDocuments =
            [CostDocument([manualLine with
            {
                ManualAllocations =
                [new GoodsReceiptCostManualAllocationRequest(1, 12m),
                 new GoodsReceiptCostManualAllocationRequest(2, 17m)]
            }])]
        };
        Assert.Throws<PurchasingValidationException>(
            () => GoodsReceiptCostCalculator.Calculate(invalid, Merchandise(invalid)));
    }

    private static ConfirmGoodsReceiptRequest Request(
        IReadOnlyCollection<GoodsReceiptLineRequest> lines,
        IReadOnlyCollection<GoodsReceiptCostDocumentRequest> costs) => new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "INV-1",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), true,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "COP", null,
            lines, AdditionalCostDocuments: costs);

    private static GoodsReceiptLineRequest Line(int number, decimal quantity, decimal unitCost) =>
        new(number, Guid.NewGuid(), $"Producto {number}", quantity, unitCost, 0m,
            "00", 0m, PurchasingTaxTreatments.NotApplicable);

    private static GoodsReceiptCostDocumentRequest CostDocument(
        IReadOnlyCollection<GoodsReceiptCostLineRequest> lines) => new(
            Guid.NewGuid(), Guid.NewGuid(), PurchaseEvidenceTypes.SupplierElectronicInvoice,
            $"COST-{Guid.NewGuid():N}", new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero),
            true, new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), "COP", 1m,
            new DateOnly(2026, 9, 2), "FunctionalCurrency", lines);

    private static GoodsReceiptCalculation Merchandise(ConfirmGoodsReceiptRequest request) =>
        GoodsReceiptCalculator.Calculate(request.Lines.Select(line => (
            line.LineNumber, line.ProductId, line.Description, line.Quantity, line.UnitCost,
            line.DiscountAmount, line.TaxCode, line.TaxRate,
            Enum.Parse<PurchaseTaxTreatment>(line.TaxTreatment))));
}
