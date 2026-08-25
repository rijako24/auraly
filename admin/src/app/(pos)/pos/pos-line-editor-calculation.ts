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

function round(value: number, decimals: number): number {
  const factor = 10 ** decimals;
  return Math.round((value + Number.EPSILON) * factor) / factor;
}
