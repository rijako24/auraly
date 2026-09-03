using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;

namespace Auraly.Application.Purchasing;

public sealed record CalculatedGoodsReceiptCostDocument(
    GoodsReceiptCostDocumentRequest Request,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    decimal FunctionalNetAmount,
    decimal FunctionalTaxAmount,
    decimal FunctionalGrandTotal,
    IReadOnlyList<GoodsReceiptCostLineSnapshot> Lines);

public sealed record GoodsReceiptCostCalculation(
    decimal ExchangeRate,
    DateOnly ExchangeRateDate,
    string ExchangeRateSource,
    decimal FunctionalNetAmount,
    decimal FunctionalTaxAmount,
    decimal FunctionalGrandTotal,
    IReadOnlyList<GoodsReceiptLineSnapshot> ReceiptLines,
    IReadOnlyList<CalculatedGoodsReceiptCostDocument> AdditionalDocuments);

public static class GoodsReceiptCostCalculator
{
    public static GoodsReceiptCostCalculation Calculate(
        ConfirmGoodsReceiptRequest request,
        GoodsReceiptCalculation merchandise)
    {
        var exchange = ValidateExchange(
            request.CurrencyCode, request.ExchangeRate, request.ExchangeRateDate,
            request.ExchangeRateSource, request.SupplierInvoiceDate ?? request.ReceivedAt);
        var requestLines = request.Lines.ToDictionary(line => line.LineNumber);
        var allocated = merchandise.Lines.ToDictionary(line => line.LineNumber, _ => 0m);
        var documents = new List<CalculatedGoodsReceiptCostDocument>();

        foreach (var document in request.AdditionalCostDocuments ?? [])
        {
            ValidateDocument(document);
            var documentExchange = ValidateExchange(
                document.CurrencyCode, document.ExchangeRate, document.ExchangeRateDate,
                document.ExchangeRateSource, document.IssuedAt);
            var calculatedLines = new List<GoodsReceiptCostLineSnapshot>();
            foreach (var line in document.Lines.OrderBy(value => value.LineNumber))
            {
                ValidateCostLine(document, line);
                var functionalAmount = Money(line.Amount * documentExchange.Rate);
                var functionalBase = Money(line.TaxableBaseAmount * documentExchange.Rate);
                var functionalTax = Money(line.TaxAmount * documentExchange.Rate);
                var capitalizable = line.CostTreatment == PurchaseCostTreatments.Capitalize
                    ? functionalAmount + (line.TaxTreatment == PurchasingTaxTreatments.CapitalizedCost
                        ? functionalTax : 0m)
                    : 0m;
                var allocations = Allocate(
                    line, capitalizable, merchandise.Lines, requestLines);
                foreach (var allocation in allocations)
                    allocated[allocation.ReceiptLineNumber] += allocation.FunctionalAmount;
                calculatedLines.Add(new(
                    line.LineNumber, line.CostKind, line.Description.Trim(), line.Amount,
                    line.TaxableBaseAmount, line.TaxCode.Trim().ToUpperInvariant(), line.TaxRate,
                    line.TaxAmount, line.TaxTreatment, line.CostTreatment, line.AllocationMethod,
                    functionalAmount, functionalBase, functionalTax,
                    Money(functionalAmount + functionalTax), allocations));
            }

            var net = Money(document.Lines.Sum(line => line.Amount));
            var tax = Money(document.Lines.Sum(line => line.TaxAmount));
            documents.Add(new(
                document, net, tax, Money(net + tax),
                Money(net * documentExchange.Rate), Money(tax * documentExchange.Rate),
                Money((net + tax) * documentExchange.Rate), calculatedLines));
        }

        var receiptLines = merchandise.Lines.Select(line =>
        {
            var source = requestLines[line.LineNumber];
            var functionalNet = Money(line.NetAmount * exchange.Rate);
            var functionalTax = Money(line.TaxAmount * exchange.Rate);
            var landed = Money(allocated[line.LineNumber]);
            var recognized = Money(functionalNet +
                (line.TaxTreatment == PurchaseTaxTreatment.CapitalizedCost ? functionalTax : 0m) +
                landed);
            return new GoodsReceiptLineSnapshot(
                line.LineNumber, line.ProductId, line.Description, line.Quantity, line.UnitCost,
                line.DiscountAmount, line.TaxCode, line.TaxRate, line.TaxTreatment.ToString(),
                line.NetAmount, line.TaxAmount, line.LineTotal,
                source.PresentationName, source.PresentationQuantity, source.UnitsPerPresentation,
                source.PurchaseOrderLineId, source.OverReceiptReason, false,
                source.TotalGrossWeightKg, source.TotalVolumeM3,
                functionalNet, functionalTax, Money(line.LineTotal * exchange.Rate),
                landed, recognized);
        }).ToArray();

        return new(
            exchange.Rate, exchange.Date, exchange.Source,
            Money(merchandise.NetAmount * exchange.Rate),
            Money(merchandise.TaxAmount * exchange.Rate),
            Money(merchandise.GrandTotal * exchange.Rate),
            receiptLines, documents);
    }

    private static IReadOnlyList<GoodsReceiptCostAllocationSnapshot> Allocate(
        GoodsReceiptCostLineRequest line,
        decimal amount,
        IReadOnlyList<CalculatedGoodsReceiptLine> merchandise,
        IReadOnlyDictionary<int, GoodsReceiptLineRequest> requests)
    {
        if (amount == 0)
        {
            if (line.AllocationMethod != PurchaseCostAllocationMethods.None &&
                line.CostTreatment == PurchaseCostTreatments.Expense)
                return [];
            return [];
        }
        if (line.AllocationMethod == PurchaseCostAllocationMethods.None)
            throw new PurchasingValidationException("A capitalized cost requires an allocation method.");

        var eligibleNumbers = line.EligibleReceiptLineNumbers is { Count: > 0 }
            ? line.EligibleReceiptLineNumbers.Distinct().Order().ToArray()
            : merchandise.Select(value => value.LineNumber).Order().ToArray();
        if (eligibleNumbers.Length == 0 || eligibleNumbers.Any(number => !requests.ContainsKey(number)))
            throw new PurchasingValidationException("A cost allocation references an invalid receipt line.");

        if (line.AllocationMethod == PurchaseCostAllocationMethods.Manual)
        {
            var manual = (line.ManualAllocations ?? []).OrderBy(value => value.ReceiptLineNumber).ToArray();
            if (manual.Length != eligibleNumbers.Length ||
                !manual.Select(value => value.ReceiptLineNumber).SequenceEqual(eligibleNumbers) ||
                manual.Any(value => value.FunctionalAmount < 0) ||
                Money(manual.Sum(value => value.FunctionalAmount)) != amount)
                throw new PurchasingValidationException("Manual allocations must cover eligible lines and equal the capitalized functional amount.");
            return manual.Select(value => new GoodsReceiptCostAllocationSnapshot(
                line.LineNumber, value.ReceiptLineNumber,
                amount == 0 ? 0 : value.FunctionalAmount / amount,
                Money(value.FunctionalAmount), line.AllocationMethod)).ToArray();
        }

        var weights = eligibleNumbers.Select(number =>
        {
            var calculated = merchandise.Single(value => value.LineNumber == number);
            var request = requests[number];
            return line.AllocationMethod switch
            {
                PurchaseCostAllocationMethods.Value => calculated.NetAmount,
                PurchaseCostAllocationMethods.Quantity => calculated.Quantity,
                PurchaseCostAllocationMethods.Weight => request.TotalGrossWeightKg ?? 0,
                PurchaseCostAllocationMethods.Volume => request.TotalVolumeM3 ?? 0,
                PurchaseCostAllocationMethods.Equal => 1m,
                _ => throw new PurchasingValidationException("The cost allocation method is invalid.")
            };
        }).ToArray();
        if (weights.Any(value => value <= 0))
            throw new PurchasingValidationException(
                $"Allocation by {line.AllocationMethod} requires a positive value on every eligible receipt line.");
        var totalWeight = weights.Sum();
        var raw = weights.Select(value => amount * value / totalWeight).ToArray();
        var rounded = raw.Select(Money).ToArray();
        var residual = Money(amount - rounded.Sum());
        if (residual != 0)
        {
            var target = Enumerable.Range(0, raw.Length)
                .OrderByDescending(index => residual > 0 ? raw[index] - rounded[index] : rounded[index] - raw[index])
                .ThenBy(index => eligibleNumbers[index])
                .First();
            rounded[target] = Money(rounded[target] + residual);
        }
        return eligibleNumbers.Select((number, index) => new GoodsReceiptCostAllocationSnapshot(
            line.LineNumber, number, weights[index] / totalWeight, rounded[index], line.AllocationMethod)).ToArray();
    }

    private static void ValidateDocument(GoodsReceiptCostDocumentRequest document)
    {
        if (document.CostDocumentId == Guid.Empty || document.SupplierId == Guid.Empty)
            throw new PurchasingValidationException("Every additional cost document requires an id and supplier.");
        if (!PurchaseEvidenceTypes.IsValid(document.PurchaseEvidenceType))
            throw new PurchasingValidationException("The additional document evidence type is invalid.");
        if (string.IsNullOrWhiteSpace(document.DocumentNumber) || document.DocumentNumber.Trim().Length > 80)
            throw new PurchasingValidationException("Every additional cost document requires a number of at most 80 characters.");
        if (document.IssuedAt == default || document.Lines is null || document.Lines.Count == 0)
            throw new PurchasingValidationException("Every additional cost document requires an issue date and lines.");
        if (!document.CreatesPayable)
            throw new PurchasingValidationException(
                "Additional supplier documents must create a payable until a cash-settlement source is available.");
        if (document.CreatesPayable && document.DueDate is null)
            throw new PurchasingValidationException("A payable additional document requires a due date.");
        if (document.DueDate < document.IssuedAt)
            throw new PurchasingValidationException("An additional document due date cannot precede its issue date.");
        if (document.Lines.Select(line => line.LineNumber).Distinct().Count() != document.Lines.Count)
            throw new PurchasingValidationException("Additional document line numbers must be unique.");
    }

    private static void ValidateCostLine(
        GoodsReceiptCostDocumentRequest document, GoodsReceiptCostLineRequest line)
    {
        if (line.LineNumber <= 0 || !PurchaseCostKinds.IsValid(line.CostKind) ||
            !PurchaseCostTreatments.IsValid(line.CostTreatment) ||
            !PurchaseCostAllocationMethods.IsValid(line.AllocationMethod) ||
            line.TaxTreatment is not (PurchasingTaxTreatments.DeductibleInputVat or
                PurchasingTaxTreatments.CapitalizedCost or PurchasingTaxTreatments.NotApplicable))
            throw new PurchasingValidationException("An additional cost line has an invalid type, treatment or allocation method.");
        if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Trim().Length > 250 ||
            line.Amount < 0 || line.TaxableBaseAmount < 0 || line.TaxAmount < 0 ||
            line.TaxRate is < 0 or > 100 || string.IsNullOrWhiteSpace(line.TaxCode))
            throw new PurchasingValidationException("An additional cost line contains invalid amounts or description.");
        if (line.TaxRate == 0 && line.TaxAmount != 0)
            throw new PurchasingValidationException("A zero-rate additional cost line cannot contain tax.");
        if (line.TaxRate > 0 && line.TaxTreatment == PurchasingTaxTreatments.NotApplicable)
            throw new PurchasingValidationException("A taxed additional cost line must declare its tax treatment.");
        if (line.TaxRate == 0 && line.TaxTreatment != PurchasingTaxTreatments.NotApplicable)
            throw new PurchasingValidationException("A zero-rate additional cost line must use NotApplicable.");
        if (document.PurchaseEvidenceType != PurchaseEvidenceTypes.ImportDeclaration &&
            Money(line.TaxableBaseAmount * line.TaxRate / 100m) != Money(line.TaxAmount))
            throw new PurchasingValidationException("The additional document tax does not reconcile with its base and rate.");
        if (document.PurchaseEvidenceType == PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            line.TaxAmount > 0)
            throw new PurchasingValidationException(
                "A foreign commercial invoice cannot recognize Colombian input VAT; use an import declaration.");
        if (line.CostTreatment == PurchaseCostTreatments.Expense &&
            line.AllocationMethod != PurchaseCostAllocationMethods.None)
            throw new PurchasingValidationException("An expensed cost line must not allocate inventory cost.");
    }

    private static (decimal Rate, DateOnly Date, string Source) ValidateExchange(
        string currency, decimal rate, DateOnly? date, string source, DateTimeOffset occurredAt)
    {
        var normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3 || rate <= 0)
            throw new PurchasingValidationException("Currency and a positive exchange rate are required.");
        if (normalized == "COP" && rate != 1)
            throw new PurchasingValidationException("COP documents must use exchange rate 1.");
        if (normalized != "COP" && (date is null || string.IsNullOrWhiteSpace(source)))
            throw new PurchasingValidationException("Foreign-currency documents require exchange-rate date and source.");
        return (decimal.Round(rate, 8, MidpointRounding.AwayFromZero),
            date ?? DateOnly.FromDateTime(occurredAt.Date),
            string.IsNullOrWhiteSpace(source) ? "FunctionalCurrency" : source.Trim());
    }

    private static decimal Money(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
