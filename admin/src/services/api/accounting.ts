import { apiClient } from "./client";

export interface AccountingAccount {
  accountId: string; code: string; name: string; accountType: string;
  allowsPosting: boolean; requiresParty: boolean; isActive: boolean;
}
export interface AccountingCostCenter {
  costCenterId: string; businessId: string; code: string; name: string;
  parentCostCenterId: string | null; isDefault: boolean; isActive: boolean;
}
export interface AccountingPeriod {
  periodId: string; startsOn: string; endsOn: string; name: string; status: string;
}
export interface AccountingMapping {
  mappingId: string; tenantId: string; businessId: string | null; category: string;
  accountId: string; effectiveFrom: string; effectiveTo: string | null;
}
export interface AccountingDefaultsResult { accountCount: number; mappingCount: number; hasDefaultCostCenter: boolean; hasOpenPeriod: boolean; isReady: boolean; }
export interface AccountingCategoryDefinition { category: string; displayName: string; accountType: string; isRequired: boolean; displayOrder: number; }
export interface TrialBalanceRow { accountCode: string; accountName: string; debit: number; credit: number; balance: number; }
export interface AccountMovementRow { entryId: string; entryNumber: string; sourceDocumentId: string; sourceDocumentType: string; occurredAt: string; description: string; debit: number; credit: number; balance: number; }
export interface CreateAccount {
  accountId: string; tenantId: string; code: string; name: string;
  accountType: string; allowsPosting: boolean; requiresParty: boolean;
}
export interface CreateCostCenter {
  costCenterId: string; businessId: string; code: string; name: string;
  parentCostCenterId: string | null; isDefault: boolean;
}
export interface CreatePeriod {
  periodId: string; tenantId: string; startsOn: string; endsOn: string; name: string;
}
export interface SetAccountMapping {
  tenantId: string; businessId: string | null; category: string; accountId: string;
  effectiveFrom: string; effectiveTo: string | null;
}

export const accountingApi = {
  accounts: () => apiClient.get<AccountingAccount[]>("/commerce/v1/accounting/accounts"),
  costCenters: () => apiClient.get<AccountingCostCenter[]>("/commerce/v1/accounting/cost-centers"),
  periods: () => apiClient.get<AccountingPeriod[]>("/commerce/v1/accounting/periods"),
  mappings: () => apiClient.get<AccountingMapping[]>("/commerce/v1/accounting/account-mappings"),
  categoryDefinitions: () => apiClient.get<AccountingCategoryDefinition[]>("/commerce/v1/accounting/category-definitions"),
  ensureDefaults: () => apiClient.put<AccountingDefaultsResult>("/commerce/v1/accounting/defaults", {}),
  createAccount: (request: CreateAccount) => apiClient.post<AccountingAccount>("/commerce/v1/accounting/accounts", request),
  createCostCenter: (request: CreateCostCenter) => apiClient.post<AccountingCostCenter>("/commerce/v1/accounting/cost-centers", request),
  createPeriod: (request: CreatePeriod) => apiClient.post<AccountingPeriod>("/commerce/v1/accounting/periods", request),
  setMapping: (request: SetAccountMapping) => apiClient.put<void>("/commerce/v1/accounting/account-mappings", request),
  closePeriod: (periodId: string) => apiClient.post<void>(`/commerce/v1/accounting/periods/${periodId}/close`, {}),
  trialBalance: (from: string, to: string) => apiClient.get<TrialBalanceRow[]>(`/commerce/v1/accounting/reports/trial-balance?from=${from}&to=${to}`),
  accountMovements: (accountCode: string, from: string, to: string) => apiClient.get<AccountMovementRow[]>(`/commerce/v1/accounting/reports/account-movements?accountCode=${encodeURIComponent(accountCode)}&from=${from}&to=${to}`),
};
