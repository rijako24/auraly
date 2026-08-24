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
