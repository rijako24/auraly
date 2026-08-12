const roundMoney = (value: number) => Math.round(value * 100) / 100;

/**
 * Auraly's published price is already the final retail price, including the
 * applicable tax. Tax bases are derived from it only for fiscal breakdowns.
 */
export function calculateRetailUnitPrice(
  publishedUnitPrice: number,
  _taxRate: number,
): number {
  if (!Number.isFinite(publishedUnitPrice)) {
    return 0;
  }

  return roundMoney(publishedUnitPrice);
}

export function calculateReceiptRetailUnitPrice(
  publishedUnitPrice: number,
  _quantity: number,
  _discount: number,
  _tax: number,
): number {
  return calculateRetailUnitPrice(publishedUnitPrice, 0);
}
