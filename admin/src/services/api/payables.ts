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
  expenseConceptName: string | null;
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
  expenseConceptName: string | null;
  expenseDescription: string | null;
  sourceInvoiceNumber: string | null;
  goodsReceiptId: string | null;
}

export interface ConfirmSupplierPaymentRequest {
  paymentId: string;
  businessId: string;
  supplierId: string;
  paidAt: string;
  currencyCode: string;
  notes: string | null;
  allocations: Array<{ payableId: string; amount: number }>;
  payments: SupplierPaymentTender[];
  workSessionId: string | null;
}
export interface SupplierPaymentTender {methodCode:"Cash"|"BankTransfer"|"DebitCard"|"CreditCard";amount:number;tenderedAmount:number|null;bankAccountId:string|null;reference:string|null;notes:string|null;cardFranchiseCode:string|null;approvalNumber:string|null}

export interface PaymentSettlementConfiguration {
  isAccountingEnabled: boolean;
  bankAccounts: Array<{ bankAccountId: string; displayName: string; isPrimary: boolean }>;
}

export interface SupplierPaymentAcceptance {
  paymentId: string;
  accountingJobId: string;
  documentNumber: string;
  status: string;
  idempotentReplay: boolean;
}
export interface SupplierPaymentHistoryPage {
  items:Array<{paymentId:string;documentNumber:string;paidAt:string;currencyCode:string;totalAmount:number;status:string;appliedDocumentCount:number;payments:SupplierPaymentTender[];applications:Array<{payableId:string;documentNumber:string;amount:number}>;supplierId:string|null;supplierName:string|null}>;
  page:number;pageSize:number;totalCount:number;totalPages:number;
}
export interface SupplierPortfolioPage {items:Array<{supplierId:string;supplierName:string;identification:string;invoiceCount:number;originalAmount:number;paidAmount:number;outstandingAmount:number;overdueAmount:number;supplierCreditAmount:number}>;page:number;pageSize:number;totalCount:number;totalPages:number;totalOutstanding:number;totalOverdue:number;totalInvoiceCount:number;totalSupplierCredit:number}

export const payablesApi = {
  expenseConcepts: (search: string, page: number, pageSize: number) =>
    apiClient.get<{ items: Array<{ conceptId: string; name: string }>; page: number; pageSize: number; totalCount: number; totalPages: number }>(
      "/commerce/v1/payables/expense-concepts", { search: search || undefined, page, pageSize }),
  supplierPortfolio:(params:{page?:number;pageSize?:number;search?:string;overdue?:boolean;supplierId?:string;status?:PayableStatus;from?:string;to?:string})=>apiClient.get<SupplierPortfolioPage>("/commerce/v1/payables/suppliers",withPagedDefaults(params)),
  payments:(params:{page?:number;pageSize?:number;search?:string;supplierId?:string;status?:PayableStatus;overdue?:boolean;from?:string;to?:string})=>apiClient.get<SupplierPaymentHistoryPage>("/commerce/v1/payable-payments",withPagedDefaults(params)),
  settlementConfiguration: () =>
    apiClient.get<PaymentSettlementConfiguration>("/commerce/v1/pos/settlement-configuration"),
  paymentHistory: (supplierId:string,page=1,pageSize=5) =>
    apiClient.get<SupplierPaymentHistoryPage>(`/commerce/v1/suppliers/${supplierId}/payments`,{page,pageSize}),
  list: (params: {
    page?: number;
    pageSize?: number;
    search?: string;
    supplierId?: string;
    status?: PayableStatus;
    overdue?: boolean;
    outstandingOnly?: boolean;
    from?: string;
    to?: string;
    conceptId?: string;
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
