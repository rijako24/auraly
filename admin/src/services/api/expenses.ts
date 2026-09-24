import { apiClient } from "./client";
import type { PurchaseEvidenceType } from "./goods-receipts";

export type ExpenseConcept = { conceptId: string; businessId: string; code: string; name: string; expenseAccountId: string; expenseAccountCode: string; expenseAccountName: string; defaultCostCenterId: string | null; defaultCostCenterName: string | null; withholdingConceptCode: string | null; isActive: boolean };
export type ExpenseSupplier = { supplierId: string; identification: string; name: string };
export type ExpenseAccount = { accountId: string; code: string; name: string };
export type ExpenseCostCenter = { costCenterId: string; code: string; name: string; isDefault: boolean };
export type ExpenseOptions = { concepts: ExpenseConcept[]; suppliers: ExpenseSupplier[]; expenseAccounts: ExpenseAccount[]; costCenters: ExpenseCostCenter[]; purchaseEvidenceTypes: Array<{ code: PurchaseEvidenceType; label: string; description: string }> };
export type ExpenseItem = { expenseId: string; documentNumber: string; supplierDocumentNumber: string | null; supplierId: string; supplierName: string; conceptId: string; conceptName: string; issuedAt: string; dueDate: string; grossAmount: number; withholdingAmount: number; netPayable: number; currencyCode: string; status: string; evidenceUrl: string | null; purchaseEvidenceType: PurchaseEvidenceType };
export type ExpensePage = { items: ExpenseItem[]; page: number; pageSize: number; totalCount: number; totalPages: number; grossTotal: number; withholdingTotal: number; netPayableTotal: number };
export type ExpenseDetail = ExpenseItem & { currencyCode: string; description: string; taxExclusiveAmount: number; vatAmount: number; fiscalNumber: string | null; fiscalStatus: string | null; payable: { payableId: string; status: string; originalAmount: number; outstandingAmount: number } | null; cancellationId:string|null; cancellationReason:string|null; adjustmentFiscalNumber:string|null; adjustmentFiscalStatus:string|null };
export type ConfirmExpense = { expenseId: string; businessId: string; supplierId: string; conceptId: string; costCenterId: string | null; supplierDocumentNumber: string | null; issuedAt: string; dueDate: string; currencyCode: "COP"; description: string; taxExclusiveAmount: number; vatAmount: number; withholdingJurisdictionCode: string | null; evidenceUrl: string | null; purchaseEvidenceType: PurchaseEvidenceType };
export type SaveExpenseConcept = { conceptId: string; businessId: string; name: string; expenseAccountId: string; defaultCostCenterId: string | null; withholdingConceptCode: string | null; isActive: boolean };

export const expensesApi = {
  options: () => apiClient.get<ExpenseOptions>("/commerce/v1/expenses/options"),
  list: (params:{page:number;pageSize:number;search?:string;supplierId?:string;conceptId?:string;from?:string;to?:string;status?:string}) => apiClient.get<ExpensePage>("/commerce/v1/expenses", params),
  get: (expenseId:string) => apiClient.get<ExpenseDetail>(`/commerce/v1/expenses/${expenseId}`),
  cancel: (expenseId:string, cancellationId:string, reason:string) =>
    apiClient.post<{expenseId:string;cancellationId:string;accountingJobId:string;hasFiscalAdjustment:boolean;idempotentReplay:boolean}>(
      `/commerce/v1/expenses/${expenseId}/cancel`, {cancellationId,reason}),
  confirm: (request: ConfirmExpense) => apiClient.postIdempotent("/commerce/v1/expenses/confirm", request, request.expenseId),
  saveConcept: (request: SaveExpenseConcept) => apiClient.put<ExpenseConcept>(`/commerce/v1/expenses/concepts/${request.conceptId}`, request),
};
