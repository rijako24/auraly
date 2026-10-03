import { apiClient } from "./client";
import type { WithholdingAdjustment, WithholdingCalculation } from "./taxation";
import type { PurchaseEvidenceType } from "./goods-receipts";

export type ExpenseConcept = { conceptId: string; businessId: string; code: string; name: string; expenseAccountId: string; expenseAccountCode: string; expenseAccountName: string; defaultCostCenterId: string | null; defaultCostCenterName: string | null; withholdingConceptCode: string | null; isActive: boolean };
export type ExpenseSupplier = { supplierId: string; identification: string; name: string };
export type ExpenseAccount = { accountId: string; code: string; name: string };
export type ExpenseCostCenter = { costCenterId: string; code: string; name: string; isDefault: boolean };
export type ExpenseOptions = { concepts: ExpenseConcept[]; suppliers: ExpenseSupplier[]; expenseAccounts: ExpenseAccount[]; costCenters: ExpenseCostCenter[]; purchaseEvidenceTypes: Array<{ code: PurchaseEvidenceType; label: string; description: string }>; taxes?: Array<{taxProfileId:string;name:string;rate:number}>; taxTreatments?: Array<{code:string;label:string;description:string}>; withholdingConceptCodes?: string[] };
export type ExpenseLineInput = { expenseAccountId:string;conceptId:string|null;costCenterId:string|null;description:string;taxExclusiveAmount:number;taxProfileId:string|null;taxTreatment:string;withholdingConceptCode:string|null };
export type ExpenseLine = ExpenseLineInput & {lineNumber:number;accountCode:string;accountName:string;costCenterName:string|null;taxName:string|null;taxRate:number;vatAmount:number};
export type ExpenseWithholding = WithholdingCalculation;
export type ExpensePreview = { lines:ExpenseLine[];withholding:ExpenseWithholding;calculationHash:string;diagnostics:string[];canConfirm:boolean };
export type ExpenseItem = { expenseId: string; documentNumber: string; supplierDocumentNumber: string | null; supplierId: string; supplierName: string; conceptId: string|null; conceptName: string; issuedAt: string; dueDate: string; grossAmount: number; withholdingAmount: number; netPayable: number; currencyCode: string; status: string; evidenceUrl: string | null; purchaseEvidenceType: PurchaseEvidenceType; payableStatus: string | null; outstandingAmount: number | null; chargeReturned: boolean };
export type ExpensePage = { items: ExpenseItem[]; page: number; pageSize: number; totalCount: number; totalPages: number; grossTotal: number; withholdingTotal: number; netPayableTotal: number };
export type ExpenseDetail = Omit<ExpenseItem,"payableStatus"|"outstandingAmount"> & { currencyCode: string; description: string; taxExclusiveAmount: number; vatAmount: number; fiscalNumber: string | null; fiscalStatus: string | null; payable: { payableId: string; status: string; originalAmount: number; outstandingAmount: number; partySiteId: string | null; partySiteName: string | null } | null; cancellationId:string|null; cancellationReason:string|null; adjustmentFiscalNumber:string|null; adjustmentFiscalStatus:string|null; sourceInvoiceId:string|null; sourceInvoiceNumber:string|null; lines?:ExpenseLine[]|null;withholding?:ExpenseWithholding|null };
export type ConfirmExpense = { expenseId: string; businessId: string; supplierId: string; partySiteId: string|null; conceptId: string|null; costCenterId: string | null; supplierDocumentNumber: string | null; issuedAt: string; dueDate: string; currencyCode: "COP"; description: string; taxExclusiveAmount: number; vatAmount: number; withholdingJurisdictionCode: string | null; evidenceUrl: string | null; purchaseEvidenceType: PurchaseEvidenceType;lines?:ExpenseLineInput[];calculationHash?:string|null;withholdingAdjustments?:WithholdingAdjustment[]|null };
export type SaveExpenseConcept = { conceptId: string; businessId: string; name: string; expenseAccountId: string; defaultCostCenterId: string | null; withholdingConceptCode: string | null; isActive: boolean };

export const expensesApi = {
  options: () => apiClient.get<ExpenseOptions>("/commerce/v1/expenses/options", { includeDirectories: false }),
  list: (params:{page:number;pageSize:number;search?:string;supplierId?:string;conceptId?:string;from?:string;to?:string;status?:string;payableStatus?:string}) => apiClient.get<ExpensePage>("/commerce/v1/expenses", params),
  get: (expenseId:string) => apiClient.get<ExpenseDetail>(`/commerce/v1/expenses/${expenseId}`),
  cancel: (expenseId:string, cancellationId:string, reasonOptionId:string) =>
    apiClient.post<{expenseId:string;cancellationId:string;accountingJobId:string;hasFiscalAdjustment:boolean;idempotentReplay:boolean}>(
      `/commerce/v1/expenses/${expenseId}/cancel`, {cancellationId,reasonOptionId}),
  confirm: (request: ConfirmExpense) => apiClient.postIdempotent("/commerce/v1/expenses/confirm", request, request.expenseId),
  preview: (request: ConfirmExpense) => apiClient.post<ExpensePreview>("/commerce/v1/expenses/preview", request),
  saveConcept: (request: SaveExpenseConcept) => apiClient.put<ExpenseConcept>(`/commerce/v1/expenses/concepts/${request.conceptId}`, request),
};
