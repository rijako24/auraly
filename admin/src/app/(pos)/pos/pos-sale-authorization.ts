type BelowCostLine = {
  quantity: number;
  net: number;
  documentUnitCost: number;
};

export function saleRequiresBelowCostAuthorization(
  lines: readonly BelowCostLine[],
): boolean {
  return lines.some((line) =>
    Number.isFinite(line.quantity) &&
    Number.isFinite(line.net) &&
    Number.isFinite(line.documentUnitCost) &&
    line.quantity > 0 &&
    line.documentUnitCost > 0 &&
    line.net / line.quantity < line.documentUnitCost,
  );
}
