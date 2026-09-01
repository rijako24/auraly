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
export interface AccountingReadiness {
  status: "Disabled" | "Configuring" | "Ready";
  functionalCurrencyCode: string;
  effectiveFrom: string | null;
  openingBalanceMode: "ZeroDeclared" | "ImportedAndApproved" | null;
  activatedAt: string | null;
  blockingIssues: string[];
}
export interface ActivateAccounting {
  effectiveFrom: string;
  functionalCurrencyCode: "COP";
  openingBalanceMode: "ZeroDeclared" | "ImportedAndApproved";
}
export interface BankAccount {
  bankAccountId:string;accountingAccountId:string;accountingAccountCode:string;
  accountingAccountName:string;accountTypeOptionId:string;accountTypeCode:string;
  accountTypeName:string;bankName:string;accountNumber:string;displayName:string;
  currencyCode:string;isPrimary:boolean;isActive:boolean;rowVersion:string;
}
export interface SaveBankAccount {
  bankAccountId:string;accountingAccountId:string;accountTypeOptionId:string;
  bankName:string;accountNumber:string;displayName:string;isPrimary:boolean;
  isActive:boolean;rowVersion:string|null;
}
export interface AccountingOpeningBalanceLine { lineNumber:number;accountId:string;partyId:string|null;costCenterId:string|null;description:string;debit:number;credit:number; }
export interface AccountingOpeningBalance { batchId:string;businessId:string;effectiveOn:string;currencyCode:string;description:string;status:"Draft"|"Approved"|"Posted";debitTotal:number;creditTotal:number;rowVersion:string;updatedAt:string;approvedAt:string|null;postedAt:string|null;lines:AccountingOpeningBalanceLine[]; }
export interface SaveAccountingOpeningBalance { batchId:string;businessId:string;effectiveOn:string;currencyCode:"COP";description:string;rowVersion:string|null;lines:Array<Omit<AccountingOpeningBalanceLine,"lineNumber">>; }
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
export interface AccountingManualAcceptance { documentId: string; documentType: string; status: string; isDuplicate: boolean; }
export interface ConfirmAccountAdjustment {
  adjustmentId: string; businessId: string; subledgerKind: "Receivable" | "Payable";
  subledgerId: string; direction: "Increase" | "Decrease"; amount: number;
  counterpartAccountId: string; costCenterId: string | null; occurredAt: string;
  conceptCode: string; description: string;
}
export interface ConfirmManualVoucher {
  voucherId: string; businessId: string; occurredAt: string; conceptCode: string;
  description: string; lines: Array<{accountId:string;partyId:string|null;costCenterId:string|null;description:string;debit:number;credit:number}>;
}
export interface AccountingJournalRow { entryId:string;entryNumber:string;occurredAt:string;sourceDocumentId:string;sourceDocumentType:string;lineNumber:number;accountCode:string;accountName:string;partyId:string|null;costCenterId:string|null;description:string;debit:number;credit:number; }
export interface GeneralLedgerRow { accountCode:string;accountName:string;accountType:string;openingBalance:number;debit:number;credit:number;closingBalance:number; }
export interface FinancialStatementRow { section:string;accountCode:string;accountName:string;amount:number; }
export interface AccountingExceptionRow { sourceDocumentId:string;sourceDocumentType:string;occurredAt:string;status:string;errorCode:string|null;errorMessage:string|null; }
export interface ComplianceReportDefinition { authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;name:string;reportKind:"Exogenous"|"FiscalDraft";resolutionNumber:string;resolutionDate:string;technicalAnnex:string;sourceUrl:string;sourceSha256:string; }
export interface ComplianceConceptMapping { mappingId:string;tenantId:string;businessId:string|null;authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;accountId:string;accountCode:string;accountName:string;conceptCode:string;targetField:string; }
export interface ComplianceValidation { severity:"Error"|"Warning";code:string;message:string;partyId:string|null;accountId:string|null; }
export interface ComplianceReportRun { runId:string;authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;name:string;reportKind:"Exogenous"|"FiscalDraft";periodFrom:string;periodTo:string;status:"Blocked"|"Ready";resolutionNumber:string;sourceUrl:string;sourceSha256:string;rowCount:number;controlTotal:number;createdAt:string;validations:ComplianceValidation[]; }
export interface SetComplianceConceptMapping { businessId:string|null;authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;accountId:string;conceptCode:string;targetField:string; }
export interface GenerateComplianceReport { authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;periodFrom:string;periodTo:string; }

export const accountingApi = {
  accounts: () => apiClient.get<AccountingAccount[]>("/commerce/v1/accounting/accounts"),
  bankAccounts: (includeInactive=false) => apiClient.get<BankAccount[]>("/commerce/v1/accounting/bank-accounts", {includeInactive}),
  saveBankAccount: (request:SaveBankAccount) => apiClient.put<BankAccount>(`/commerce/v1/accounting/bank-accounts/${request.bankAccountId}`,request),
  costCenters: () => apiClient.get<AccountingCostCenter[]>("/commerce/v1/accounting/cost-centers"),
  periods: () => apiClient.get<AccountingPeriod[]>("/commerce/v1/accounting/periods"),
  mappings: () => apiClient.get<AccountingMapping[]>("/commerce/v1/accounting/account-mappings"),
  categoryDefinitions: () => apiClient.get<AccountingCategoryDefinition[]>("/commerce/v1/accounting/category-definitions"),
  readiness: (effectiveFrom?: string, openingBalanceMode?: "ZeroDeclared"|"ImportedAndApproved") => apiClient.get<AccountingReadiness>("/commerce/v1/accounting/readiness", { effectiveFrom, openingBalanceMode }),
  activate: (request: ActivateAccounting) => apiClient.post<AccountingReadiness>("/commerce/v1/accounting/activate", request),
  openingBalance: (effectiveOn:string) => apiClient.get<AccountingOpeningBalance|null>("/commerce/v1/accounting/opening-balances", {effectiveOn}),
  saveOpeningBalance: (request:SaveAccountingOpeningBalance) => apiClient.put<AccountingOpeningBalance>("/commerce/v1/accounting/opening-balances",request),
  approveOpeningBalance: (batchId:string) => apiClient.post<AccountingOpeningBalance>(`/commerce/v1/accounting/opening-balances/${batchId}/approve`,{}),
  ensureDefaults: () => apiClient.put<AccountingDefaultsResult>("/commerce/v1/accounting/defaults", {}),
  createAccount: (request: CreateAccount) => apiClient.post<AccountingAccount>("/commerce/v1/accounting/accounts", request),
  createCostCenter: (request: CreateCostCenter) => apiClient.post<AccountingCostCenter>("/commerce/v1/accounting/cost-centers", request),
  createPeriod: (request: CreatePeriod) => apiClient.post<AccountingPeriod>("/commerce/v1/accounting/periods", request),
  setMapping: (request: SetAccountMapping) => apiClient.put<void>("/commerce/v1/accounting/account-mappings", request),
  closePeriod: (periodId: string) => apiClient.post<void>(`/commerce/v1/accounting/periods/${periodId}/close`, {}),
  confirmAdjustment: (request: ConfirmAccountAdjustment) => apiClient.post<AccountingManualAcceptance>("/commerce/v1/accounting/manual/account-adjustments", request),
  confirmVoucher: (request: ConfirmManualVoucher) => apiClient.post<AccountingManualAcceptance>("/commerce/v1/accounting/manual/vouchers", request),
  trialBalance: (from: string, to: string) => apiClient.get<TrialBalanceRow[]>(`/commerce/v1/accounting/reports/trial-balance?from=${from}&to=${to}`),
  accountMovements: (accountCode: string, from: string, to: string) => apiClient.get<AccountMovementRow[]>(`/commerce/v1/accounting/reports/account-movements?accountCode=${encodeURIComponent(accountCode)}&from=${from}&to=${to}`),
  journal: (from:string,to:string) => apiClient.get<AccountingJournalRow[]>(`/commerce/v1/accounting/reports/journal?from=${from}&to=${to}`),
  generalLedger: (from:string,to:string) => apiClient.get<GeneralLedgerRow[]>(`/commerce/v1/accounting/reports/general-ledger?from=${from}&to=${to}`),
  balanceSheet: (asOf:string) => apiClient.get<FinancialStatementRow[]>(`/commerce/v1/accounting/reports/balance-sheet?asOf=${asOf}`),
  incomeStatement: (from:string,to:string) => apiClient.get<FinancialStatementRow[]>(`/commerce/v1/accounting/reports/income-statement?from=${from}&to=${to}`),
  exceptions: (from:string,to:string) => apiClient.get<AccountingExceptionRow[]>(`/commerce/v1/accounting/reports/exceptions?from=${from}&to=${to}`),
  complianceDefinitions: (taxYear?:number) => apiClient.get<ComplianceReportDefinition[]>("/commerce/v1/accounting/compliance/definitions",{taxYear}),
  complianceMappings: (taxYear:number,formatCode?:string) => apiClient.get<ComplianceConceptMapping[]>("/commerce/v1/accounting/compliance/mappings",{taxYear,formatCode}),
  setComplianceMapping: (request:SetComplianceConceptMapping) => apiClient.put<ComplianceConceptMapping>("/commerce/v1/accounting/compliance/mappings",request),
  generateComplianceReport: (request:GenerateComplianceReport) => apiClient.post<ComplianceReportRun>("/commerce/v1/accounting/compliance/runs",request),
  complianceRuns: (taxYear?:number) => apiClient.get<ComplianceReportRun[]>("/commerce/v1/accounting/compliance/runs",{taxYear}),
  complianceArtifactUrl: (runId:string) => `/api/commerce/v1/accounting/compliance/runs/${runId}/artifact`,
};
