import { apiClient } from "./client";
import type { WithholdingAdjustment, WithholdingCalculation } from "./taxation";

export interface AccountingAccount {
  accountId: string; code: string; name: string; accountType: string;
  allowsPosting: boolean; requiresParty: boolean; isActive: boolean;
  level: "Class"|"Group"|"Account"|"Subaccount"|"Auxiliary";
  rowVersion?: string | null;
  configurationLocked?: boolean;
  semantics?: { parentAccountId:string|null;financialFunction:string;balanceSide:string;businessId:string|null;
    bankAccountId:string|null;paymentMethodOptionId:string|null } | null;
}
export interface AccountingAccountPage { items: AccountingAccount[]; page: number; pageSize: number; totalCount: number; totalPages: number; }
export interface AccountingOpenPosition {
  subledgerKind: "Payable" | "Receivable" | "SupplierCredit" | "CustomerCredit"; subledgerId: string; partyId: string;
  partyName: string; partyIdentification: string | null; sourceDocumentId: string;
  sourceDocumentType: string; documentNumber: string; occurredAt: string; dueDate: string | null;
  currencyCode: string | null; originalAmount: number; outstandingAmount: number;
  pendingPaymentAmount: number; pendingAdjustmentAmount: number; availableAmount: number;
  openingEntryId: string | null; openingEntryLineNumber: number | null; accountId: string | null;
  accountCode: string | null; accountName: string | null; accountIsActive: boolean | null;
  openingDebit: number | null; openingCredit: number | null; openingLinkIssue: string | null;
  counterpartyRoleId: string | null; status: string;
  pendingApplicationAmount?: number; creditNature?: string | null; partySiteId?: string | null;
  financialDiscountEligibleAmount?: number | null; financialDiscountAnticipated?: boolean | null;
  financialDiscountDecided?: boolean; financialDiscountPendingReturn?: boolean;
  financialDiscountEarnedAccountId?: string | null; financialDiscountEarnedAccountCode?: string | null;
  financialDiscountEarnedAccountName?: string | null;
  financialDiscountLostAccountId?: string | null; financialDiscountLostAccountCode?: string | null;
  financialDiscountLostAccountName?: string | null;
}
export interface AccountingOpenPositionPage { items: AccountingOpenPosition[]; page: number; pageSize: number; totalCount: number; totalOutstandingAmount: number; }
export interface AccountingCostCenter {
  costCenterId: string; businessId: string; code: string; name: string;
  parentCostCenterId: string | null; isDefault: boolean; isActive: boolean; rowVersion:string;
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
  canEditOpeningBalances: boolean;
}
export interface AccountingCostCenterAssignment { assignmentId:string;businessId:string;costCenterId:string;costCenterCode:string;costCenterName:string;operationKind:"All"|"Sales"|"Purchasing"|"Expenses"|"Inventory";warehouseId:string|null;warehouseCode:string|null;warehouseName:string|null;isActive:boolean;rowVersion:string; }
export interface ActivateAccounting {
  effectiveFrom: string;
  functionalCurrencyCode: "COP";
  openingBalanceMode: "ZeroDeclared" | "ImportedAndApproved";
}
export interface BankAccount {
  bankAccountId:string;accountingAccountId:string|null;accountingAccountCode:string|null;
  accountingAccountName:string|null;accountTypeOptionId:string;accountTypeCode:string;
  accountTypeName:string;bankName:string;accountNumber:string;displayName:string;
  currencyCode:string;isPrimary:boolean;isActive:boolean;rowVersion:string;auxiliaryCount:number;
}
export interface SaveBankAccount {
  bankAccountId:string;accountingAccountId?:string|null;accountTypeOptionId:string;
  bankName:string;accountNumber:string;displayName:string;isPrimary:boolean;
  isActive:boolean;rowVersion:string|null;
}
export interface BankReconciliationSummary { reconciliationId:string;bankAccountId:string;bankDisplayName:string;accountingAccountCode:string|null;accountingAccountName:string|null;periodFrom:string;periodTo:string;openingBalance:number;closingBalance:number;fileName:string;fileSha256:string;status:"Preparation"|"Closed"|"Reopened";statementLineCount:number;matchedLineCount:number;statementMovementTotal:number;bookMovementTotal:number;difference:number;requiresReview:boolean;rowVersion:string;updatedAt:string; }
export interface BankStatementLine { statementLineId:string;lineNumber:number;transactionDate:string;description:string;reference:string|null;amount:number;balance:number|null;allocatedAmount:number;isMatched:boolean; }
export interface BankBookLine { entryId:string;entryLineNumber:number;entryNumber:string;occurredAt:string;businessId:string;businessName:string;sourceDocumentType:string;sourceDocumentId:string;description:string;amount:number;allocatedAmount:number;availableAmount:number; }
export interface BankReconciliationAllocation { matchId:string;statementLineId:string;entryId:string;entryLineNumber:number;amount:number;createdAt:string; }
export interface BankReconciliationStatusEvent { statusEventId:string;status:"Closed"|"Reopened";reason:string|null;actorUserId:string;actorName:string;occurredAt:string;statementClosingBalance:number|null;bookClosingBalance:number|null;accountingCutoffPostedAt:string|null; }
export interface BankReconciliationQuery { page:number;pageSize:number;bankAccountId?:string;search?:string;sortBy?:"periodTo"|"updatedAt"|"bankDisplayName";sortDirection?:"asc"|"desc"; }
export interface BankReconciliationPage { items:BankReconciliationSummary[];page:number;pageSize:number;totalCount:number; }
export interface BankReconciliationDetailQuery { statementPage:number;bookPage:number;allocationPage:number;eventPage:number;pageSize:number;statementSearch?:string;bookSearch?:string; }
export interface BankReconciliationDetail { summary:BankReconciliationSummary;statementLines:BankStatementLine[];bookLines:BankBookLine[];allocations:BankReconciliationAllocation[];statusEvents:BankReconciliationStatusEvent[];
  paging:{query:BankReconciliationDetailQuery;statementCount:number;bookCount:number;allocationCount:number;eventCount:number;unmatchedBookCount:number}; }
export interface BankStatementLineImport { lineNumber:number;transactionDate:string;description:string;reference:string|null;amount:number;balance:number|null; }
export interface ImportBankReconciliation { reconciliationId:string;bankAccountId:string;periodFrom:string;periodTo:string;openingBalance:number;closingBalance:number;fileName:string;fileSha256:string;originalFileBase64:string;lines:BankStatementLineImport[]; }
export interface AccountingOpeningBalanceLine { lineNumber:number;accountId:string;partyId:string|null;costCenterId:string|null;description:string;debit:number;credit:number;lineId?:string|null;existingPosition?:{subledgerKind:"Payable"|"Receivable";subledgerId:string}|null;existingPositionDocumentNumber?:string|null; }
export interface AccountingOpeningBalance { batchId:string;businessId:string;effectiveOn:string;currencyCode:string;description:string;status:"Draft"|"Approved"|"Posted";debitTotal:number;creditTotal:number;rowVersion:string;updatedAt:string;approvedAt:string|null;postedAt:string|null;lines:AccountingOpeningBalanceLine[]; }
export interface SaveAccountingOpeningBalance { batchId:string;businessId:string;effectiveOn:string;currencyCode:"COP";description:string;rowVersion:string|null;lines:Array<Omit<AccountingOpeningBalanceLine,"lineNumber"|"existingPositionDocumentNumber">>; }
export interface AccountingCategoryDefinition { category: string; displayName: string; accountType: string; isRequired: boolean; displayOrder: number; }
export interface TrialBalanceRow { accountCode: string; accountName: string; level:"Class"|"Group"|"Account"|"Subaccount"|"Auxiliary"; openingDebit:number; openingCredit:number; debit:number; credit:number; closingDebit:number; closingCredit:number; }
export interface AccountMovementRow { lineNumber:number;costCenterId:string|null;costCenterCode:string|null;costCenterName:string|null;entryId: string; entryNumber: string; sourceDocumentId: string; sourceDocumentType: string; occurredAt: string; description: string; debit: number; credit: number; balance: number; accountCode:string|null;accountName:string|null;partyId:string|null;partyIdentification:string|null;partyName:string|null;sourceDocumentNumber:string|null;reference:string|null;baseAmount:number|null; }
export interface AccountMovementTotal { accountCode:string;accountName:string;openingBalance:number;debit:number;credit:number;closingBalance:number;movementCount:number;baseAmount:number|null; }
export interface AccountMovementPage { items:AccountMovementRow[];accounts:AccountMovementTotal[];page:number;pageSize:number;totalCount:number;totalPages:number; }
export interface AccountMovementQuery { from:string;to:string;page:number;pageSize:number;accountFrom?:string;accountTo?:string;accountRootCode?:string;partyId?:string;costCenterId?:string; }
export interface CreateAccount {
  accountId: string; tenantId: string; code: string; name: string;
  accountType: string; allowsPosting: boolean; requiresParty: boolean; semantics?: AccountingAccount["semantics"];
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
export interface AccountingPosting { sourceDocumentId:string;sourceDocumentType:string;status:string;errorCode:string|null;errorMessage:string|null;entryId:string|null; }
export interface AccountingEntryLine { lineNumber:number;accountCode:string;accountName:string;debit:number;credit:number;partyId:string|null;partyIdentification:string|null;partyName:string|null;costCenterId:string|null;costCenterCode:string|null;costCenterName:string|null;description:string;accountId?:string;sourceLineId?:string|null;baseAmount?:number|null; }
export interface AccountingEntry { entryId:string;entryNumber:string;sourceDocumentId:string;sourceDocumentType:string;occurredAt:string;postedAt:string;description:string;debitTotal:number;creditTotal:number;lines:AccountingEntryLine[];mainPartyId?:string|null; }
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
export interface AccountingJournalRow { entryId:string;entryNumber:string;occurredAt:string;sourceDocumentId:string;sourceDocumentType:string;lineNumber:number;accountCode:string;accountName:string;partyId:string|null;costCenterId:string|null;costCenterCode:string|null;costCenterName:string|null;description:string;debit:number;credit:number; }
export interface GeneralLedgerRow { accountCode:string;accountName:string;accountType:string;openingBalance:number;debit:number;credit:number;closingBalance:number; }
export interface FinancialStatementRow { section:string;accountCode:string;accountName:string;amount:number; }
export interface AccountingExceptionRow { sourceDocumentId:string;sourceDocumentType:string;occurredAt:string;status:string;errorCode:string|null;errorMessage:string|null; }
export interface AccountingDocumentRow { mainPartyId?:string|null;mainPartyName?:string|null;mainPartyIdentification?:string|null;hasManualDraft?:boolean; sourceDocumentId:string;sourceDocumentType:string;sourceDocumentNumber:string|null;occurredAt:string;status:string;attemptCount:number;errorCode:string|null;errorMessage:string|null;entryId:string|null;entryNumber:string|null;debitTotal:number|null;creditTotal:number|null;postedAt:string|null;fiscalDocumentType:string|null;dianNumber:string|null;uniqueCodeType:string|null;uniqueCode:string|null;fiscalStatus:string|null; }
export interface AccountingDocumentPage { items:AccountingDocumentRow[];page:number;pageSize:number;totalCount:number;totalPages:number; }
export interface ComplianceReportDefinition { authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;name:string;reportKind:"Exogenous"|"FiscalDraft";resolutionNumber:string;resolutionDate:string;technicalAnnex:string;sourceUrl:string;sourceSha256:string; }
export interface ComplianceConceptMapping { mappingId:string;tenantId:string;businessId:string|null;authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;accountId:string;accountCode:string;accountName:string;conceptCode:string;targetField:string; }
export interface ComplianceValidation { severity:"Error"|"Warning";code:string;message:string;partyId:string|null;accountId:string|null; }
export interface ComplianceReportRun { runId:string;authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;name:string;reportKind:"Exogenous"|"FiscalDraft";periodFrom:string;periodTo:string;status:"Blocked"|"Ready";resolutionNumber:string;sourceUrl:string;sourceSha256:string;rowCount:number;controlTotal:number;createdAt:string;validations:ComplianceValidation[]; }
export interface SetComplianceConceptMapping { businessId:string|null;authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;accountId:string;conceptCode:string;targetField:string; }
export interface GenerateComplianceReport { authorityCode:string;taxYear:number;formatCode:string;formatVersion:number;periodFrom:string;periodTo:string; }

export const accountingApi = {
  openPositions: (params: { subledgerKind: AccountingOpenPosition["subledgerKind"] | "All"; page: number; pageSize: number;
    partyId?: string; accountId?: string; search?: string; dueFrom?: string; dueTo?: string;
    subledgerId?: string; counterpartyRoleId?: string; manualSupportOnly?: boolean; effectiveOn?: string }) => apiClient.get<AccountingOpenPositionPage>("/commerce/v1/accounting/open-positions", params),
  voucherDraft: (id: string) => apiClient.get<VoucherDraft>(`/commerce/v1/accounting/manual/drafts/${id}`),
  unapplicationCapture: (subledgerKind: "Payable" | "Receivable", subledgerId: string, transactionId: string) =>
    apiClient.get<VoucherDraftPreparation>(`/commerce/v1/accounting/unapplication-capture?${new URLSearchParams({ subledgerKind, subledgerId, transactionId })}`),
  paymentCorrectionCapture: (subledgerKind: "Payable" | "Receivable", paymentId: string,
    correctionKind: "AnnulRegistration" | "ReturnMoney") =>
    apiClient.get<VoucherDraftPreparation>(`/commerce/v1/accounting/payment-correction-capture?${new URLSearchParams({ subledgerKind, paymentId, correctionKind })}`),
  supportCancellationCapture: (payableId: string) => apiClient.get<VoucherDraftPreparation>("/commerce/v1/accounting/support-cancellation-capture", { payableId }),
  saveVoucherDraft: (request: SaveVoucherDraft) => apiClient.put<VoucherDraft>(`/commerce/v1/accounting/manual/drafts/${request.documentId}`, request),
  previewVoucherFiscal: (businessId: string, request: SaveVoucherDraft) => apiClient.post<VoucherFiscalPreview>("/commerce/v1/accounting/manual/fiscal-preview", {
    voucherId: request.documentId, businessId, occurredAt: request.occurredAt, conceptCode: request.conceptCode,
    description: request.description, reference: request.reference, mainPartyId: request.mainPartyId,
    payloadVersion: request.payloadVersion, voucherTypeCode: request.voucherTypeCode, workSessionId: request.workSessionId,
    currencyCode: "COP", lines: request.lines, fiscalCapture: request.fiscalCapture,
  }),
  cloneVoucher: (sourceId: string, documentId: string, occurredAt: string) => apiClient.post<VoucherDraft>(`/commerce/v1/accounting/documents/${sourceId}/clone`, { documentId, occurredAt }),
  sendVoucherDraft: (id: string, rowVersion: string) => apiClient.post<VoucherDraft>(`/commerce/v1/accounting/manual/drafts/${id}/send`, { rowVersion }),
  accountOptions:(params:{page:number;pageSize:number;search?:string;expenseOnly?:boolean;liabilityOnly?:boolean;accountId?:string;groupingOnly?:boolean})=>apiClient.get<{items:AccountingAccount[];page:number;pageSize:number;totalCount:number;totalPages:number}>("/commerce/v1/accounting/account-options",params),
  accounts: () => apiClient.get<AccountingAccount[]>("/commerce/v1/accounting/accounts"),
  bankAccounts: (includeInactive=false) => apiClient.get<BankAccount[]>("/commerce/v1/accounting/bank-accounts", {includeInactive}),
  saveBankAccount: (request:SaveBankAccount) => apiClient.put<BankAccount>(`/commerce/v1/accounting/bank-accounts/${request.bankAccountId}`,request),
  bankReconciliations: (query:BankReconciliationQuery) => apiClient.get<BankReconciliationPage>(`/commerce/v1/accounting/bank-reconciliations?${new URLSearchParams(Object.entries(query).filter(([,value])=>value!=null&&value!=="").map(([key,value])=>[key,String(value)]))}`),
  bankReconciliation: (id:string,query:BankReconciliationDetailQuery) => apiClient.get<BankReconciliationDetail>(`/commerce/v1/accounting/bank-reconciliations/${id}?${new URLSearchParams(Object.entries(query).filter(([,value])=>value!=null&&value!=="").map(([key,value])=>[key,String(value)]))}`),
  importBankReconciliation: (request:ImportBankReconciliation) => apiClient.post<BankReconciliationDetail>("/commerce/v1/accounting/bank-reconciliations",request),
  allocateBankReconciliation: (id:string,request:{matchId:string;statementLineId:string;entryId:string;entryLineNumber:number;amount:number;rowVersion:string}) => apiClient.post<BankReconciliationDetail>(`/commerce/v1/accounting/bank-reconciliations/${id}/allocations`,request),
  reverseBankReconciliationAllocation: (id:string,matchId:string,reason:string,rowVersion:string) => apiClient.post<BankReconciliationDetail>(`/commerce/v1/accounting/bank-reconciliations/${id}/allocations/${matchId}/reverse`,{reason,rowVersion}),
  closeBankReconciliation: (id:string,rowVersion:string) => apiClient.post<BankReconciliationDetail>(`/commerce/v1/accounting/bank-reconciliations/${id}/close`,{rowVersion}),
  reopenBankReconciliation: (id:string,rowVersion:string,reason:string) => apiClient.post<BankReconciliationDetail>(`/commerce/v1/accounting/bank-reconciliations/${id}/reopen`,{rowVersion,reason}),
  costCenters: () => apiClient.get<AccountingCostCenter[]>("/commerce/v1/accounting/cost-centers"),
  costCenterAssignments: () => apiClient.get<AccountingCostCenterAssignment[]>("/commerce/v1/accounting/cost-center-assignments"),
  saveCostCenterAssignment: (request:{assignmentId:string;businessId:string;costCenterId:string;operationKind:AccountingCostCenterAssignment["operationKind"];warehouseId:string|null;isActive:boolean;rowVersion?:string|null}) => apiClient.put<AccountingCostCenterAssignment>("/commerce/v1/accounting/cost-center-assignments",request),
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
  accountPage: (params: {page:number;pageSize:number;search?:string}) => apiClient.get<AccountingAccountPage>("/commerce/v1/accounting/accounts", params),
  updateAccount: (accountId:string, request:{name:string;requiresParty:boolean;rowVersion:string;semantics?:AccountingAccount["semantics"]}) => apiClient.put<AccountingAccount>(`/commerce/v1/accounting/accounts/${accountId}`, request),
  createCostCenter: (request: CreateCostCenter) => apiClient.post<AccountingCostCenter>("/commerce/v1/accounting/cost-centers", request),
  updateCostCenter: (costCenterId:string, request:Omit<AccountingCostCenter,"costCenterId"|"businessId">) => apiClient.put<AccountingCostCenter>(`/commerce/v1/accounting/cost-centers/${costCenterId}`,request),
  setCostCenterStatus: (costCenterId:string,isActive:boolean,rowVersion:string) => apiClient.put<AccountingCostCenter>(`/commerce/v1/accounting/cost-centers/${costCenterId}/status`,{isActive,rowVersion}),
  createPeriod: (request: CreatePeriod) => apiClient.post<AccountingPeriod>("/commerce/v1/accounting/periods", request),
  setMapping: (request: SetAccountMapping) => apiClient.put<void>("/commerce/v1/accounting/account-mappings", request),
  closePeriod: (periodId: string) => apiClient.post<void>(`/commerce/v1/accounting/periods/${periodId}/close`, {}),
  confirmAdjustment: (request: ConfirmAccountAdjustment) => apiClient.post<AccountingManualAcceptance>("/commerce/v1/accounting/manual/account-adjustments", request),
  confirmVoucher: (request: ConfirmManualVoucher) => apiClient.post<AccountingManualAcceptance>("/commerce/v1/accounting/manual/vouchers", request),
  posting: (documentId:string) => apiClient.get<AccountingPosting>(`/commerce/v1/accounting/postings/by-document/${documentId}`),
  retryPosting: (documentId:string) => apiClient.post<AccountingPosting>(`/commerce/v1/accounting/postings/${documentId}/retry`,{}),
  entry: (documentId:string) => apiClient.get<AccountingEntry>(`/commerce/v1/accounting/entries/by-document/${documentId}`),
  documents: (params:{from:string;to:string;documentType?:string;status?:string;search?:string;partyId?:string;page?:number;pageSize?:number}) => apiClient.get<AccountingDocumentPage>("/commerce/v1/accounting/documents",params),
  trialBalance: (from: string, to: string) => apiClient.get<TrialBalanceRow[]>(`/commerce/v1/accounting/reports/trial-balance?from=${from}&to=${to}`),
  accountMovements: (query: AccountMovementQuery) => apiClient.get<AccountMovementPage>("/commerce/v1/accounting/reports/account-movements",{...query}),
  accountMovementReport: (query: Omit<AccountMovementQuery,"page"|"pageSize">) => apiClient.get<AccountMovementPage>("/commerce/v1/accounting/reports/account-movements/print",{...query}),
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

export interface VoucherPaymentEvidence { cardFranchiseCode?:string|null;approvalNumber?:string|null;tenderedAmount?:number|null;notes?:string|null; }
export interface VoucherDraftLine { accountId:string|null;partyId:string|null;costCenterId:string|null;description:string;debit:number;credit:number;reference?:string|null;lineId?:string|null;baseAmount?:number|null;
  existingPosition?:{subledgerKind:"Payable"|"Receivable"|"SupplierCredit"|"CustomerCredit";subledgerId:string;reversesTransactionId?:string|null}|null;
  financialGroupId?:string|null;dueDate?:string|null;partySiteId?:string|null;paymentEvidence?:VoucherPaymentEvidence|null;
  financialDiscountEarned?:boolean|null; }
export interface VoucherDraftAdjustment { subledgerKind:"Receivable"|"Payable";subledgerId:string;direction:"Increase"|"Decrease";amount:number;counterpartAccountId:string|null;costCenterId:string|null; }
export interface VoucherFiscalAcquisition {
  sourceLineId:string;taxExclusiveAmount:number;taxProfileId:string|null;
  taxTreatment:"DeductibleInputVat"|"CapitalizedCost";inputVatSourceLineId?:string|null;withholdingConceptCode?:string|null;
}
export interface VoucherFiscalCapture {
  acquisitions:VoucherFiscalAcquisition[];
  withholdingBindings?:{sourceLineId:string;ruleId?:string|null;manualLineId?:string|null}[]|null;
  withholdingAdjustments?:WithholdingAdjustment[]|null;calculationHash?:string|null;
}
export interface VoucherFiscalPreview {
  acquisitions:{value:VoucherFiscalAcquisition;lineNumber:number;accountCode:string;description:string;taxName:string;dianTaxCode:string;taxRate:number;vatAmount:number}[];
  withholding:WithholdingCalculation;calculationHash:string;diagnostics:string[];canConfirm:boolean;
  payableSourceLineId?:string|null;supplierPartySiteId?:string|null;
}
export interface SaveVoucherDraft { payloadVersion?:number|null;clonedFromDocumentId?:string|null;mainPartyId?:string|null;voucherTypeCode?:string|null;workSessionId?:string|null;fiscalCapture?:VoucherFiscalCapture|null; documentId:string;documentType:"ManualAccountingVoucher"|"AccountAdjustment";occurredAt:string;conceptCode:string;description:string;reference:string|null;lines:VoucherDraftLine[];adjustment:VoucherDraftAdjustment|null;rowVersion:string|null; }
export interface VoucherDraftLineView { value:VoucherDraftLine;accountCode:string|null;accountName:string|null;partyName:string|null;partyIdentification:string|null;costCenterName:string|null;validationError?:string|null; }
export interface VoucherDraftPreparation { description:string;reference:string|null;mainPartyId:string;mainPartyName:string;mainPartyIdentification:string|null;lines:VoucherDraftLineView[]; }
export interface VoucherDraft extends Omit<SaveVoucherDraft,"lines"> { mainPartyName?:string|null;mainPartyIdentification?:string|null;lines:VoucherDraftLineView[];currencyCode:string;rowVersion:string;sentAt:string|null;status:string;canEdit:boolean;canSend:boolean;row:AccountingDocumentRow; }
