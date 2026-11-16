import { apiClient } from "./client";

export type PaymentMethod = {
  paymentMethodId: string; code: string; name: string; paymentTypeId: string;
  methodCode: string; typeLabel: string; accountId: string; accountCode: string;
  accountName: string; bankAccountId: string | null; useForSales: boolean;
  useForReceipts: boolean; useForSupplierPayments: boolean; useForPayroll: boolean;
  useForVouchers?: boolean;
  isActive: boolean; sortOrder: number; version: number; rowVersion: string;
};
export type PaymentMethodCatalogSnapshot = { contractVersion: number; forms: PaymentMethod[] };
export type PaymentMethodQuery = {
  page: number; pageSize: number; search?: string; isActive?: boolean; methodCode?: string;
  usage?: "Sales" | "ServiceInvoices" | "TipPayments" | "Receipts" | "SupplierPayments" | "Payroll" | "VoucherReceipts" | "VoucherPayments"; paymentMethodId?: string;
};
export type PaymentMethodPage = { items: PaymentMethod[]; page: number; pageSize: number; totalCount: number };
export type SavePaymentMethod = Pick<PaymentMethod, "paymentMethodId" | "code" | "name" | "paymentTypeId" |
  "accountId" | "useForSales" | "useForReceipts" | "useForSupplierPayments" | "useForPayroll" | "useForVouchers" |
  "isActive" | "sortOrder"> & { rowVersion?: string };

export const paymentMethodsApi = {
  list: (query: PaymentMethodQuery) => apiClient.get<PaymentMethodPage>("/commerce/v1/payment-methods/", query),
  save: (request: SavePaymentMethod, query: PaymentMethodQuery) => {
    const parameters = new URLSearchParams(Object.entries(query).filter(([, value]) => value !== undefined)
      .map(([key, value]) => [key, String(value)]));
    return apiClient.put<{ method: PaymentMethod; page: PaymentMethodPage }>(
      `/commerce/v1/payment-methods/${request.paymentMethodId}?${parameters}`, request);
  },
};
