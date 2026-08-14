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
  effectiveTo: string | null; isActive: boolean;
}


export interface CounterpartyTaxProfile {
  businessId: string; counterpartyId: string; responsibilities: string[];
  jurisdictionCode: string | null; updatedAt: string;
}

export interface SaveCounterpartyTaxProfile {
  businessId: string; counterpartyId: string; responsibilities: string[];
  jurisdictionCode: string | null;
}
export type SaveWithholdingRule = Omit<WithholdingRule, "ruleId" | "version">;

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
    rate: number; amount: number; jurisdictionCode: string | null; }>;
}

export const taxationApi = {
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
