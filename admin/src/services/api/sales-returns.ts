import { apiClient, withPagedDefaults } from "./client";

export type SalesReturnResolution = "Refund" | "CustomerCredit";
export type SalesReturnRefundMethod = "Cash" | "Transfer" | "DebitCard" | "CreditCard";
export type SalesReturnScope = "FullCancellation" | "Partial";

export interface ReturnableSaleListItem {
  documentId: string;
  documentNumber: string;
  fiscalNumber: string;
  cufe: string;
  issuedAt: string;
  customerId: string | null;
  customerName: string;
  customerIdentification: string;
  warehouseId: string;
  warehouseName: string;
  totalAmount: number;
  returnedAmount: number;
  hasAvailableQuantity: boolean;
  fiscalStatus: string;
}

export interface ReturnableSalePage {
  items: ReturnableSaleListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ReturnableSale {
  documentId: string;
  documentNumber: string;
  fiscalNumber: string;
  cufe: string;
  issuedAt: string;
  customerId: string | null;
  customerName: string;
  customerIdentification: string;
  warehouseId: string;
  warehouseName: string;
  totalAmount: number;
  returnedAmount: number;
  receivableOutstanding: number;
  fiscalStatus: string;
  payments: Array<{
    paymentNumber: number;
    methodCode: string;
    originalAmount: number;
    refundedAmount: number;
    availableAmount: number;
  }>;
  lines: Array<{
    originalLineNumber: number;
    productId: string;
    productCode: string;
    reference: string | null;
    description: string;
    soldQuantity: number;
    returnedQuantity: number;
    availableQuantity: number;
    unitPrice: number;
    discountAmount: number;
    taxCode: string;
    taxRate: number;
    untaxedAmount: number;
    taxAmount: number;
    lineTotal: number;
    barcodes: string;
  }>;
}

export interface ConfirmSalesReturnRequest {
  returnId: string;
  businessId: string;
  warehouseId: string;
  originalDocumentId: string;
  returnedAt: string;
  returnScopeCode: SalesReturnScope;
  economicResolution: SalesReturnResolution;
  refundMethodCode: SalesReturnRefundMethod | null;
  reasonDescription: string;
  lines: Array<{
    originalLineNumber: number;
    quantity: number;
    inventoryDisposition: "Sellable";
  }>;
  workSessionId: string | null;
  originalPaymentNumber: number | null;
  reasonCode: string;
  notes: string | null;
}

export interface SalesReturnAcceptance {
  returnId: string;
  movementId: string;
  documentNumber: string;
  status: string;
  processingSequence: number;
  idempotentReplay: boolean;
}

export interface WorkSessionView {
  workSessionId: string;
  businessId: string;
  warehouseId: string;
  userId: string;
  status: string;
}

export const salesReturnsApi = {
  listSales: (params: {
    page?: number;
    pageSize?: number;
    search?: string;
    customer?: string;
    from?: string;
    to?: string;
    withAvailableQuantity?: boolean;
  }) => apiClient.get<ReturnableSalePage>(
    "/commerce/v1/sales-returns/sales",
    withPagedDefaults(params),
  ),
  getSale: (documentId: string) =>
    apiClient.get<ReturnableSale>(`/commerce/v1/sales-returns/sales/${documentId}`),
  openWorkSession: (businessId: string, warehouseId: string) =>
    apiClient.post<WorkSessionView>("/commerce/v1/work-sessions/current", {
      businessId,
      warehouseId,
      deviceId: null,
    }),
  confirm: (request: ConfirmSalesReturnRequest) =>
    apiClient.postIdempotent<SalesReturnAcceptance>(
      "/commerce/v1/sales-returns/confirm",
      request,
      `sales-return-${request.returnId}`,
    ),
};
