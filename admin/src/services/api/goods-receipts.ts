import { apiClient, withPagedDefaults } from "./client";

export type GoodsReceiptStatus = "Draft" | "Accepted" | "Processed";
export type PurchaseTaxTreatment = "DeductibleInputVat" | "CapitalizedCost" | "NotApplicable";

export interface GoodsReceiptLine {
  lineNumber: number;
  productId: string;
  description: string;
  quantity: number;
  unitCost: number;
  discountAmount: number;
  taxCode: string;
  taxRate: number;
  taxTreatment: PurchaseTaxTreatment;
  presentationName: string;
  baseUnitCode?: string;
  preferredPresentationName?: string;
  preferredUnitsPerPresentation?: number;
  presentationQuantity: number;
  unitsPerPresentation: number;
}

export interface GoodsReceiptLineSnapshot extends GoodsReceiptLine {
  netAmount: number;
  taxAmount: number;
  lineTotal: number;
}

export interface GoodsReceiptDraft {
  draftId: string;
  businessId: string;
  warehouseId: string | null;
  supplierId: string | null;
  supplierInvoiceNumber: string | null;
  supplierInvoiceDate: string | null;
  receivedAt: string;
  createsPayable: boolean;
  dueDate: string | null;
  currencyCode: string;
  notes: string | null;
  netAmount: number;
  taxAmount: number;
  grandTotal: number;
  lines: GoodsReceiptLineSnapshot[];
  updatedAt: string;
  concurrencyToken: string;
}

export interface GoodsReceiptDetail {
  documentId: string;
  documentNumber: string;
  status: Exclude<GoodsReceiptStatus, "Draft">;
  warehouseId: string;
  warehouseName: string;
  supplierId: string;
  supplierName: string;
  supplierInvoiceNumber: string | null;
  supplierInvoiceDate: string | null;
  receivedAt: string;
  createsPayable: boolean;
  dueDate: string | null;
  currencyCode: string;
  notes: string | null;
  netAmount: number;
  taxAmount: number;
  grandTotal: number;
  acceptedAt: string;
  processedAt: string | null;
  lines: GoodsReceiptLineSnapshot[];
}

export interface SaveGoodsReceiptDraftRequest {
  draftId: string;
  businessId: string;
  warehouseId: string | null;
  supplierId: string | null;
  supplierInvoiceNumber: string | null;
  supplierInvoiceDate: string | null;
  receivedAt: string;
  createsPayable: boolean;
  dueDate: string | null;
  currencyCode: string;
  notes: string | null;
  lines: GoodsReceiptLine[];
  concurrencyToken: string | null;
}

export interface GoodsReceiptListItem {
  documentId: string;
  documentNumber: string | null;
  status: GoodsReceiptStatus;
  warehouseId: string | null;
  warehouseName: string | null;
  supplierId: string | null;
  supplierName: string | null;
  supplierInvoiceNumber: string | null;
  receivedAt: string;
  grandTotal: number;
  updatedAt: string;
}

export interface GoodsReceiptPage {
  items: GoodsReceiptListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface GoodsReceiptOptions {
  warehouses: Array<{ warehouseId: string; code: string; name: string }>;
  suppliers: Array<{ supplierId: string; identification: string; name: string }>;
}

export interface GoodsReceiptProduct {
  productId: string;
  productCode: string;
  reference: string | null;
  name: string;
  supplierProductCode: string | null;
  latestUnitCost: number | null;
  taxCode: string;
  taxRate: number;
  taxTreatment: PurchaseTaxTreatment;
  barcodes: string[];
  baseUnitCode: string;
  isAssociated: boolean;
  purchasePresentationName: string;
  unitsPerPresentation: number;
  isPrimary: boolean;
}

export interface GoodsReceiptProductPage {
  items: GoodsReceiptProduct[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface GoodsReceiptAcceptance {
  documentId: string;
  movementId: string;
  documentNumber: string;
  status: string;
  processingSequence: number;
  idempotentReplay: boolean;
}

export const goodsReceiptsApi = {
  options: () => apiClient.get<GoodsReceiptOptions>("/commerce/v1/goods-receipts/options"),
  products: (supplierId: string, search?: string, includeUnassociated = false, page = 1, pageSize = 50) =>
    apiClient.get<GoodsReceiptProductPage>("/commerce/v1/goods-receipts/products", {
      supplierId, search, includeUnassociated, page, pageSize,
    }),
  associateProduct: (request: {
    supplierId: string; productId: string; supplierProductCode: string | null; isPrimary: boolean;
    purchasePresentationName: string; unitsPerPresentation: number;
  }) => apiClient.post<GoodsReceiptProduct>(
    "/commerce/v1/goods-receipts/supplier-products", request,
  ),
  list: (params: { search?: string; status?: GoodsReceiptStatus; page: number; pageSize: number }) =>
    apiClient.get<GoodsReceiptPage>("/commerce/v1/goods-receipts", withPagedDefaults(params)),
  getDraft: (draftId: string) =>
    apiClient.get<GoodsReceiptDraft>(`/commerce/v1/goods-receipts/drafts/${draftId}`),
  getDetail: (documentId: string) =>
    apiClient.get<GoodsReceiptDetail>(`/commerce/v1/goods-receipts/${documentId}`),
  saveDraft: (request: SaveGoodsReceiptDraftRequest) =>
    apiClient.put<GoodsReceiptDraft>(`/commerce/v1/goods-receipts/drafts/${request.draftId}`, request),
  deleteDraft: (draftId: string, concurrencyToken: string) =>
    apiClient.delete<{ deleted: boolean }>(
      `/commerce/v1/goods-receipts/drafts/${draftId}?concurrencyToken=${encodeURIComponent(concurrencyToken)}`,
    ),
  confirm: (request: {
    documentId: string; businessId: string; warehouseId: string; supplierId: string;
    supplierInvoiceNumber: string | null; supplierInvoiceDate: string | null;
    receivedAt: string; createsPayable: boolean; dueDate: string | null;
    currencyCode: string; notes: string | null; lines: GoodsReceiptLine[];
    draftConcurrencyToken: string | null;
  }) => apiClient.postIdempotent<GoodsReceiptAcceptance>(
    "/commerce/v1/goods-receipts/confirm", request, `goods-receipt-${request.documentId}`,
  ),
};
