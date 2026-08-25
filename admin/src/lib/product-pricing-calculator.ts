export type ProductPricingField = "cost" | "margin" | "salePrice";

export type ProductPricingValues = {
  cost: number;
  margin: number;
  salePrice: number;
  salesTaxRate?: number;
};

const MAX_MARGIN = 99.9999;

export function recalculateProductPricing(
  changed: ProductPricingField,
  value: number,
  current: ProductPricingValues,
): ProductPricingValues {
  const safeValue = requireNonNegative(value, changed);
  const salesTaxRate = requireTaxRate(current.salesTaxRate ?? 0);

  // An explicit sale price owns the recalculation. Do not reject it because a
  // previously stored margin is negative; the new price determines the margin.
  if (changed === "salePrice") {
    return {
      cost: current.cost,
      margin: marginFromCostAndGrossSale(current.cost, safeValue, salesTaxRate),
      salePrice: safeValue,
      salesTaxRate,
    };
  }

  const margin = requireMargin(changed === "margin" ? safeValue : current.margin);
  const cost = changed === "cost" ? safeValue : current.cost;
  const netSalePrice = cost / (1 - margin / 100);
  return {
    cost,
    margin,
    salePrice: roundMoney(netSalePrice * (1 + salesTaxRate / 100)),
    salesTaxRate,
  };
}

export function recalculateProductPricingForSalesTaxChange(
  salesTaxRate: number,
  current: ProductPricingValues,
): ProductPricingValues {
  return recalculateProductPricing("margin", current.margin, {
    ...current,
    salesTaxRate,
  });
}

export function marginFromCostAndSale(cost: number, salePrice: number): number {
  return marginFromCostAndGrossSale(cost, salePrice, 0);
}

export function marginFromCostAndGrossSale(cost: number, grossSalePrice: number, salesTaxRate: number): number {
  requireNonNegative(cost, "cost");
  requireTaxRate(salesTaxRate);
  if (!Number.isFinite(grossSalePrice) || grossSalePrice <= 0) return 0;
  const netSalePrice = grossSalePrice / (1 + salesTaxRate / 100);
  return roundPercent(((netSalePrice - cost) / netSalePrice) * 100);
}

function requireNonNegative(value: number, field: ProductPricingField): number {
  if (!Number.isFinite(value) || value < 0)
    throw new RangeError(`${field} must be a non-negative number.`);
  return value;
}

function requireMargin(value: number): number {
  if (!Number.isFinite(value) || value < 0 || value > MAX_MARGIN)
    throw new RangeError("margin must be between 0 and 99.9999.");
  return value;
}

function requireTaxRate(value: number): number {
  if (!Number.isFinite(value) || value < 0 || value > 100)
    throw new RangeError("salesTaxRate must be between 0 and 100.");
  return value;
}

function roundMoney(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

function roundPercent(value: number): number {
  return Math.round((value + Number.EPSILON) * 10_000) / 10_000;
}
