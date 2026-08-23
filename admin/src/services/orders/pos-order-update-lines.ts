type PosOrderDraftLine = {
  productId: { value: string };
  quantity: number;
  unitPrice: number;
  discount: number;
};

export function buildPosOrderUpdateLines(lines: PosOrderDraftLine[]) {
  return lines.map((line) => ({
    productId: line.productId.value,
    quantity: line.quantity,
    unitPrice: line.unitPrice,
    discountAmount: line.discount,
  }));
}
