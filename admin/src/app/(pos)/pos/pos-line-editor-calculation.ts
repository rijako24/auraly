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

export function nextFocusableIndex(currentIndex: number, length: number, backwards: boolean): number {
  if (length <= 0) return -1;
  if (currentIndex < 0) return backwards ? length - 1 : 0;
  const direction = backwards ? -1 : 1;
  return (currentIndex + direction + length) % length;
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
    const next = nextFocusableIndex(current, columns.length, direction === "ArrowLeft");
    return { row, column: columns[next] };
  }

  const step = direction === "ArrowDown" ? 1 : -1;
  for (let offset = 1; offset <= availableColumns.length; offset += 1) {
    const targetRow = (row + step * offset + availableColumns.length) % availableColumns.length;
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
