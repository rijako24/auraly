import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";

export enum ProductSource {
  Local = 0,
  External = 1,
}

export enum ProductAliasScope {
  Business = 0,
  Customer = 1,
}

export enum ProductAliasKind {
  Alias = 0,
  Keyword = 1,
  Misspelling = 2,
}

export enum ProductAliasResolutionMode {
  SuggestOnly = 0,
  AutoResolve = 1,
}

export enum ProductAliasSource {
  Manual = 0,
  Imported = 1,
  Learned = 2,
}

export enum ProductAliasStatus {
  Pending = 0,
  Active = 1,
  Rejected = 2,
}

export enum ProductAliasReviewAction {
  Approve = 0,
  Reject = 1,
}

export interface Product {
  productId: string;
  businessId: string;
  integrationConnectionId?: string | null;
  externalProductId?: string | null;
  source?: ProductSource;
  sku?: string | null;
  name: string;
  description?: string | null;
  categoryName?: string | null;
  unitPrice: number;
  currency: string;
  manageStock: boolean;
  stockQuantity?: number | null;
  isActive: boolean;
  createdAt?: string;
  updatedAt?: string | null;
  lastSyncedAt?: string | null;
}

export interface ProductAlias {
  productAliasId: string;
  productId: string;
  productName: string;
  alias: string;
  scope: ProductAliasScope;
  customerKey: string;
  kind: ProductAliasKind;
  resolutionMode: ProductAliasResolutionMode;
  source: ProductAliasSource;
  status: ProductAliasStatus;
  usageCount: number;
  lastConfirmedAt?: string | null;
  normalizedAlias: string;
  sharedMappingCount: number;
  distinctProductCount: number;
  businessMappingCount: number;
  distinctCustomerCount: number;
}

export interface ProductConfiguration {
  aliases: ProductAlias[];
  searchTerms: string[];
}

export interface UpdateProductRequest {
  name: string;
  description: string | null;
  categoryName: string | null;
  unitPrice: number;
  currency: string;
}

export interface ReviewProductAliasRequest {
  action: ProductAliasReviewAction;
  resolutionMode: ProductAliasResolutionMode;
}

export interface PromoteProductAliasRequest {
  resolutionMode: ProductAliasResolutionMode;
}

export const productsApi = {
  list: (businessId: string, params?: Partial<PagedRequest> & { includeInactive?: boolean }) =>
    apiClient.get<PagedResponse<Product>>(`/businesses/${businessId}/products`, withPagedDefaults(params)),
  getConfiguration: async (businessId: string, productId: string): Promise<ProductConfiguration> => {
    const [aliases, searchTerms] = await Promise.all([
      apiClient.get<ProductAlias[]>(`/businesses/${businessId}/products/${productId}/aliases`),
      apiClient.get<string[]>(`/businesses/${businessId}/products/${productId}/search-terms`),
    ]);
    return { aliases, searchTerms };
  },
  update: (businessId: string, productId: string, request: UpdateProductRequest) =>
    apiClient.put<Product>(`/businesses/${businessId}/products/${productId}`, request),
  updateStatus: (businessId: string, productId: string, isActive: boolean) =>
    apiClient.patch<Product>(`/businesses/${businessId}/products/${productId}/status`, { isActive }),
  reviewAlias: (businessId: string, productId: string, productAliasId: string, request: ReviewProductAliasRequest) =>
    apiClient.put<ProductAlias>(`/businesses/${businessId}/products/${productId}/aliases/${productAliasId}/review`, request),
  promoteAlias: (businessId: string, productId: string, productAliasId: string, request: PromoteProductAliasRequest) =>
    apiClient.post<ProductAlias>(`/businesses/${businessId}/products/${productId}/aliases/${productAliasId}/promote`, request),
};