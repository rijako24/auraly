const round = (value: number) => Math.round(value * 1_000_000) / 1_000_000;

export type GenericProductEconomics = {
  publicSalePrice: number;
  documentUnitCost: number;
  marginPercent: number;
};

function taxExclusive(publicSalePrice: number, salesTaxRate: number): number {
  return round(Math.max(0, publicSalePrice) / (1 + Math.max(0, salesTaxRate) / 100));
}

export function initializeGenericProductPrice(
  publicSalePrice: number,
  salesTaxRate: number,
): GenericProductEconomics {
  const salePrice = round(Math.max(0, publicSalePrice));
  return {
    publicSalePrice: salePrice,
    documentUnitCost: taxExclusive(salePrice, salesTaxRate),
    marginPercent: 0,
  };
}

export function genericProductFromSalePrice(
  publicSalePrice: number,
  documentUnitCost: number,
  salesTaxRate: number,
): GenericProductEconomics {
  const salePrice = round(Math.max(0, publicSalePrice));
  const netSalePrice = taxExclusive(salePrice, salesTaxRate);
  return {
    publicSalePrice: salePrice,
    documentUnitCost: round(Math.max(0, documentUnitCost)),
    marginPercent: netSalePrice <= 0
      ? 0
      : round((netSalePrice - documentUnitCost) / netSalePrice * 100),
  };
}

export function genericProductFromMargin(
  documentUnitCost: number,
  marginPercent: number,
  salesTaxRate: number,
): GenericProductEconomics {
  if (!Number.isFinite(marginPercent) || marginPercent >= 100)
    throw new Error("El margen debe ser menor que 100 %.");
  const cost = round(Math.max(0, documentUnitCost));
  const publicSalePrice = round(
    cost / (1 - marginPercent / 100) * (1 + Math.max(0, salesTaxRate) / 100),
  );
  return { publicSalePrice, documentUnitCost: cost, marginPercent: round(marginPercent) };
}

export function nextGenericProductFieldIndex(
  currentIndex: number,
  fieldCount: number,
  key: string,
): number | null {
  if (key !== "ArrowUp" && key !== "ArrowDown") return null;
  if (fieldCount <= 0) return null;

  const step = key === "ArrowDown" ? 1 : -1;
  return Math.min(fieldCount - 1, Math.max(0, currentIndex + step));
}
