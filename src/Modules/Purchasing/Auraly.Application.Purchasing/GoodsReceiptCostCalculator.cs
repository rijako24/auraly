using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;
using System.Text.Json.Serialization;

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
    IReadOnlyList<CalculatedGoodsReceiptCostDocument> AdditionalDocuments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedGoodsReceiptVoucherCost>? ReferencedDocuments = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GoodsReceiptFinancialDiscountSnapshot? FinancialDiscount = null);

public sealed record CalculatedGoodsReceiptVoucherCost(
    GoodsReceiptVoucherCostRequest Request,
    IReadOnlyList<GoodsReceiptCostAllocationSnapshot> Allocations);

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
        var allocationInputs = merchandise.Lines.ToDictionary(line => line.LineNumber, line =>
        {
            var requested = requestLines[line.LineNumber];
            return new AllocationInput(line.NetAmount, line.Quantity,
                requested.TotalGrossWeightKg, requested.TotalVolumeM3);
        });
        var allocated = merchandise.Lines.ToDictionary(line => line.LineNumber, _ => 0m);
        var documents = new List<CalculatedGoodsReceiptCostDocument>();
        ValidateReferencedDocuments(request.ReferencedCostDocuments);
        var associationIds = (request.ReferencedCostDocuments ?? []).Select(document => document.AssociationId).ToHashSet();
        if ((request.AdditionalCostDocuments ?? []).Any(document => associationIds.Contains(document.CostDocumentId)))
            throw new PurchasingValidationException("La factura adicional y la asociación de comprobante requieren identificadores distintos.");

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
                var allocations = Allocate(line.LineNumber, line.AllocationMethod,
                    line.EligibleReceiptLineNumbers, line.ManualAllocations,
                    capitalizable, allocationInputs);
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

        var referenced = CalculateReferenced(request.ReferencedCostDocuments ?? [], allocationInputs);
        foreach (var allocation in referenced.SelectMany(document => document.Allocations))
            allocated[allocation.ReceiptLineNumber] += allocation.FunctionalAmount;

        var financialDiscount = CalculateFinancialDiscount(request.FinancialDiscount,
            merchandise, exchange.Rate, allocationInputs);
        var anticipatedByLine = financialDiscount?.AnticipatedInCost == true
            ? financialDiscount.Lines.ToDictionary(line => line.ReceiptLineNumber,
                line => line.FunctionalAmount)
            : new Dictionary<int, decimal>();

        var receiptLines = merchandise.Lines.Select(line =>
        {
            var source = requestLines[line.LineNumber];
            var functionalNet = Money(line.NetAmount * exchange.Rate);
            var functionalTax = Money(line.TaxAmount * exchange.Rate);
            var landed = Money(allocated[line.LineNumber]);
            var recognized = Money(functionalNet +
                (line.TaxTreatment == PurchaseTaxTreatment.CapitalizedCost ? functionalTax : 0m) +
                landed - anticipatedByLine.GetValueOrDefault(line.LineNumber));
            if (recognized < 0)
                throw new PurchasingValidationException(
                    "El descuento financiero anticipado supera el costo del producto.");
            return new GoodsReceiptLineSnapshot(
                line.LineNumber, line.ProductId, line.Description, line.Quantity, line.UnitCost,
                line.DiscountAmount, line.TaxCode, line.TaxRate, line.TaxTreatment.ToString(),
                line.NetAmount, line.TaxAmount, line.LineTotal,
                source.PresentationName, source.PresentationQuantity, source.UnitsPerPresentation,
                source.PurchaseOrderLineId, source.OverReceiptReason, false,
                source.TotalGrossWeightKg, source.TotalVolumeM3,
                functionalNet, functionalTax, Money(line.LineTotal * exchange.Rate),
                landed, recognized, source.UnitGrossWeightKg, line.CommercialDiscountPercent);
        }).ToArray();

        return new(
            exchange.Rate, exchange.Date, exchange.Source,
            Money(merchandise.NetAmount * exchange.Rate),
            Money(merchandise.TaxAmount * exchange.Rate),
            Money(merchandise.GrandTotal * exchange.Rate),
            receiptLines, documents, referenced.Count == 0 ? null : referenced,
            financialDiscount);
    }

    private static GoodsReceiptFinancialDiscountSnapshot? CalculateFinancialDiscount(
        GoodsReceiptFinancialDiscountRequest? request, GoodsReceiptCalculation merchandise,
        decimal exchangeRate, IReadOnlyDictionary<int, AllocationInput> allocationInputs)
    {
        if (request is null) return null;
        if (request.Percent <= 0 || request.Percent > 100 ||
            request.Percent != decimal.Round(request.Percent, 6, MidpointRounding.AwayFromZero))
            throw new PurchasingValidationException(
                "El descuento financiero debe ser un porcentaje mayor que cero y hasta 100 con máximo seis decimales.");
        var baseAmount = merchandise.NetAmount;
        var amount = Money(baseAmount * request.Percent / 100m);
        var functionalAmount = Money(amount * exchangeRate);
        if (baseAmount <= 0 || amount <= 0 || functionalAmount <= 0)
            throw new PurchasingValidationException(
                "La base de mercancía y el descuento financiero deben tener un valor positivo.");
        var eligible = allocationInputs.Where(line => line.Value.NetAmount > 0)
            .ToDictionary(line => line.Key, line => line.Value);
        var sourceAllocations = Allocate(0, PurchaseCostAllocationMethods.Value,
            null, null, amount, eligible).ToDictionary(line => line.ReceiptLineNumber);
        var functionalAllocations = Allocate(0, PurchaseCostAllocationMethods.Value,
            null, null, functionalAmount, eligible).ToDictionary(line => line.ReceiptLineNumber);
        var lines = merchandise.Lines.Select(line => new GoodsReceiptFinancialDiscountLineSnapshot(
            line.LineNumber,
            sourceAllocations.GetValueOrDefault(line.LineNumber)?.FunctionalAmount ?? 0m,
            functionalAllocations.GetValueOrDefault(line.LineNumber)?.FunctionalAmount ?? 0m)).ToArray();
        return new(request.Percent, baseAmount, amount, functionalAmount,
            request.ExpectedOn, request.AnticipateInCost, lines);
    }

    // Cost revisions use the accepted receipt's frozen factors. They never
    // reread product weights or reconstruct the original purchase calculation.
    public static IReadOnlyList<CalculatedGoodsReceiptVoucherCost> CalculateReferenced(
        IReadOnlyList<GoodsReceiptVoucherCostRequest> documents,
        IReadOnlyList<GoodsReceiptLineSnapshot> receiptLines)
    {
        ArgumentNullException.ThrowIfNull(receiptLines);
        var inputs = receiptLines.ToDictionary(line => line.LineNumber, line =>
            new AllocationInput(line.NetAmount, line.Quantity,
                line.TotalGrossWeightKg, line.TotalVolumeM3));
        return CalculateReferenced(documents, inputs);
    }

    private static IReadOnlyList<CalculatedGoodsReceiptVoucherCost> CalculateReferenced(
        IReadOnlyList<GoodsReceiptVoucherCostRequest> documents,
        IReadOnlyDictionary<int, AllocationInput> inputs)
    {
        ValidateReferencedDocuments(documents);
        return documents.Select(document => new CalculatedGoodsReceiptVoucherCost(document,
            document.Lines.SelectMany(line => Allocate(line.EntryLineNumber,
                line.AllocationMethod, null, line.ManualAllocations,
                line.FunctionalAmount, inputs)).ToArray())).ToArray();
    }

    public static void ValidateReferencedDocuments(IReadOnlyList<GoodsReceiptVoucherCostRequest>? documents)
    {
        if (documents is null) return;
        if (documents.Count > 100 || documents.Any(document => document is null) ||
            documents.Sum(document => document.Lines?.Count ?? 0) > 500 ||
            documents.Any(document => document.AssociationId == Guid.Empty || document.EntryId == Guid.Empty || document.EntryNumber?.Length > 24) ||
            documents.Select(document => document.AssociationId).Distinct().Count() != documents.Count ||
            documents.Select(document => document.EntryId).Distinct().Count() != documents.Count)
            throw new PurchasingValidationException("Selecciona hasta 100 comprobantes únicos y 500 partidas de costo.");
        foreach (var document in documents)
        {
            if (document.Lines is null || (document.Mode switch
                {
                    GoodsReceiptCostAssociationModes.Associate => document.Lines.Count != 0,
                    GoodsReceiptCostAssociationModes.Distribute => document.Lines.Count == 0,
                    _ => true
                }))
                throw new PurchasingValidationException("Elige solo asociar o distribuir con sus partidas de costo.");
            if (document.Lines.Any(line => line is null || line.EntryLineNumber <= 0 || line.FunctionalAmount <= 0 ||
                    line.FunctionalAmount > 999_999_999_999_999.9999m ||
                    line.FunctionalAmount != Money(line.FunctionalAmount) ||
                    !PurchaseCostAllocationMethods.IsValid(line.AllocationMethod) ||
                    line.AllocationMethod == PurchaseCostAllocationMethods.None) ||
                document.Lines.Select(line => line.EntryLineNumber).Distinct().Count() != document.Lines.Count)
                throw new PurchasingValidationException("Indica un importe positivo en COP y un método de reparto para cada partida única.");
        }
    }

    private static IReadOnlyList<GoodsReceiptCostAllocationSnapshot> Allocate(
        int costLineNumber, string allocationMethod,
        IReadOnlyCollection<int>? eligibleReceiptLineNumbers,
        IReadOnlyCollection<GoodsReceiptCostManualAllocationRequest>? manualAllocations,
        decimal amount,
        IReadOnlyDictionary<int, AllocationInput> inputs)
    {
        if (amount == 0)
            return [];
        if (allocationMethod == PurchaseCostAllocationMethods.None)
            throw new PurchasingValidationException("Un costo capitalizable requiere un método de distribución.");

        var eligibleNumbers = eligibleReceiptLineNumbers is { Count: > 0 }
            ? eligibleReceiptLineNumbers.Distinct().Order().ToArray()
            : inputs.Keys.Order().ToArray();
        if (eligibleNumbers.Length == 0 || eligibleNumbers.Any(number => !inputs.ContainsKey(number)))
            throw new PurchasingValidationException("La distribución del costo contiene una línea de recepción inválida.");

        if (allocationMethod == PurchaseCostAllocationMethods.Manual)
        {
            if (manualAllocations?.Any(value => value is null) == true)
                throw new PurchasingValidationException("Completa cada partida del reparto manual.");
            var manual = (manualAllocations ?? []).OrderBy(value => value.ReceiptLineNumber).ToArray();
            if (manual.Length != eligibleNumbers.Length ||
                !manual.Select(value => value.ReceiptLineNumber).SequenceEqual(eligibleNumbers) ||
                manual.Any(value => value.FunctionalAmount < 0 || value.FunctionalAmount != Money(value.FunctionalAmount)) ||
                Money(manual.Sum(value => value.FunctionalAmount)) != amount)
                throw new PurchasingValidationException("La distribución manual debe cubrir las líneas elegibles y sumar el valor capitalizable en COP.");
            return manual.Select(value => new GoodsReceiptCostAllocationSnapshot(
                costLineNumber, value.ReceiptLineNumber,
                amount == 0 ? 0 : value.FunctionalAmount / amount,
                Money(value.FunctionalAmount), allocationMethod)).ToArray();
        }

        var weights = eligibleNumbers.Select(number =>
        {
            var input = inputs[number];
            return allocationMethod switch
            {
                PurchaseCostAllocationMethods.Value => input.NetAmount,
                PurchaseCostAllocationMethods.Quantity => input.Quantity,
                PurchaseCostAllocationMethods.Weight => input.TotalGrossWeightKg ?? 0,
                PurchaseCostAllocationMethods.Volume => input.TotalVolumeM3 ?? 0,
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
            costLineNumber, number, weights[index] / totalWeight, rounded[index], allocationMethod)).ToArray();
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

    private sealed record AllocationInput(decimal NetAmount, decimal Quantity,
        decimal? TotalGrossWeightKg, decimal? TotalVolumeM3);
}
