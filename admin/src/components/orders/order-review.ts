import type { SellerCatalogItem } from "@/services/api/seller-orders";
import type { CommerceOrderLine } from "@/services/orders/commerce-orders-client";

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

export function sellerOrderSubmitDisabled(
  saving: boolean,
  selectedCount: number,
  editing: boolean,
  online: boolean,
  hasInvalidQuantity: boolean,
): boolean {
  return saving || selectedCount === 0 || (editing && !online) || hasInvalidQuantity;
}

export function sellerOrderMaximumQuantity(
  manageStock: boolean,
  quantityOnHand: number,
  originalQuantity: number | null,
  allowsNegativeStockSales: boolean,
): number {
  if (!manageStock || allowsNegativeStockSales) return Number.POSITIVE_INFINITY;
  return Math.max(quantityOnHand, originalQuantity ?? 0);
}

export function editableOrderInitialState(lines: readonly CommerceOrderLine[]): {
  knownItems: Record<string, SellerCatalogItem>;
  quantities: Record<string, number>;
} {
  const knownItems: Record<string, SellerCatalogItem> = {};
  const quantities: Record<string, number> = {};
  for (const line of lines) {
    if (!line.productId) continue;
    knownItems[line.productId] = {
      productId: line.productId,
      productCode: line.productCode ?? line.sku ?? "",
      name: line.productName,
      unitCode: line.unitCode,
      unitPrice: line.unitPrice,
      priceSource: line.priceSource,
      quantityOnHand: editableOrderAvailableQuantity(
        line.quantityOnHand,
        line.reservedQuantity,
      ),
      manageStock: line.manageStock,
    };
    quantities[line.productId] = line.quantity;
  }
  return { knownItems, quantities };
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
