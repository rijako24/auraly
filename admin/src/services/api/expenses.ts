import { apiClient } from "./client";

export type ExpenseConcept = { conceptId: string; businessId: string; code: string; name: string; expenseAccountId: string; expenseAccountCode: string; expenseAccountName: string; defaultCostCenterId: string | null; defaultCostCenterName: string | null; withholdingConceptCode: string | null; isActive: boolean };
export type ExpenseSupplier = { supplierId: string; identification: string; name: string };
export type ExpenseAccount = { accountId: string; code: string; name: string };
export type ExpenseCostCenter = { costCenterId: string; code: string; name: string; isDefault: boolean };
export type ExpenseOptions = { concepts: ExpenseConcept[]; suppliers: ExpenseSupplier[]; expenseAccounts: ExpenseAccount[]; costCenters: ExpenseCostCenter[] };
export type ExpenseItem = { expenseId: string; documentNumber: string; supplierDocumentNumber: string; supplierId: string; supplierName: string; conceptId: string; conceptName: string; issuedAt: string; dueDate: string; grossAmount: number; withholdingAmount: number; netPayable: number; currencyCode: string; status: string; evidenceUrl: string | null };
export type ExpensePage = { items: ExpenseItem[]; page: number; pageSize: number; totalCount: number; totalPages: number; grossTotal: number; withholdingTotal: number; netPayableTotal: number };
export type ConfirmExpense = { expenseId: string; businessId: string; supplierId: string; conceptId: string; costCenterId: string | null; supplierDocumentNumber: string; issuedAt: string; dueDate: string; currencyCode: "COP"; description: string; taxExclusiveAmount: number; vatAmount: number; withholdingJurisdictionCode: string | null; evidenceUrl: string | null };
export type SaveExpenseConcept = { conceptId: string; businessId: string; code: string; name: string; expenseAccountId: string; defaultCostCenterId: string | null; withholdingConceptCode: string | null; isActive: boolean };

export const expensesApi = {
  options: () => apiClient.get<ExpenseOptions>("/commerce/v1/expenses/options"),
  list: (params:{page:number;pageSize:number;search?:string}) => apiClient.get<ExpensePage>("/commerce/v1/expenses", params),
  confirm: (request: ConfirmExpense) => apiClient.postIdempotent("/commerce/v1/expenses/confirm", request, request.expenseId),
  saveConcept: (request: SaveExpenseConcept) => apiClient.put<ExpenseConcept>(`/commerce/v1/expenses/concepts/${request.conceptId}`, request),
};
