import { apiClient } from "./client";

export interface BillableServiceItem {
  billableServiceId: string;
  code: string;
  name: string;
  description?: string | null;
  unitLabel: string;
  ublUnitCode: string;
  unitPrice: number;
  taxCode: string;
  taxName: string;
  taxRate: number;
}

export interface ServiceInvoiceCustomerItem {
  customerId: string;
  identification: string;
  displayName: string;
  email?: string | null;
}

export interface ServiceInvoicePage<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface IssueServiceInvoiceLine {
  billableServiceId: string;
  quantity: number;
  description?: string;
  unitPrice?: number;
  discountKind?: "Percentage" | "Value";
  discountValue: number;
}

export interface IssuedServiceInvoice {
  documentId: string;
  documentNumber: string;
  fiscalNumber: string;
  cufe: string;
  untaxedAmount: number;
  taxAmount: number;
  payableAmount: number;
  creditAmount: number;
  fiscalStatus: string;
  isReplay: boolean;
}

export interface ServiceInvoiceHistoryItem {
  documentId: string;
  documentNumber: string;
  fiscalNumber: string;
  issuedAt: string;
  customerIdentification: string;
  customerName: string;
  payableAmount: number;
  creditAmount: number;
  fiscalStatus: string;
}

export interface ServiceInvoiceDetailLine {
  lineNumber: number;
  serviceCode: string;
  description: string;
  unitCode: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  untaxedAmount: number;
  taxName: string;
  taxRate: number;
  taxAmount: number;
  lineTotal: number;
}

export interface ServiceInvoiceDetailPayment {
  paymentNumber: number;
  methodCode: string;
  amount: number;
  reference?: string | null;
}

export interface ServiceInvoiceDetail {
  documentId: string;
  businessId: string;
  businessName: string;
  documentNumber: string;
  fiscalNumber: string;
  issuedAt: string;
  customerIdentification: string;
  customerName: string;
  customerEmail?: string | null;
  untaxedAmount: number;
  taxAmount: number;
  payableAmount: number;
  creditAmount: number;
  creditDueDate?: string | null;
  cufe: string;
  fiscalStatus: string;
  qrPayload: string;
  lines: ServiceInvoiceDetailLine[];
  payments: ServiceInvoiceDetailPayment[];
}

export const serviceInvoicesApi = {
  services: (businessId: string, query = "") =>
    apiClient.post<ServiceInvoicePage<BillableServiceItem>>(
      "/commerce/v1/service-invoices/services/search",
      { businessId, query, page: 1, pageSize: 100 },
    ),
  customers: (businessId: string, query = "") =>
    apiClient.post<ServiceInvoicePage<ServiceInvoiceCustomerItem>>(
      "/commerce/v1/service-invoices/customers/search",
      { businessId, query, page: 1, pageSize: 50 },
    ),
  issue: (
    businessId: string,
    customerId: string,
    lines: IssueServiceInvoiceLine[],
    paymentMethodCode: string,
    paymentReference: string | undefined,
    idempotencyKey: string,
  ) => apiClient.postIdempotent<IssuedServiceInvoice>(
    "/commerce/v1/service-invoices/issue",
    { businessId, customerId, lines, paymentMethodCode, paymentReference },
    idempotencyKey,
  ),
  history: (
    businessId: string,
    filters: { query?: string; from?: string; to?: string; fiscalStatus?: string; page: number; pageSize: number },
  ) => apiClient.post<ServiceInvoicePage<ServiceInvoiceHistoryItem>>(
    "/commerce/v1/service-invoices/history/search",
    { businessId, ...filters },
  ),
  detail: (businessId: string, documentId: string) =>
    apiClient.get<ServiceInvoiceDetail>(
      `/commerce/v1/service-invoices/${documentId}`,
      { businessId },
    ),
  printable: (businessId: string, documentId: string) =>
    apiClient.get<ServiceInvoiceDetail>(
      `/commerce/v1/service-invoices/${documentId}/print`,
      { businessId },
    ),
};
