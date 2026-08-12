import { apiClient, withPagedDefaults } from "./client";

export type ReceivableStatus = "Open" | "PartiallyPaid" | "Paid" | "Cancelled";
export type CustomerPaymentMethod = "Cash" | "BankTransfer" | "DebitCard" | "CreditCard";

export interface ReceivableListItem {
  receivableId: string;
  customerId: string;
  customerName: string;
  documentNumber: string;
  currencyCode: string;
  originalAmount: number;
  outstandingAmount: number;
  dueDate: string;
  status: ReceivableStatus;
  isOverdue: boolean;
  createdAt: string;
}

export interface ReceivablePage {
  items: ReceivableListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  totalOutstanding: number;
  totalOverdue: number;
}

export interface ReceivableDetail {
  receivableId: string;
  documentNumber: string;
  customerId: string;
  customerName: string;
  dueDate: string;
  originalAmount: number;
  outstandingAmount: number;
  status: ReceivableStatus;
  currencyCode: string;
  customerIdentification: string;
  sourceDocumentId: string;
  sourceDocumentType: string;
  transactions: Array<{
    transactionId: string;
    type: string;
    amount: number;
    sourceDocumentId: string;
    occurredAt: string;
  }>;
}

export interface ConfirmCustomerPaymentRequest {
  paymentId: string;
  businessId: string;
  customerId: string;
  workSessionId: string | null;
  paidAt: string;
  currencyCode: string;
  paymentMethod: CustomerPaymentMethod;
  reference: string | null;
  notes: string | null;
  allocations: Array<{ receivableId: string; amount: number }>;
}

export interface CustomerPaymentAcceptance {
  paymentId: string;
  movementId: string;
  documentNumber: string;
  status: string;
  processingSequence: number;
  idempotentReplay: boolean;
}

export const receivablesApi = {
  list: (params: {
    page?: number;
    pageSize?: number;
    search?: string;
    customerId?: string;
    status?: ReceivableStatus;
    overdue?: boolean;
  }) => apiClient.get<ReceivablePage>(
    "/commerce/v1/receivables",
    withPagedDefaults(params),
  ),
  get: (receivableId: string) =>
    apiClient.get<ReceivableDetail>(`/commerce/v1/receivables/${receivableId}`),
  confirmPayment: (request: ConfirmCustomerPaymentRequest, idempotencyKey: string) =>
    apiClient.postIdempotent<CustomerPaymentAcceptance>(
      "/commerce/v1/receivable-payments/confirm",
      request,
      idempotencyKey,
    ),
};
