export interface SalesReturnCalculationLine {
  originalLineNumber: number;
  soldQuantity: number;
  availableQuantity: number;
  lineTotal: number;
}

export interface SalesReturnSelection {
  selectedLineNumbers: number[];
  lineTotals: Record<number, number>;
  estimatedTotal: number;
  isValid: boolean;
}

export function salesReturnPurchasedUnitPrice(line: SalesReturnCalculationLine): number {
  return line.soldQuantity > 0 ? line.lineTotal / line.soldQuantity : 0;
}

export function calculateSalesReturnSelection(
  lines: SalesReturnCalculationLine[],
  quantities: Record<number, number>,
): SalesReturnSelection {
  const selectedLineNumbers: number[] = [];
  const lineTotals: Record<number, number> = {};
  let estimatedTotal = 0;
  let isValid = true;

  for (const line of lines) {
    const quantity = quantities[line.originalLineNumber] ?? 0;
    if (!Number.isFinite(quantity) || quantity < 0 ||
        quantity > line.availableQuantity || line.soldQuantity <= 0) {
      isValid = false;
      continue;
    }
    if (quantity === 0) continue;
    selectedLineNumbers.push(line.originalLineNumber);
    const lineTotal = salesReturnPurchasedUnitPrice(line) * quantity;
    lineTotals[line.originalLineNumber] = lineTotal;
    estimatedTotal += lineTotal;
  }

  return { selectedLineNumbers, lineTotals, estimatedTotal, isValid };
}
