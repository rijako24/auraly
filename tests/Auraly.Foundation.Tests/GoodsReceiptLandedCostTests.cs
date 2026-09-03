using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;

namespace Auraly.Foundation.Tests;

public sealed class GoodsReceiptLandedCostTests
{
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
