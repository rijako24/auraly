import type { PaymentSettlementConfiguration } from "./accounting";
import { apiClient, withPagedDefaults } from "./client";

export type ReceivableStatus = "Open" | "PartiallyPaid" | "Paid" | "Cancelled";
export type CustomerPaymentMethod = "Cash" | "Transfer" | "DebitCard" | "CreditCard";

export interface ReceivableListItem {
  dueOn?: string | null; issuedOn?: string | null;
  sourceDocumentType?: string | null;
  receivableId: string;
  customerId: string | null;
  partyId?: string | null;
  customerName: string;
  partySiteId: string | null;
  partySiteName: string | null;
  documentNumber: string;
  currencyCode: string;
  originalAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  dueDate: string;
  status: ReceivableStatus;
  isOverdue: boolean;
  overdueDays?: number;
  agingBandCode?: string;
  issuedAt?: string | null;
  adjustmentAmount?: number;
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
  referenceDate?: string | null;
  totalAdjustments?: number;
  totalOriginal: number;
  totalPaid: number;
}

export interface ReceivableDetail {
  receivableId: string;
  documentNumber: string;
  customerId: string | null;
  partyId?: string | null;
  customerName: string;
  partySiteId: string | null;
  partySiteName: string | null;
  dueDate: string;
  originalAmount: number;
  outstandingAmount: number;
  status: ReceivableStatus;
  currencyCode: string;
  customerIdentification: string;
  sourceDocumentId: string;
  sourceDocumentType: string;
  sourceNote?: string | null;
  transactions: Array<{
    transactionId: string;
    type: string;
    amount: number;
    balanceChange: number;
    sourceDocumentId: string;
    occurredAt: string;
  }>;
}

export interface ConfirmCustomerPaymentRequest {
  paymentId: string;
  businessId: string;
  customerId: string;
  workSessionId: string | null;
  paidOn?: string;
  paidAt: string;
  currencyCode: string;
  notes: string | null;
  allocations: Array<{ receivableId: string; amount: number }>;
  payments: CustomerPaymentTender[];
}
export interface CustomerPaymentTender { paymentMethodId?:string|null;paymentMethodVersion?:number|null;paymentMethodCode?:string|null;paymentMethodName?:string|null;accountId?:string|null; methodCode:CustomerPaymentMethod;amount:number;tenderedAmount:number|null;bankAccountId:string|null;reference:string|null;notes:string|null;cardFranchiseCode:string|null;approvalNumber:string|null }

export interface CustomerPaymentAcceptance {
  paidAt?:string|null;
  paymentId: string;
  accountingJobId: string;
  documentNumber: string;
  status: string;
  idempotentReplay: boolean;
}
export interface CustomerPaymentHistoryPage {
  items:Array<{paymentId:string;sourceDocumentId?:string;sourceDocumentType?:string;sourceGroupId?:string;documentNumber:string;paidAt:string;currencyCode:string;totalAmount:number;status:string;appliedDocumentCount:number;payments:CustomerPaymentTender[];applications:Array<{receivableId:string;documentNumber:string;amount:number}>;customerId:string|null;customerName:string|null}>;
  page:number;pageSize:number;totalCount:number;totalPages:number;totalAmount:number;
}
export interface CustomerPortfolioPage {items:Array<{customerId:string;customerName:string;identification:string;invoiceCount:number;originalAmount:number;paidAmount:number;outstandingAmount:number;overdueAmount:number;partySiteId:string|null;partySiteName:string|null}>;page:number;pageSize:number;totalCount:number;totalPages:number;totalOutstanding:number;totalOverdue:number;totalInvoiceCount:number;totalOriginal:number;totalPaid:number}
export interface AvailableCreditAmounts { availableAdvances?: number; availableCredits?: number; netAmount?: number }
export interface ReceivablesReportItem extends AvailableCreditAmounts {
  internalDocumentNumber?: string | null;
  isCreditBalance?: boolean;
  termDays?: number | null; ageDays?: number | null; daysPastDue?: number | null;
  legalName?: string | null; address?: string | null; city?: string | null;
  phone?: string | null; email?: string | null; creditLimit?: number | null;
  groupTotals?: {invoiceCount:number;originalAmount:number;paidAmount:number;otherImpact:number;
    outstandingAmount:number;notDueAmount:number;overdue1To30Amount:number;overdue31To60Amount:number;
    overdue61To90Amount:number;overdueOver90Amount:number} & AvailableCreditAmounts | null;
  customerId: string | null; partyId?: string | null; customerName: string; identification: string;
  partySiteId: string | null; partySiteName: string | null; currencyCode: string;
  invoiceCount: number; originalAmount: number; paidAmount: number;
  outstandingAmount: number; overdueAmount: number; otherImpact: number; receivableId: string | null;
  documentNumber: string | null; issuedAt: string | null; dueDate: string | null;
  applications: Array<{documentNumber:string;appliedAt:string;amount:number}> | null;
  notDueAmount:number;overdue1To30Amount:number;overdue31To60Amount:number;
  overdue61To90Amount:number;overdueOver90Amount:number;
}
export interface ReceivablesReportPage {
  totalAvailableAdvances?:number;totalAvailableCredits?:number;totalNetAmount?:number;
  items: ReceivablesReportItem[]; page: number; pageSize: number; totalCount: number;
  totalPages: number; totalInvoiceCount: number; totalOriginal: number;
  totalPaid: number; totalOutstanding: number; totalOverdue: number; totalOtherImpact: number;
  totalNotDue:number;totalOverdue1To30:number;totalOverdue31To60:number;
  totalOverdue61To90:number;totalOverdueOver90:number;
}
export interface ReceivablesReportFilters {
  documentNumber?: string; issuedFrom?: string; issuedTo?: string;
  minimumAgeDays?: number; maximumAgeDays?: number;
  minimumDaysPastDue?: number; maximumDaysPastDue?: number;
  includeApplications?: boolean;
  page: number; pageSize: number; consolidated: boolean; cutoff: string;
  customerId?: string; partySiteId?: string; from?: string; to?: string; search?: string;
  status?: ReceivableStatus; outstandingOnly?: boolean; overdueOnly?: boolean;
  sortBy?: string; sortDirection?: "asc" | "desc";
}
export interface ImportPreexistingReceivablesRequest {businessId:string;items:Array<{receivableId:string;customerId:string|null;customerIdentification:string|null;partySiteId:string|null;documentNumber:string;issuedAt:string;dueDate:string;amount:number;counterpartAccountId:string;notes:string|null}>}

export type { PaymentSettlementConfiguration } from "./accounting";

export interface CustomerCreditProfile {
  customerId: string;
  creditLimit: number | null;
  defaultDueDays: number;
  maximumOverdueInvoices?: number | null;
  isCreditEnabled: boolean;
  outstandingAmount: number;
  availableCredit: number | null;
  pendingCreditAmount?: number;
}

export const receivablesApi = {
  report: (filters: ReceivablesReportFilters) =>
    apiClient.get<ReceivablesReportPage>("/commerce/v1/receivables/report", { ...filters, templateVersion: 4, dateRangeVersion: 2 }),
  printReport: (filters: ReceivablesReportFilters) =>
    apiClient.getText("/commerce/v1/receivables/report/print", { ...filters, templateVersion: 4, dateRangeVersion: 2, page: undefined, pageSize: undefined }),
  exportReport: (filters: ReceivablesReportFilters) =>
    apiClient.get<ReceivablesReportPage>("/commerce/v1/receivables/report/export", { ...filters, templateVersion: 4, dateRangeVersion: 2, page: 1, pageSize: 100 }),
  customerPortfolio:(params:{page?:number;pageSize?:number;search?:string;overdue?:boolean;customerId?:string;partySiteId?:string;status?:ReceivableStatus;from?:string;to?:string;sortBy?:string;sortDirection?:"asc"|"desc"})=>apiClient.get<CustomerPortfolioPage>("/commerce/v1/receivables/customers",withPagedDefaults({ ...params, dateRangeVersion: 2 })),
  payments:(params:{page?:number;pageSize?:number;search?:string;customerId?:string;partySiteId?:string;status?:ReceivableStatus;overdue?:boolean;from?:string;to?:string;sortBy?:string;sortDirection?:"asc"|"desc"})=>apiClient.get<CustomerPaymentHistoryPage>("/commerce/v1/receivable-payments",withPagedDefaults({ ...params, dateRangeVersion: 2 })),
  importPreexisting:(request:ImportPreexistingReceivablesRequest)=>apiClient.post<{acceptedCount:number;receivableIds:string[]}>("/commerce/v1/receivables/preexisting/import",request),
  settlementConfiguration: () =>
    apiClient.get<PaymentSettlementConfiguration>("/commerce/v1/pos/settlement-configuration"),
  getCreditProfile: (customerId: string) =>
    apiClient.get<CustomerCreditProfile>(`/commerce/v1/customers/${customerId}/credit`),
  paymentHistory: (customerId:string,page=1,pageSize=5) =>
    apiClient.get<CustomerPaymentHistoryPage>(`/commerce/v1/customers/${customerId}/payments`,{page,pageSize}),
  updateCreditProfile: (customerId: string, request: {
    businessId: string;
    creditLimit: number | null;
    defaultDueDays: number;
    maximumOverdueInvoices?: number | null;
    isCreditEnabled: boolean;
  }) => apiClient.put<CustomerCreditProfile>(`/commerce/v1/customers/${customerId}/credit`, request),
  list: (params: {
    receivableId?: string;
    page?: number;
    pageSize?: number;
    search?: string;
    customerId?: string;
    partySiteId?: string;
    status?: ReceivableStatus;
    overdue?: boolean;
    outstandingOnly?: boolean;
    from?: string;
    to?: string;
    sortBy?: string;
    sortDirection?: "asc" | "desc";
  }) => apiClient.get<ReceivablePage>(
    "/commerce/v1/receivables",
    withPagedDefaults({ ...params, dateRangeVersion: 2 }),
  ),
  posList: (params: {page:number;pageSize:number;customerId:string;outstandingOnly:true}) =>
    apiClient.get<ReceivablePage>("/commerce/v1/pos/receivables",params),
  get: (receivableId: string) =>
    apiClient.get<ReceivableDetail>(`/commerce/v1/receivables/${receivableId}`),
  confirmPayment: (request: ConfirmCustomerPaymentRequest, idempotencyKey: string) =>
    apiClient.postIdempotent<CustomerPaymentAcceptance>(
      "/commerce/v1/receivable-payments/confirm",
      request,
      idempotencyKey,
    ),
  confirmPosPayment: (request: ConfirmCustomerPaymentRequest, idempotencyKey: string) =>
    apiClient.postIdempotent<CustomerPaymentAcceptance>(
      "/commerce/v1/pos/receivable-payments/confirm",request,idempotencyKey),
};
