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
                landed, recognized, source.UnitGrossWeightKg);
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
            throw new PurchasingValidationException("Un costo capitalizable requiere un método de distribución.");

        var eligibleNumbers = line.EligibleReceiptLineNumbers is { Count: > 0 }
            ? line.EligibleReceiptLineNumbers.Distinct().Order().ToArray()
            : merchandise.Select(value => value.LineNumber).Order().ToArray();
        if (eligibleNumbers.Length == 0 || eligibleNumbers.Any(number => !requests.ContainsKey(number)))
            throw new PurchasingValidationException("La distribución del costo contiene una línea de recepción inválida.");

        if (line.AllocationMethod == PurchaseCostAllocationMethods.Manual)
        {
            var manual = (line.ManualAllocations ?? []).OrderBy(value => value.ReceiptLineNumber).ToArray();
            if (manual.Length != eligibleNumbers.Length ||
                !manual.Select(value => value.ReceiptLineNumber).SequenceEqual(eligibleNumbers) ||
                manual.Any(value => value.FunctionalAmount < 0) ||
                Money(manual.Sum(value => value.FunctionalAmount)) != amount)
                throw new PurchasingValidationException("La distribución manual debe cubrir las líneas elegibles y sumar el valor capitalizable en COP.");
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
                _ => throw new PurchasingValidationException("El método de distribución del costo es inválido.")
            };
        }).ToArray();
        if (weights.Any(value => value <= 0))
            throw new PurchasingValidationException(
                "La distribución requiere un valor positivo en cada línea elegible.");
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
            throw new PurchasingValidationException("Cada documento de costo adicional requiere proveedor e identificador.");
        if (!PurchaseEvidenceTypes.IsValid(document.PurchaseEvidenceType))
            throw new PurchasingValidationException("El tipo de soporte del documento adicional es inválido.");
        if (document.DocumentNumber?.Trim().Length > 80 ||
            (document.PurchaseEvidenceType != PurchaseEvidenceTypes.BuyerElectronicSupportDocument &&
             string.IsNullOrWhiteSpace(document.DocumentNumber)))
            throw new PurchasingValidationException(
                "Cada documento adicional requiere un número de hasta 80 caracteres; el documento soporte lo numera Auraly.");
        if (document.IssuedAt == default || document.Lines is null || document.Lines.Count == 0)
            throw new PurchasingValidationException("Cada documento adicional requiere fecha de emisión y conceptos.");
        if (!document.CreatesPayable)
            throw new PurchasingValidationException(
                "El documento adicional debe generar una cuenta por pagar; el pago de contado se registra por separado.");
        if (document.CreatesPayable && document.DueDate is null)
            throw new PurchasingValidationException("El documento adicional requiere fecha de vencimiento.");
        if (document.DueDate < document.IssuedAt)
            throw new PurchasingValidationException("El vencimiento del documento adicional no puede ser anterior a su emisión.");
        if (document.Lines.Select(line => line.LineNumber).Distinct().Count() != document.Lines.Count)
            throw new PurchasingValidationException("Los números de concepto del documento adicional deben ser únicos.");
    }

    private static void ValidateCostLine(
        GoodsReceiptCostDocumentRequest document, GoodsReceiptCostLineRequest line)
    {
        if (line.LineNumber <= 0 || !PurchaseCostKinds.IsValid(line.CostKind) ||
            !PurchaseCostTreatments.IsValid(line.CostTreatment) ||
            !PurchaseCostAllocationMethods.IsValid(line.AllocationMethod) ||
            line.TaxTreatment is not (PurchasingTaxTreatments.DeductibleInputVat or
                PurchasingTaxTreatments.CapitalizedCost or PurchasingTaxTreatments.NotApplicable))
            throw new PurchasingValidationException("Un concepto adicional tiene tipo, tratamiento o distribución inválidos.");
        if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Trim().Length > 250 ||
            line.Amount < 0 || line.TaxableBaseAmount < 0 || line.TaxAmount < 0 ||
            line.TaxRate is < 0 or > 100 || string.IsNullOrWhiteSpace(line.TaxCode))
            throw new PurchasingValidationException("Un concepto adicional tiene valores o descripción inválidos.");
        if (line.TaxRate == 0 && line.TaxAmount != 0)
            throw new PurchasingValidationException("Un concepto con tarifa cero no puede incluir impuesto.");
        if (line.TaxRate > 0 && line.TaxTreatment == PurchasingTaxTreatments.NotApplicable)
            throw new PurchasingValidationException("Un concepto gravado requiere tratamiento del IVA.");
        if (line.TaxRate == 0 && line.TaxTreatment != PurchasingTaxTreatments.NotApplicable)
            throw new PurchasingValidationException("Un concepto con tarifa cero debe marcar el IVA como no aplicable.");
        if (document.PurchaseEvidenceType != PurchaseEvidenceTypes.ImportDeclaration &&
            Money(line.TaxableBaseAmount * line.TaxRate / 100m) != Money(line.TaxAmount))
            throw new PurchasingValidationException("El IVA del documento adicional no coincide con su base y tarifa.");
        if (document.PurchaseEvidenceType == PurchaseEvidenceTypes.ForeignCommercialInvoice &&
            line.TaxAmount > 0)
            throw new PurchasingValidationException(
                "Una factura del exterior no puede registrar IVA colombiano descontable; usa la declaración de importación.");
        if (document.PurchaseEvidenceType == PurchaseEvidenceTypes.InternalReceiptVoucher &&
            line.TaxTreatment == PurchasingTaxTreatments.DeductibleInputVat)
            throw new PurchasingValidationException(
                "Un comprobante interno de costo adicional no puede registrar IVA descontable; inclúyelo en el costo.");
        if (line.CostTreatment == PurchaseCostTreatments.Expense &&
            line.AllocationMethod != PurchaseCostAllocationMethods.None)
            throw new PurchasingValidationException("Un costo llevado al gasto no puede distribuirse al inventario.");
    }

    private static (decimal Rate, DateOnly Date, string Source) ValidateExchange(
        string currency, decimal rate, DateOnly? date, string source, DateTimeOffset occurredAt)
    {
        var normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3 || rate <= 0)
            throw new PurchasingValidationException("Se requiere moneda y una tasa de cambio positiva.");
        if (normalized == "COP" && rate != 1)
            throw new PurchasingValidationException("Los documentos en COP deben usar tasa de cambio 1.");
        if (normalized != "COP" && (date is null || string.IsNullOrWhiteSpace(source)))
            throw new PurchasingValidationException("Los documentos en moneda extranjera requieren fecha y fuente de la tasa de cambio.");
        return (decimal.Round(rate, 8, MidpointRounding.AwayFromZero),
            date ?? DateOnly.FromDateTime(occurredAt.Date),
            string.IsNullOrWhiteSpace(source) ? "FunctionalCurrency" : source.Trim());
    }

    private static decimal Money(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
