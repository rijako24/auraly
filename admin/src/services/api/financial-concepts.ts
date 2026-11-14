import { apiClient } from "./client";

export interface FinancialConcept {
  financialConceptId: string; code: string; name: string; accountId: string; accountCode: string; accountName: string;
  financialFunction: string | null; sideOptionId: string; sideCode: string; sideLabel: string;
  useForReceipts: boolean; useForSupplierPayments: boolean; useForVouchers: boolean; isActive: boolean;
  sortOrder: number; rowVersion: string;
}
export type SaveFinancialConcept = Omit<FinancialConcept, "accountCode" | "accountName" | "financialFunction" | "sideCode" | "sideLabel" | "rowVersion"> & { rowVersion?: string | null };
export type FinancialConceptQuery = { page?: number; pageSize?: number; search?: string; isActive?: boolean; usage?: "Receipts" | "SupplierPayments" | "Vouchers" | "Filter"; financialConceptId?: string; };
export interface FinancialConceptPage { items: FinancialConcept[]; page: number; pageSize: number; totalCount: number; }
export const financialConceptsApi = {
  list: (query: FinancialConceptQuery) => apiClient.get<FinancialConceptPage>("/commerce/v1/financial-concepts", query),
  save: (value: SaveFinancialConcept, query: FinancialConceptQuery) => {
    const parameters = new URLSearchParams(Object.entries(query).filter(([, item]) => item !== undefined).map(([key, item]) => [key, String(item)]));
    return apiClient.put<{ concept: FinancialConcept; page: FinancialConceptPage }>(`/commerce/v1/financial-concepts/${value.financialConceptId}?${parameters}`, value);
  },
};
