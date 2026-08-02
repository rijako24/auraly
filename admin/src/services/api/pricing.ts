import { apiClient, withPagedDefaults } from "./client";

export type PriceProposalStatus =
  | "PendingReview" | "Approved" | "Published" | "Rejected" | "Superseded";
export type PriceInputMode = "Margin" | "SalePrice";
export type PricingRoundingMode = "Nearest" | "Up" | "Down";

export interface PriceRevisionListItem {
  proposalId: string;
  productId: string;
  productCode: string;
  productName: string;
  sourceDocumentId: string;
  sourceLineNumber: number;
  supplierName: string;
  previousObservedUnitCost: number | null;
  observedUnitCost: number;
  currentSalePrice: number;
  currentMarginPercent: number | null;
  targetMarginPercent: number | null;
  suggestedSalePrice: number;
  effectiveMarginAfterRounding: number | null;
  status: PriceProposalStatus;
  createdAt: string;
  concurrencyToken: string;
}

export interface PriceRevisionPage {
  items: PriceRevisionListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface PriceCalculationRequest {
  costBasisAmount: number;
  inputMode: PriceInputMode;
  targetMarginPercent: number | null;
  salePrice: number | null;
  roundingIncrement: number;
  roundingMode: PricingRoundingMode;
}

export interface PriceCalculationResult extends PriceCalculationRequest {
  unroundedSalePrice: number;
  roundedSalePrice: number;
  effectiveMarginPercent: number | null;
}

export interface PublishPriceItem {
  proposalId: string;
  inputMode: PriceInputMode;
  targetMarginPercent: number | null;
  salePrice: number | null;
  roundingIncrement: number;
  roundingMode: PricingRoundingMode;
  concurrencyToken: string;
}

export interface PublishPricesResult {
  items: Array<{
    productPriceId: string;
    proposalId: string;
    productId: string;
    amount: number;
    effectiveMarginPercent: number | null;
    catalogCursor: number;
    publishedAt: string;
  }>;
  catalogCursor: number;
}

export const pricingApi = {
  list: (params: {
    page: number;
    pageSize: number;
    search?: string;
    status?: PriceProposalStatus;
    supplierId?: string;
    sourceDocumentId?: string;
  }) => apiClient.get<PriceRevisionPage>(
    "/commerce/v1/pricing/proposals", withPagedDefaults(params)),
  calculate: (request: PriceCalculationRequest) =>
    apiClient.post<PriceCalculationResult>("/commerce/v1/pricing/calculate", request),
  review: (proposalId: string, request: Omit<PublishPriceItem, "proposalId">) =>
    apiClient.put<void>(`/commerce/v1/pricing/proposals/${proposalId}`, request),
  reject: (proposalId: string, concurrencyToken: string, reason?: string) =>
    apiClient.post<void>(`/commerce/v1/pricing/proposals/${proposalId}/reject`, {
      concurrencyToken, reason: reason || null,
    }),
  publish: (items: PublishPriceItem[]) =>
    apiClient.post<PublishPricesResult>("/commerce/v1/pricing/publish", { items }),
};
