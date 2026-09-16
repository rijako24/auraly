const roundMoney = (value: number) => Math.round(value * 100) / 100;

export function calculateRetailUnitPrice(
  unitPrice: number,
  taxRate = 0,
  taxExclusive = false,
): number {
  if (!Number.isFinite(unitPrice) || !Number.isFinite(taxRate)) {
    return 0;
  }

  return roundMoney(
    taxExclusive ? unitPrice * (1 + taxRate / 100) : unitPrice,
  );
}

export function calculateReceiptRetailUnitPrice(
  publishedUnitPrice: number,
): number {
  return calculateRetailUnitPrice(publishedUnitPrice);
}

export function calculateEffectiveRetailUnitPrice(
  lineTotal: number,
  quantity: number,
) {
  if (!Number.isFinite(lineTotal) || !Number.isFinite(quantity) || quantity <= 0) {
    return 0;
  }
  return Math.round((lineTotal / quantity + Number.EPSILON) * 1_000_000) / 1_000_000;
}
