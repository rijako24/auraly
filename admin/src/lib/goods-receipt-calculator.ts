type ConfirmedReceiptAmounts = {
  currencyCode: string;
  grandTotal?: number;
  taxAmount?: number;
  functionalGrandTotal?: number;
  functionalTaxAmount?: number;
  withholding?: { withholdingTotal: number; netAmount: number } | null;
};

// Summarize confirmed amounts only; taxes and landed-cost allocations remain
// owned by the backend. Original currencies must never be added together.
export function summarizeGoodsReceipt(detail: ConfirmedReceiptAmounts & {
  additionalCostDocuments?: ConfirmedReceiptAmounts[] | null;
  lines: Array<{ allocatedLandedCostAmount?: number; recognizedInventoryCostAmount?: number }>;
}) {
  const documents = [detail, ...(detail.additionalCostDocuments ?? [])];
  const copAmount = (functional: number | undefined, original: number | undefined,
    currency: string) => {
    const value = functional ?? (currency === "COP" ? original : undefined);
    return value !== undefined && Number.isFinite(value) ? value : null;
  };
  const sum = (values: Array<number | null>) => values.some(value => value === null)
    ? null : values.reduce<number>((total, value) => total + value!, 0);
  const gross = documents.map(document => copAmount(document.functionalGrandTotal,
    document.grandTotal, document.currencyCode));
  const withholding = documents.map(document => document.withholding?.withholdingTotal ?? 0);
  return {
    principal: gross[0],
    additional: sum(gross.slice(1)),
    tax: sum(documents.map(document => copAmount(document.functionalTaxAmount,
      document.taxAmount, document.currencyCode))),
    gross: sum(gross),
    withholding: sum(withholding),
    net: sum(documents.map((document, index) => document.withholding?.netAmount ?? gross[index])),
    landedCost: sum(detail.lines.map(line => copAmount(line.allocatedLandedCostAmount,
      undefined, detail.currencyCode))),
    inventory: sum(detail.lines.map(line => copAmount(line.recognizedInventoryCostAmount,
      undefined, detail.currencyCode))),
  };
}

export type GoodsReceiptCalculationLine = {
  quantity: number;
  unitCost: number;
  discountAmount: number;
  taxRate: number;
};

export function calculateGoodsReceiptLine(line: GoodsReceiptCalculationLine) {
  const gross = line.quantity * line.unitCost;
  const net = Math.max(0, gross - line.discountAmount);
  const tax = net * line.taxRate / 100;
  return { net, tax, total: net + tax };
}

export function calculateGoodsReceiptTotals(lines: GoodsReceiptCalculationLine[]) {
  return lines.reduce((result, line) => {
    const value = calculateGoodsReceiptLine(line);
    return {
      net: result.net + value.net,
      tax: result.tax + value.tax,
      total: result.total + value.total,
    };
  }, { net: 0, tax: 0, total: 0 });
}

export function calculateBaseQuantity(presentationQuantity: number, unitsPerPresentation: number) {
  if (!Number.isFinite(presentationQuantity) || presentationQuantity < 0)
    throw new Error("La cantidad de presentaciones no es válida.");
  if (!Number.isFinite(unitsPerPresentation) || unitsPerPresentation <= 0)
    throw new Error("Las unidades por presentación deben ser mayores que cero.");
  return presentationQuantity * unitsPerPresentation;
}

export function goodsReceiptUnitLabel(unitCode: string | null | undefined, quantity = 1) {
  const normalized = unitCode?.trim().toUpperCase();
  if (!normalized || normalized === "EA" || normalized === "NIU")
    return quantity === 1 ? "unidad" : "unidades";
  return unitCode!.trim();
}

export function nextGoodsReceiptQuantityIndex(
  currentIndex: number,
  offset: number,
  lineCount: number,
) {
  if (lineCount <= 0) return -1;
  return Math.max(0, Math.min(currentIndex + offset, lineCount - 1));
}

export type GoodsReceiptEditorField = "quantity" | "unitCost" | "discount";
export type GoodsReceiptEditorTarget =
  | { kind: "product-search" }
  | { kind: "cell"; rowIndex: number; field: GoodsReceiptEditorField };

const goodsReceiptEditorFields: GoodsReceiptEditorField[] = [
  "quantity", "unitCost", "discount",
];

export function nextGoodsReceiptEditorTarget(
  rowIndex: number,
  field: GoodsReceiptEditorField,
  key: string,
  lineCount: number,
): GoodsReceiptEditorTarget | null {
  if (key === "Enter") return { kind: "product-search" };
  if (lineCount <= 0) return null;
  const fieldIndex = goodsReceiptEditorFields.indexOf(field);
  if (key === "ArrowLeft") return {
    kind: "cell", rowIndex, field: goodsReceiptEditorFields[Math.max(0, fieldIndex - 1)],
  };
  if (key === "ArrowRight") return {
    kind: "cell", rowIndex,
    field: goodsReceiptEditorFields[Math.min(goodsReceiptEditorFields.length - 1, fieldIndex + 1)],
  };
  if (key === "ArrowUp" || key === "ArrowDown") return {
    kind: "cell",
    rowIndex: nextGoodsReceiptQuantityIndex(rowIndex, key === "ArrowDown" ? 1 : -1, lineCount),
    field,
  };
  return null;
}

type PurchaseCostPreviewLine = GoodsReceiptCalculationLine & {
  lineNumber: number;
  taxTreatment: string;
  totalGrossWeightKg?: number | null;
  totalVolumeM3?: number | null;
};

type PurchaseCostPreviewDocument = {
  currencyCode: string;
  exchangeRate: number;
  lines: Array<{
    lineNumber: number;
    amount: number;
    taxAmount: number;
    taxTreatment: string;
    costTreatment: string;
    allocationMethod: string;
    eligibleReceiptLineNumbers?: number[] | null;
    manualAllocations?: Array<{ receiptLineNumber: number; functionalAmount: number }> | null;
  }>;
};

export type GoodsReceiptPurchaseCostPreview = {
  lineNumber: number;
  allocatedAdditionalCost: number;
  recognizedInventoryCost: number;
  purchaseUnitCost: number | null;
};

// UI preview of the backend-owned allocation. Confirmation recalculates and validates
// the same values in GoodsReceiptCostCalculator before anything is persisted.
export function previewGoodsReceiptPurchaseCosts(
  receiptLines: PurchaseCostPreviewLine[],
  receiptCurrencyCode: string,
  receiptExchangeRate: number,
  documents: PurchaseCostPreviewDocument[],
): GoodsReceiptPurchaseCostPreview[] {
  const receiptRate = receiptCurrencyCode === "COP" ? 1 : receiptExchangeRate;
  const allocated = new Map(receiptLines.map(line => [line.lineNumber, 0]));

  for (const document of documents) {
    const rate = document.currencyCode === "COP" ? 1 : document.exchangeRate;
    for (const costLine of document.lines) {
      if (costLine.costTreatment !== "Capitalize") continue;
      const amount = money(
        money(costLine.amount * rate) +
        (costLine.taxTreatment === "CapitalizedCost" ? money(costLine.taxAmount * rate) : 0),
      );
      if (amount === 0 || costLine.allocationMethod === "None") continue;
      const eligibleNumbers = (costLine.eligibleReceiptLineNumbers?.length
        ? [...new Set(costLine.eligibleReceiptLineNumbers)]
        : receiptLines.map(line => line.lineNumber)).sort((left, right) => left - right);
      const eligible = eligibleNumbers.map(number => receiptLines.find(line => line.lineNumber === number));
      if (eligible.some(line => !line)) continue;

      if (costLine.allocationMethod === "Manual") {
        const manual = costLine.manualAllocations ?? [];
        if (money(manual.reduce((sum, item) => sum + item.functionalAmount, 0)) !== amount) continue;
        for (const item of manual)
          if (allocated.has(item.receiptLineNumber))
            allocated.set(item.receiptLineNumber,
              money((allocated.get(item.receiptLineNumber) ?? 0) + item.functionalAmount));
        continue;
      }

      const weights = eligible.map(line => {
        if (!line) return 0;
        const calculated = calculateGoodsReceiptLine(line);
        switch (costLine.allocationMethod) {
          case "Value": return calculated.net;
          case "Quantity": return line.quantity;
          case "Weight": return line.totalGrossWeightKg ?? 0;
          case "Volume": return line.totalVolumeM3 ?? 0;
          case "Equal": return 1;
          default: return 0;
        }
      });
      if (weights.some(value => value <= 0)) continue;
      const totalWeight = weights.reduce((sum, value) => sum + value, 0);
      const raw = weights.map(value => amount * value / totalWeight);
      const rounded = raw.map(money);
      const residual = money(amount - rounded.reduce((sum, value) => sum + value, 0));
      if (residual !== 0) {
        const candidates = raw.map((value, index) => ({ index, fraction: residual > 0
          ? value - rounded[index] : rounded[index] - value, lineNumber: eligibleNumbers[index] }));
        candidates.sort((left, right) => right.fraction - left.fraction || left.lineNumber - right.lineNumber);
        rounded[candidates[0].index] = money(rounded[candidates[0].index] + residual);
      }
      eligibleNumbers.forEach((lineNumber, index) => allocated.set(
        lineNumber, money((allocated.get(lineNumber) ?? 0) + rounded[index]),
      ));
    }
  }

  return receiptLines.map(line => {
    const calculated = calculateGoodsReceiptLine(line);
    const base = money(
      money(calculated.net * receiptRate) +
      (line.taxTreatment === "CapitalizedCost" ? money(calculated.tax * receiptRate) : 0),
    );
    const additional = money(allocated.get(line.lineNumber) ?? 0);
    const recognized = money(base + additional);
    return {
      lineNumber: line.lineNumber,
      allocatedAdditionalCost: additional,
      recognizedInventoryCost: recognized,
      purchaseUnitCost: line.quantity > 0 ? money(recognized / line.quantity) : null,
    };
  });
}

function money(value: number) {
  return Math.round((value + Number.EPSILON) * 10_000) / 10_000;
}
