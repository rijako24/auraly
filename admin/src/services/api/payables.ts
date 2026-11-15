import { apiClient, withPagedDefaults } from "./client";
import type { PaymentSettlementConfiguration } from "./accounting";

export type PayableStatus = "Open" | "PartiallyPaid" | "Paid" | "Cancelled";

export interface PayableListItem {
  dueOn?: string | null; issuedOn?: string | null;
  sourceDocumentType?: string | null;
  payableId: string;
  supplierId: string;
  supplierName: string;
  documentNumber: string;
  currencyCode: string;
  originalAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  dueDate: string;
  status: PayableStatus;
  isOverdue: boolean;
  overdueDays?: number;
  agingBandCode?: string;
  issuedAt?: string | null;
  adjustmentAmount?: number;
  createdAt: string;
  expenseConceptName: string | null;
  partySiteId: string | null;
  partySiteName: string | null;
  financialDiscountEligibleAmount?: number | null;
  financialDiscountAnticipated?: boolean | null;
  financialDiscountPendingReturn?: boolean;
}

export interface PayablePage {
  items: PayableListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  totalOutstanding: number;
  totalOverdue: number;
  referenceDate?: string | null;
  currencyTotals: Array<{ currencyCode: string; outstandingAmount: number; overdueAmount: number;originalAmount?:number|null;paidAmount?:number|null;adjustmentAmount?:number|null }>;
}

export interface PayableTransaction {
  transactionId: string;
  type: string;
  amount: number;
  balanceChange: number;
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
  partySiteId: string | null;
  partySiteName: string | null;
}

export interface ConfirmSupplierPaymentRequest {
  paymentId: string;
  businessId: string;
  supplierId: string;
  paidOn?: string;
  paidAt: string;
  currencyCode: string;
  notes: string | null;
  allocations: Array<{ payableId: string; amount: number; financialDiscountEarned?: boolean | null }>;
  payments: SupplierPaymentTender[];
  workSessionId: string | null;
}
export interface SupplierPaymentTender {paymentMethodId?:string|null;paymentMethodVersion?:number|null;paymentMethodCode?:string|null;paymentMethodName?:string|null;accountId?:string|null;methodCode:"Cash"|"Transfer"|"DebitCard"|"CreditCard";amount:number;tenderedAmount:number|null;bankAccountId:string|null;reference:string|null;notes:string|null;cardFranchiseCode:string|null;approvalNumber:string|null}

export type { PaymentSettlementConfiguration } from "./accounting";

export interface SupplierPaymentAcceptance {
  paidAt?:string|null;
  paymentId: string;
  accountingJobId: string;
  documentNumber: string;
  status: string;
  idempotentReplay: boolean;
}
export interface SupplierPaymentHistoryPage {
  items:Array<{paymentId:string;sourceDocumentId?:string;sourceDocumentType?:string;sourceGroupId?:string;documentNumber:string;paidAt:string;currencyCode:string;totalAmount:number;status:string;appliedDocumentCount:number;payments:SupplierPaymentTender[];applications:Array<{payableId:string;documentNumber:string;amount:number}>;supplierId:string|null;supplierName:string|null}>;
  page:number;pageSize:number;totalCount:number;totalPages:number;
}
export interface SupplierPortfolioPage {items:Array<{supplierId:string;supplierName:string;identification:string;invoiceCount:number;originalAmount:number;paidAmount:number;outstandingAmount:number;overdueAmount:number;supplierCreditAmount:number;currencyCode:string;partySiteId:string|null;partySiteName:string|null}>;page:number;pageSize:number;totalCount:number;totalPages:number;totalOutstanding:number;totalOverdue:number;totalInvoiceCount:number;totalSupplierCredit:number;currencyTotals:Array<{currencyCode:string;outstandingAmount:number;overdueAmount:number}>}
export interface PayablesReportItem {
  supplierId: string; supplierName: string; identification: string;
  partySiteId: string | null; partySiteName: string | null; currencyCode: string;
  invoiceCount: number; originalAmount: number; paidAmount: number;
  outstandingAmount: number; overdueAmount: number; otherImpact: number; payableId: string | null;
  documentNumber: string | null; issuedAt: string | null; dueDate: string | null;
  applications: Array<{documentNumber:string;appliedAt:string;amount:number}> | null;
  notDueAmount:number;overdue1To30Amount:number;overdue31To60Amount:number;
  overdue61To90Amount:number;overdueOver90Amount:number;
}
export interface PayablesReportPage {
  items: PayablesReportItem[]; currencyTotals: Array<{currencyCode:string;
    invoiceCount:number;originalAmount:number;paidAmount:number;
    outstandingAmount:number;overdueAmount:number;otherImpact:number;
    notDueAmount:number;overdue1To30Amount:number;overdue31To60Amount:number;
    overdue61To90Amount:number;overdueOver90Amount:number}>;
  page: number; pageSize: number; totalCount: number; totalPages: number;
}
export interface PayablesReportFilters {
  includeApplications?: boolean;
  page: number; pageSize: number; consolidated: boolean; cutoff: string;
  supplierId?: string; partySiteId?: string; from?: string; to?: string; search?: string; conceptId?: string;
  status?: PayableStatus; outstandingOnly?: boolean; overdueOnly?: boolean;
  sortBy?: string; sortDirection?: "asc" | "desc";
}

export const payablesApi = {
  report: (filters: PayablesReportFilters, signal?: AbortSignal) =>
    apiClient.get<PayablesReportPage>("/commerce/v1/payables/report", { ...filters, dateRangeVersion: 2 }, {signal}),
  printReport: (filters: PayablesReportFilters) =>
    apiClient.getText("/commerce/v1/payables/report/print", { ...filters, dateRangeVersion: 2, page: undefined, pageSize: undefined }),
  exportReport: (filters: PayablesReportFilters) =>
    apiClient.get<PayablesReportPage>("/commerce/v1/payables/report/export", { ...filters, dateRangeVersion: 2, page: 1, pageSize: 100 }),
  expenseConcepts: (search: string, page: number, pageSize: number) =>
    apiClient.get<{ items: Array<{ conceptId: string; name: string }>; page: number; pageSize: number; totalCount: number; totalPages: number }>(
      "/commerce/v1/payables/expense-concepts", { search: search || undefined, page, pageSize }),
  supplierPortfolio:(params:{page?:number;pageSize?:number;search?:string;overdue?:boolean;supplierId?:string;partySiteId?:string;status?:PayableStatus;from?:string;to?:string;sortBy?:string;sortDirection?:"asc"|"desc"})=>apiClient.get<SupplierPortfolioPage>("/commerce/v1/payables/suppliers",withPagedDefaults({ ...params, dateRangeVersion: 2 })),
  payments:(params:{page?:number;pageSize?:number;search?:string;supplierId?:string;partySiteId?:string;status?:PayableStatus;overdue?:boolean;from?:string;to?:string;sortBy?:string;sortDirection?:"asc"|"desc"})=>apiClient.get<SupplierPaymentHistoryPage>("/commerce/v1/payable-payments",withPagedDefaults({ ...params, dateRangeVersion: 2 })),
  settlementConfiguration: () =>
    apiClient.get<PaymentSettlementConfiguration>("/commerce/v1/pos/settlement-configuration"),
  paymentHistory: (supplierId:string,page=1,pageSize=5) =>
    apiClient.get<SupplierPaymentHistoryPage>(`/commerce/v1/suppliers/${supplierId}/payments`,{page,pageSize}),
  list: (params: {
    payableId?: string;
    page?: number;
    pageSize?: number;
    search?: string;
    supplierId?: string;
    partySiteId?: string;
    status?: PayableStatus;
    overdue?: boolean;
    outstandingOnly?: boolean;
    from?: string;
    to?: string;
    conceptId?: string;
    sortBy?: string;
    sortDirection?: "asc" | "desc";
  }, signal?: AbortSignal) => apiClient.get<PayablePage>(
    "/commerce/v1/payables",
    withPagedDefaults({ ...params, dateRangeVersion: 2 }),
    {signal},
  ),
  posList: (params: {page:number;pageSize:number;supplierId:string;outstandingOnly:true}) =>
    apiClient.get<PayablePage>("/commerce/v1/pos/payables",params),
  get: (payableId: string) =>
    apiClient.get<PayableDetail>(`/commerce/v1/payables/${payableId}`),
  confirmPayment: (request: ConfirmSupplierPaymentRequest, idempotencyKey: string) =>
    apiClient.postIdempotent<SupplierPaymentAcceptance>(
      "/commerce/v1/payable-payments/confirm",
      request,
      idempotencyKey,
    ),
  confirmPosPayment: (request: ConfirmSupplierPaymentRequest, idempotencyKey: string) =>
    apiClient.postIdempotent<SupplierPaymentAcceptance>(
      "/commerce/v1/pos/payable-payments/confirm",request,idempotencyKey),
};
