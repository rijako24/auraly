import { apiClient, withPagedDefaults } from "./client";

export type ReceivableStatus = "Open" | "PartiallyPaid" | "Paid" | "Cancelled";
export type CustomerPaymentMethod = "Cash" | "BankTransfer" | "DebitCard" | "CreditCard";

export interface ReceivableListItem {
  receivableId: string;
  customerId: string;
  customerName: string;
  partySiteId: string | null;
  partySiteName: string | null;
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
  notes: string | null;
  allocations: Array<{ receivableId: string; amount: number }>;
  payments: CustomerPaymentTender[];
}
export interface CustomerPaymentTender { methodCode:CustomerPaymentMethod;amount:number;tenderedAmount:number|null;bankAccountId:string|null;reference:string|null;notes:string|null;cardFranchiseCode:string|null;approvalNumber:string|null }

export interface CustomerPaymentAcceptance {
  paymentId: string;
  accountingJobId: string;
  documentNumber: string;
  status: string;
  idempotentReplay: boolean;
}
export interface CustomerPaymentHistoryPage {
  items:Array<{paymentId:string;documentNumber:string;paidAt:string;currencyCode:string;totalAmount:number;status:string;appliedDocumentCount:number;payments:CustomerPaymentTender[];applications:Array<{receivableId:string;documentNumber:string;amount:number}>;customerId:string|null;customerName:string|null}>;
  page:number;pageSize:number;totalCount:number;totalPages:number;
}
export interface CustomerPortfolioPage {items:Array<{customerId:string;customerName:string;identification:string;invoiceCount:number;originalAmount:number;paidAmount:number;outstandingAmount:number;overdueAmount:number}>;page:number;pageSize:number;totalCount:number;totalPages:number;totalOutstanding:number;totalOverdue:number;totalInvoiceCount:number}
export interface ImportPreexistingReceivablesRequest {businessId:string;items:Array<{receivableId:string;customerId:string|null;customerIdentification:string|null;partySiteId:string|null;documentNumber:string;issuedAt:string;dueDate:string;amount:number;counterpartAccountId:string;notes:string|null}>}

export interface PaymentSettlementConfiguration {
  isAccountingEnabled: boolean;
  bankAccounts: Array<{ bankAccountId: string; displayName: string; isPrimary: boolean }>;
}

export interface CustomerCreditProfile {
  customerId: string;
  creditLimit: number | null;
  defaultDueDays: number;
  isCreditEnabled: boolean;
  outstandingAmount: number;
  availableCredit: number | null;
}

export const receivablesApi = {
  customerPortfolio:(params:{page?:number;pageSize?:number;search?:string;overdue?:boolean;customerId?:string;status?:ReceivableStatus;from?:string;to?:string})=>apiClient.get<CustomerPortfolioPage>("/commerce/v1/receivables/customers",withPagedDefaults(params)),
  payments:(params:{page?:number;pageSize?:number;search?:string;customerId?:string;status?:ReceivableStatus;overdue?:boolean;from?:string;to?:string})=>apiClient.get<CustomerPaymentHistoryPage>("/commerce/v1/receivable-payments",withPagedDefaults(params)),
  currentWorkSession:(businessId:string)=>apiClient.post<{workSessionId:string}>("/commerce/v1/work-sessions/current",{businessId,warehouseId:null,deviceId:null}),
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
    isCreditEnabled: boolean;
  }) => apiClient.put<CustomerCreditProfile>(`/commerce/v1/customers/${customerId}/credit`, request),
  list: (params: {
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
