import { apiClient, withPagedDefaults } from "./client";

export type PurchaseOrderStatus = "Draft" | "Open" | "PartiallyReceived" | "Received" | "Closed" | "Cancelled";
export interface PurchaseOrderLine {
  lineId: string; lineNumber: number; productId: string; productCode: string; description: string;
  orderedQuantity: number; receivedQuantity: number; cancelledQuantity: number; remainingQuantity: number;
  unitCost: number; discountAmount: number; taxCode: string; taxRate: number; taxTreatment: string;
  netAmount: number; taxAmount: number; lineTotal: number; presentationName: string;
  presentationQuantity: number; unitsPerPresentation: number; rotation30Days: number;
  rotation90Days: number; dailyDemand90Days: number; currentStock: number; incomingQuantity: number;
  rotationCalculatedAt: string | null;
}
export interface PurchaseOrderDetail {
  purchaseOrderId: string; documentNumber: string | null; status: PurchaseOrderStatus;
  warehouseId: string | null; warehouseName: string | null; supplierId: string | null; supplierName: string | null;
  orderedAt: string; expectedAt: string | null; currencyCode: string; notes: string | null;
  netAmount: number; taxAmount: number; grandTotal: number; updatedAt: string;
  concurrencyToken: string | null; lines: PurchaseOrderLine[];
}
export interface PurchaseOrderListItem {
  purchaseOrderId: string; documentNumber: string | null; status: PurchaseOrderStatus;
  supplierName: string | null; warehouseName: string | null; orderedAt: string;
  expectedAt: string | null; grandTotal: number; fulfillmentPercent: number; updatedAt: string;
}
export interface PurchaseOrderPage { items: PurchaseOrderListItem[]; page: number; pageSize: number; totalCount: number; totalPages: number }
export interface PurchaseOrderSuggestion {
  productId: string; targetCoverageDays: number; rotation30Days: number; rotation90Days: number;
  dailyDemand90Days: number; forecastDailyDemand: number; currentStock: number; incomingQuantity: number;
  presentationName: string; unitsPerPresentation: number; suggestedQuantity: number;
  suggestedPresentationQuantity: number; rotationCalculatedAt: string | null;
}
export type PurchaseOrderLineRequest = Pick<PurchaseOrderLine,"lineId"|"lineNumber"|"productId"|"description"|"orderedQuantity"|"unitCost"|"discountAmount"|"taxCode"|"taxRate"|"taxTreatment"|"presentationName"|"presentationQuantity"|"unitsPerPresentation">;
export interface SavePurchaseOrderRequest {
  purchaseOrderId: string; businessId: string; warehouseId: string | null; supplierId: string | null;
  orderedAt: string; expectedAt: string | null; currencyCode: string; notes: string | null;
  lines: PurchaseOrderLineRequest[]; concurrencyToken: string | null;
}
export interface PurchaseOrderReceiptSource extends Omit<PurchaseOrderDetail,"warehouseId"|"supplierId"|"warehouseName"|"supplierName"|"netAmount"|"taxAmount"|"grandTotal"|"updatedAt"|"concurrencyToken"> {
  documentNumber: string; warehouseId: string; supplierId: string;
}
export const purchaseOrdersApi = {
  list: (params: { search?: string; status?: PurchaseOrderStatus; page: number; pageSize: number }) =>
    apiClient.get<PurchaseOrderPage>("/commerce/v1/purchase-orders", withPagedDefaults(params)),
  get: (id: string) => apiClient.get<PurchaseOrderDetail>(`/commerce/v1/purchase-orders/${id}`),
  receiptSource: (id: string) => apiClient.get<PurchaseOrderReceiptSource>(`/commerce/v1/purchase-orders/${id}/receipt-source`),
  suggestions: (request: { businessId: string; warehouseId: string; supplierId: string; productIds: string[]; targetCoverageDays?: number }) =>
    apiClient.post<PurchaseOrderSuggestion[]>("/commerce/v1/purchase-orders/suggestions", request),
  saveDraft: (request: SavePurchaseOrderRequest) => apiClient.put<PurchaseOrderDetail>(`/commerce/v1/purchase-orders/${request.purchaseOrderId}/draft`, request),
  deleteDraft: (id: string, concurrencyToken: string) => apiClient.delete<{ deleted: boolean }>(
    `/commerce/v1/purchase-orders/${id}/draft?concurrencyToken=${encodeURIComponent(concurrencyToken)}`),
  confirm: (request: Omit<SavePurchaseOrderRequest,"warehouseId"|"supplierId"|"concurrencyToken"> & { warehouseId: string; supplierId: string; draftConcurrencyToken: string | null }) =>
    apiClient.postIdempotent<{ purchaseOrderId: string; documentNumber: string; status: string }>("/commerce/v1/purchase-orders/confirm", request, `purchase-order-${request.purchaseOrderId}`),
  close: (id: string, reason: string, concurrencyToken: string) => apiClient.post<{ closed: boolean }>(`/commerce/v1/purchase-orders/${id}/close`, { reason, concurrencyToken }),
};
