export type OrderReviewEvaluation = {
  quantity: number;
  additionalQuantity: number;
  validNumber: boolean;
  insufficient: boolean;
};

export function isOrderReviewLinePending(
  manageStock: boolean,
  quantity: number,
  reservedQuantity: number,
): boolean {
  return manageStock && reservedQuantity < quantity;
}

export function editableOrderAvailableQuantity(
  quantityOnHand: number,
  reservedQuantity: number,
): number {
  return quantityOnHand + reservedQuantity;
}

export function evaluateOrderReviewQuantity(
  rawValue: string,
  manageStock: boolean,
  quantityOnHand: number,
  reservedQuantity: number,
): OrderReviewEvaluation {
  const quantity = Number(rawValue);
  const validNumber = Number.isFinite(quantity) && quantity > 0;
  const additionalQuantity = validNumber ? Math.max(0, quantity - reservedQuantity) : 0;
  return {
    quantity,
    additionalQuantity,
    validNumber,
    insufficient: validNumber && manageStock && additionalQuantity > quantityOnHand,
  };
}
