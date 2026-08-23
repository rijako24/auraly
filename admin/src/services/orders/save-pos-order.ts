import { sellerOrdersApi, type SellerOrderResult } from "@/services/api/seller-orders";
import type { PosDraft } from "@/services/pos/pos-edge-client";

export type PosOrderSaveContext = {
  businessId: string;
  warehouseId: string;
  workSessionId: string;
};

export async function savePosDraftAsOrder(
  context: PosOrderSaveContext,
  draft: PosDraft,
  idempotencyKey: string,
): Promise<SellerOrderResult> {
  if (!draft.customerId)
    throw new Error("Selecciona un cliente antes de guardar el pedido.");
  if (!draft.lines.length)
    throw new Error("Agrega al menos un producto antes de guardar el pedido.");

  const lines = draft.lines.map((line) => ({
    productId: line.productId.value,
    quantity: line.quantity,
  }));
  return draft.sourceOrderId
    ? sellerOrdersApi.update(draft.sourceOrderId, {
        notes: draft.observation ?? null,
        idempotencyKey,
        lines,
        workSessionId: context.workSessionId,
      })
    : sellerOrdersApi.create({
        businessId: context.businessId,
        warehouseId: context.warehouseId,
        customerId: draft.customerId,
        partySiteId: null,
        routeId: null,
        routeStopId: null,
        capturedOffline: false,
        notes: draft.observation ?? null,
        idempotencyKey,
        lines,
      });
}
