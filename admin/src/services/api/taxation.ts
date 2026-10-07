import { apiClient } from "./client";

export type WithholdingKind = "IncomeTax" | "Vat" | "IndustryCommerce";
export type WithholdingDirection = "Purchase" | "Sale";
export type WithholdingRecognitionMoment = "Accrual" | "Payment";
export type WithholdingBaseKind = "TaxExclusiveAmount" | "VatAmount";

export interface WithholdingRule {
  ruleId: string; businessId: string; version: number; code: string; name: string;
  kind: WithholdingKind; direction: WithholdingDirection;
  moment: WithholdingRecognitionMoment; baseKind: WithholdingBaseKind;
  conceptCode: string | null; jurisdictionCode: string | null; rate: number;
  minimumBase: number; requiredResponsibilities: string[]; effectiveFrom: string;
  effectiveTo: string | null; isActive: boolean; appliesAutomatically: boolean;
  defaultAccountId: string | null;
}


export interface CounterpartyTaxProfile {
  businessId: string; counterpartyId: string; appliesWithholding: boolean; responsibilities: string[];
  jurisdictionCode: string | null; updatedAt: string;
}

export interface AppliedWithholdingReport {
  from: string; to: string; page: number; pageSize: number; totalCount: number;
  incomeTaxTotal: number; vatTotal: number; industryCommerceTotal: number;
  items: Array<{ documentId: string; documentType: string; lineNumber: number;
    recognizedAt: string; documentNumber: string; supplierName: string;
    supplierIdentification: string; kind: WithholdingKind; name: string;
    ruleCode: string; jurisdictionCode: string | null; taxableBase: number;
    rate: number; amount: number; isManual: boolean }>;
}

export interface SaveCounterpartyTaxProfile {
  businessId: string; counterpartyId: string; appliesWithholding: boolean; responsibilities: string[];
  jurisdictionCode: string | null;
}
export type SaveWithholdingRule = Omit<WithholdingRule, "ruleId" | "version" | "appliesAutomatically" | "defaultAccountId"> &
  { appliesAutomatically?: boolean; defaultAccountId?: string | null };

export interface WithholdingPreview {
  businessId: string; direction: WithholdingDirection; moment: "Accrual" | "Payment";
  counterpartyId: string; conceptCode?: string; jurisdictionCode?: string;
  taxExclusiveAmount: number; vatAmount: number; occurredAt: string;
  counterpartyResponsibilities?: string[]; previouslyRecognizedRuleIds?: string[];
}

export interface WithholdingCalculation {
  grossAmount: number; withholdingTotal: number; netAmount: number;
  lines: Array<{ ruleId: string; ruleVersion: number; ruleCode: string; name: string;
    kind: WithholdingKind; baseKind: WithholdingBaseKind; taxableBase: number;
    rate: number; amount: number; jurisdictionCode: string | null;
    accountId?: string | null; manualLineId?: string | null; }>;
  adjustments?: Array<{ruleId:string;ruleVersion:number;action:"Add"|"Override"|"Exclude"|"Manual";
    automaticTaxableBase:number|null;automaticAmount:number|null;taxableBase:number|null;
    amount:number|null;reason:string;adjustedByUserId:string}>|null;
}

export type WithholdingAdjustment = {ruleId:string;action:"Add"|"Override"|"Exclude"|"Manual";
  taxableBase:number|null;amount:number|null;reason:string;
  manualLineId?:string|null;kind?:WithholdingKind|null;name?:string|null;rate?:number|null;
  jurisdictionCode?:string|null;accountId?:string|null};

export const taxationApi = {
  listApplied: (from: string, to: string, page: number, pageSize = 25) =>
    apiClient.get<AppliedWithholdingReport>("/commerce/v1/taxation/withholdings/applied",
      { from, to, page, pageSize }),
  listRules: (includeInactive = false) => apiClient.get<WithholdingRule[]>(
    "/commerce/v1/taxation/withholding-rules", { includeInactive }),
  createRule: (request: SaveWithholdingRule) => apiClient.post<WithholdingRule>(
    "/commerce/v1/taxation/withholding-rules", request),
  updateRule: (ruleId: string, request: SaveWithholdingRule) => apiClient.put<WithholdingRule>(
    `/commerce/v1/taxation/withholding-rules/${ruleId}`, request),
  preview: (request: WithholdingPreview) => apiClient.post<WithholdingCalculation>(
    "/commerce/v1/taxation/withholdings/preview", request),
  getProfile: (counterpartyId: string) => apiClient.get<CounterpartyTaxProfile>(
    `/commerce/v1/taxation/counterparty-profiles/${counterpartyId}`),
  saveProfile: (request: SaveCounterpartyTaxProfile) => apiClient.put<CounterpartyTaxProfile>(
    `/commerce/v1/taxation/counterparty-profiles/${request.counterpartyId}`, request),
};
