import { apiClient } from "./client";
import type { ExpenseConcept } from "./expenses";
import type { TaxProfile } from "./tax-profiles";

export type ChargeTariff = { fromInclusive: number; toExclusive: number | null; calculationMode: string; value: number };
export type ChargeSupplier = { supplierId: string; name: string; identification: string | null; defaultPaymentDueDays: number; isActive: boolean };
export type InvoiceCharge = {
  chargeId: string; businessId: string; version: number; code: string; name: string;
  isActive: boolean; sortOrder: number; calculationMode: string; value: number | null;
  inclusionMode: string; invoiceAmountLimit: number | null;
  expenseConceptId: string; expenseConceptName: string; expenseAccountId: string;
  expenseAccountCode: string; expenseAccountName: string; costCenterId: string | null; costCenterName: string | null;
  salesTaxProfileId: string; salesTaxProfileName: string; taxCode: string; taxRate: number;
  purchaseTaxProfileId: string; purchaseTaxProfileName: string; purchaseTaxRate: number;
  ranges: ChargeTariff[]; suppliers: ChargeSupplier[];
};
export type SaveInvoiceCharge = Pick<InvoiceCharge, "chargeId" | "code" | "name" | "isActive" | "sortOrder" |
  "calculationMode" | "value" | "inclusionMode" | "invoiceAmountLimit" | "expenseConceptId" | "salesTaxProfileId" | "purchaseTaxProfileId" | "ranges"> &
  { expectedVersion: number; supplierIds: string[] };
export type InvoiceChargePage = { items: InvoiceCharge[]; page: number; pageSize: number; totalCount: number; totalPages: number };
export type InvoiceChargeOptions = { concepts: ExpenseConcept[]; taxProfiles: TaxProfile[] };

export type InvoiceChargeHistoryPage = { items: Array<{ documentId: string; documentNumber: string;
  issuedAt: string; customerName: string; workSessionId: string | null; charge: AppliedInvoiceCharge; expenseDocumentNumber: string | null;
  expenseStatus: string | null; payableBalance: number | null; fiscalNumber?: string|null;invoiceTotal:number;expenseCreatedByName?:string|null;
  expenseId?:string|null;expenseAcceptedAt?:string|null;expenseIssuedAt?:string|null;expenseFiscalNumber?:string|null;expenseGrossAmount?:number|null;expenseWithholdingAmount?:number|null;expenseNetPayable?:number|null;expenseDueDate?:string|null }>;
  page: number; pageSize: number; totalCount: number; totalDocuments: number; invoicedTotal: number; expenseTotal: number; generatedExpenseTotal:number };

export type InvoiceChargeHistoryFilters = { from:string;to:string;search?:string;createdByUserId?:string;customerId?:string };

export const invoiceChargesApi = {
  history: (params: InvoiceChargeHistoryFilters & {page:number;pageSize:number}, signal?:AbortSignal) =>
    apiClient.get<InvoiceChargeHistoryPage>("/commerce/v1/invoice-charges/history", params, {signal}),
  exportHistory: (params:InvoiceChargeHistoryFilters, signal?:AbortSignal) =>
    apiClient.get<InvoiceChargeHistoryPage>("/commerce/v1/invoice-charges/history/export",params,{signal}),
  list: (params: { page: number; pageSize: number; search?: string; includeInactive?: boolean }) =>
    apiClient.get<InvoiceChargePage>("/commerce/v1/invoice-charges", params),
  options: () => apiClient.get<InvoiceChargeOptions>("/commerce/v1/invoice-charges/options"),
  save: (request: SaveInvoiceCharge) => apiClient.put<InvoiceCharge>(`/commerce/v1/invoice-charges/${request.chargeId}`, request),
};

export type AppliedInvoiceCharge = {
  appliedChargeId: string; chargeId: string; version: number; code: string; name: string;
  invoiceBase: number; amount: number; invoicedAmount: number; expenseAmount: number;
  invoicedUntaxedAmount: number; invoicedTaxAmount: number; supplier: ChargeSupplier;
  manualAmount: number | null;
};
export type AddInvoiceCharge = {
  appliedChargeId: string; chargeId: string; chargeVersion: number; supplierId: string; manualAmount: number | null;
};
