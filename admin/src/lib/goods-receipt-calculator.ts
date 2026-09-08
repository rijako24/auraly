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
