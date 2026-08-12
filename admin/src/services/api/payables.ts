import { apiClient, withPagedDefaults } from "./client";

export type PayableStatus = "Open" | "PartiallyPaid" | "Paid" | "Cancelled";

export interface PayableListItem {
  payableId: string;
  supplierId: string;
  supplierName: string;
  documentNumber: string;
  currencyCode: string;
  originalAmount: number;
  outstandingAmount: number;
  dueDate: string;
  status: PayableStatus;
  isOverdue: boolean;
  createdAt: string;
}

export interface PayablePage {
  items: PayableListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  totalOutstanding: number;
  totalOverdue: number;
}

export interface PayableTransaction {
  transactionId: string;
  type: string;
  amount: number;
  sourceDocumentId: string;
  occurredAt: string;
}

export interface PayableDetail {
  payableId: string;
  supplierId: string;
  supplierName: string;
  supplierIdentification: string;
  sourceDocumentId: string;
  sourceDocumentType: string;
  documentNumber: string;
  currencyCode: string;
  originalAmount: number;
  outstandingAmount: number;
  dueDate: string;
  status: PayableStatus;
  transactions: PayableTransaction[];
}

export interface ConfirmSupplierPaymentRequest {
  paymentId: string;
  businessId: string;
  supplierId: string;
  paidAt: string;
  currencyCode: string;
  paymentMethod: "Cash" | "BankTransfer";
  reference: string | null;
  notes: string | null;
  allocations: Array<{ payableId: string; amount: number }>;
}

export interface SupplierPaymentAcceptance {
  paymentId: string;
  movementId: string;
  documentNumber: string;
  status: string;
  processingSequence: number;
  idempotentReplay: boolean;
}

export const payablesApi = {
  list: (params: {
    page?: number;
    pageSize?: number;
    search?: string;
    supplierId?: string;
    status?: PayableStatus;
    overdue?: boolean;
  }) => apiClient.get<PayablePage>(
    "/commerce/v1/payables",
    withPagedDefaults(params),
  ),
  get: (payableId: string) =>
    apiClient.get<PayableDetail>(`/commerce/v1/payables/${payableId}`),
  confirmPayment: (request: ConfirmSupplierPaymentRequest, idempotencyKey: string) =>
    apiClient.postIdempotent<SupplierPaymentAcceptance>(
      "/commerce/v1/payable-payments/confirm",
      request,
      idempotencyKey,
    ),
};
