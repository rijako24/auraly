type PosOrderDraftLine = {
  productId: { value: string };
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  promotionDiscount?: number;
  taxRate: number;
  total: number;
  priceSource: string;
  documentUnitCost: number;
  publicUnitPrice?: number | null;
};

export function buildPosOrderUpdateLines(lines: PosOrderDraftLine[]) {
  return lines.map((line) => {
    const publicUnitPrice = line.publicUnitPrice ?? money(
      line.unitPrice * (1 + line.taxRate / 100),
    );
    const publicGross = money(publicUnitPrice * line.quantity);

    return {
      productId: line.productId.value,
      description: line.description,
      quantity: line.quantity,
      unitPrice: publicUnitPrice,
      discountAmount: money(publicGross - line.total),
      priceSource: line.priceSource,
      documentUnitCost: line.documentUnitCost,
    };
  });
}

function money(value: number) {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}
