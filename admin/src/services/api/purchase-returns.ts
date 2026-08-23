import { apiClient, withPagedDefaults } from "./client";

export interface ReturnableReceiptLine {
  originalLineNumber: number;
  productId: string;
  description: string;
  receivedQuantity: number;
  returnedQuantity: number;
  availableQuantity: number;
  unitCost: number;
  netAmount: number;
  taxAmount: number;
  lineTotal: number;
}

export interface ReturnableReceipt {
  goodsReceiptId: string;
  documentNumber: string;
  warehouseId: string;
  warehouseName: string;
  supplierId: string;
  supplierName: string;
  supplierInvoiceNumber: string | null;
  receivedAt: string;
  currencyCode: string;
  grandTotal: number;
  lines: ReturnableReceiptLine[];
}

export interface ReturnableReceiptListItem {
  goodsReceiptId: string;
  documentNumber: string;
  supplierName: string;
  warehouseName: string;
  supplierInvoiceNumber: string | null;
  receivedAt: string;
  grandTotal: number;
  returnedTotal: number;
  hasAvailableQuantity: boolean;
}

export interface ReturnableReceiptPage {
  items: ReturnableReceiptListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PurchaseReturnAcceptance {
  returnId: string;
  movementId: string;
  documentNumber: string;
  status: string;
  processingSequence: number;
  idempotentReplay: boolean;
}

export interface ConfirmPurchaseReturnRequest {
  returnId: string;
  businessId: string;
  originalGoodsReceiptId: string;
  returnedAt: string;
  reasonCode: string;
  notes: string | null;
  lines: Array<{ originalLineNumber: number; quantity: number }>;
}

export const purchaseReturnsApi = {
  listReceipts: (params: { search?: string; from?: string; to?: string; withAvailableQuantity?: boolean; page: number; pageSize: number }) =>
    apiClient.get<ReturnableReceiptPage>(
      "/commerce/v1/purchase-returns/receipts", withPagedDefaults(params),
    ),
  getReceipt: (goodsReceiptId: string) =>
    apiClient.get<ReturnableReceipt>(
      `/commerce/v1/purchase-returns/receipts/${goodsReceiptId}`,
    ),
  confirm: (request: ConfirmPurchaseReturnRequest) =>
    apiClient.postIdempotent<PurchaseReturnAcceptance>(
      "/commerce/v1/purchase-returns/confirm", request,
      `purchase-return-${request.returnId}`,
    ),
};
