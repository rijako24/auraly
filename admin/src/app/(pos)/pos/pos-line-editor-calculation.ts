export function lineDiscountPercent(discount: number, quantity: number, salePrice: number): number {
  const gross = quantity * salePrice;
  return gross <= 0 ? 0 : round(discount / gross * 100, 4);
}

export function lineMarginPercent(
  unitCost: number, quantity: number, salePriceWithTax: number,
  discountWithTax: number, taxRate: number,
): number {
  if (quantity <= 0) return 0;
  const netUnit = Math.max(0, quantity * salePriceWithTax - discountWithTax) /
    quantity / (1 + taxRate / 100);
  return netUnit <= 0 ? 0 : round((netUnit - unitCost) / netUnit * 100, 4);
}

export function salePriceForMargin(
  unitCost: number, marginPercent: number, discountPercent: number, taxRate: number,
): number {
  if (marginPercent >= 100 || discountPercent >= 100) return Number.NaN;
  const netUnit = unitCost / (1 - marginPercent / 100);
  return round(netUnit * (1 + taxRate / 100) / (1 - discountPercent / 100), 6);
}

export type ReactiveLineEconomics = {
  finalUnitPrice: number;
  documentUnitPrice: number;
  discount: number;
  discountPercent: number;
  marginPercent: number;
};

export function lineEconomicsFromFinalPrice(
  unitCost: number,
  quantity: number,
  referenceUnitPriceWithTax: number,
  finalUnitPriceWithTax: number,
  taxRate: number,
): ReactiveLineEconomics {
  const finalUnitPrice = round(Math.max(0, finalUnitPriceWithTax), 6);
  const documentUnitPrice = round(Math.max(referenceUnitPriceWithTax, finalUnitPrice), 6);
  const discount = round(Math.max(0, quantity * (documentUnitPrice - finalUnitPrice)), 6);
  return {
    finalUnitPrice,
    documentUnitPrice,
    discount,
    discountPercent: lineDiscountPercent(discount, quantity, documentUnitPrice),
    marginPercent: lineMarginPercent(unitCost, quantity, documentUnitPrice, discount, taxRate),
  };
}

export function lineEconomicsFromDiscount(
  unitCost: number,
  quantity: number,
  referenceUnitPriceWithTax: number,
  discountWithTax: number,
  taxRate: number,
): ReactiveLineEconomics {
  const discount = round(Math.max(0, discountWithTax), 6);
  const finalUnitPrice = quantity <= 0
    ? 0
    : round(referenceUnitPriceWithTax - discount / quantity, 6);
  return {
    finalUnitPrice,
    documentUnitPrice: referenceUnitPriceWithTax,
    discount,
    discountPercent: lineDiscountPercent(discount, quantity, referenceUnitPriceWithTax),
    marginPercent: lineMarginPercent(
      unitCost, quantity, referenceUnitPriceWithTax, discount, taxRate,
    ),
  };
}

export function lineEconomicsFromDiscountPercent(
  unitCost: number,
  quantity: number,
  referenceUnitPriceWithTax: number,
  discountPercent: number,
  taxRate: number,
): ReactiveLineEconomics {
  return lineEconomicsFromDiscount(
    unitCost,
    quantity,
    referenceUnitPriceWithTax,
    quantity * referenceUnitPriceWithTax * discountPercent / 100,
    taxRate,
  );
}

export function lineEconomicsFromMargin(
  unitCost: number,
  quantity: number,
  referenceUnitPriceWithTax: number,
  marginPercent: number,
  taxRate: number,
): ReactiveLineEconomics {
  return lineEconomicsFromFinalPrice(
    unitCost,
    quantity,
    referenceUnitPriceWithTax,
    salePriceForMargin(unitCost, marginPercent, 0, taxRate),
    taxRate,
  );
}

export function nextFocusableIndex(currentIndex: number, length: number, backwards: boolean): number {
  if (length <= 0) return -1;
  if (currentIndex < 0) return backwards ? length - 1 : 0;
  const direction = backwards ? -1 : 1;
  return (currentIndex + direction + length) % length;
}

export type ProratableSaleLine = {
  lineId: string;
  quantity: number;
  unitPrice: number;
};

export type ProratableDiscountLine = ProratableSaleLine & { discount: number };

export function prorateSaleDiscount(
  lines: readonly ProratableDiscountLine[],
  discountValue: number,
): Array<ProratableDiscountLine & { allocatedValue: number }> {
  if (!Number.isFinite(discountValue) || discountValue <= 0)
    throw new Error("El descuento debe ser mayor que cero.");
  if (!lines.length || lines.some(line =>
    !Number.isFinite(line.quantity) || line.quantity <= 0 ||
    !Number.isFinite(line.unitPrice) || line.unitPrice < 0 ||
    !Number.isFinite(line.discount) || line.discount < 0))
    throw new Error("Todas las líneas deben tener valores válidos.");
  const available = lines.map(line =>
    round(Math.max(0, line.quantity * line.unitPrice - line.discount), 6));
  const totalAvailable = round(available.reduce((sum, value) => sum + value, 0), 6);
  if (discountValue > totalAvailable)
    throw new Error("El descuento no puede superar el valor disponible de la venta.");

  const lastEligible = available.reduce((last, value, index) => value > 0 ? index : last, -1);
  let allocated = 0;
  return lines.map((line, index) => {
    const allocation = index === lastEligible
      ? round(discountValue - allocated, 6)
      : index > lastEligible || available[index] === 0
        ? 0
        : round(discountValue * available[index] / totalAvailable, 6);
    allocated = round(allocated + allocation, 6);
    return {
      ...line,
      discount: round(line.discount + allocation, 6),
      allocatedValue: allocation,
    };
  });
}

export function isPositiveWholeSaleValue(value: number): boolean {
  return Number.isInteger(value) && value > 0;
}

export type GridDirection = "ArrowLeft" | "ArrowRight" | "ArrowUp" | "ArrowDown";

export function nextGridPosition(
  row: number,
  column: number,
  availableColumns: readonly (readonly number[])[],
  direction: GridDirection,
): { row: number; column: number } | null {
  if (availableColumns.length === 0) return null;
  if (direction === "ArrowLeft" || direction === "ArrowRight") {
    const columns = availableColumns[row] ?? [];
    if (columns.length === 0) return null;
    const current = columns.indexOf(column);
    if (current < 0) return null;
    const next = current + (direction === "ArrowRight" ? 1 : -1);
    return next >= 0 && next < columns.length ? { row, column: columns[next] } : null;
  }

  const step = direction === "ArrowDown" ? 1 : -1;
  for (let targetRow = row + step;
    targetRow >= 0 && targetRow < availableColumns.length;
    targetRow += step) {
    const columns = availableColumns[targetRow] ?? [];
    if (columns.length === 0) continue;
    const targetColumn = columns.includes(column)
      ? column
      : columns.reduce((nearest, candidate) => Math.abs(candidate - column) < Math.abs(nearest - column) ? candidate : nearest);
    return { row: targetRow, column: targetColumn };
  }
  return null;
}

function round(value: number, decimals: number): number {
  const factor = 10 ** decimals;
  return Math.round((value + Number.EPSILON) * factor) / factor;
}
