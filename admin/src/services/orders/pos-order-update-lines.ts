type PosOrderDraftLine = {
  productId: { value: string };
  quantity: number;
  unitPrice: number;
  discount: number;
  priceSource: string;
  documentUnitCost: number;
};

export function buildPosOrderUpdateLines(lines: PosOrderDraftLine[]) {
  return lines.map((line) => ({
    productId: line.productId.value,
    quantity: line.quantity,
    unitPrice: line.unitPrice,
    discountAmount: line.discount,
    priceSource: line.priceSource,
    documentUnitCost: line.documentUnitCost,
  }));
}
