import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";

export enum ProductSource { Local = 0, External = 1 }
export enum ProductAliasScope { Business = 0, Customer = 1 }
export enum ProductAliasKind { Alias = 0, Keyword = 1, Misspelling = 2 }
export enum ProductAliasResolutionMode { SuggestOnly = 0, AutoResolve = 1 }
export enum ProductAliasSource { Manual = 0, Imported = 1, Learned = 2 }
export enum ProductAliasStatus { Pending = 0, Active = 1, Rejected = 2 }
export enum ProductAliasReviewAction { Approve = 0, Reject = 1 }

export interface Product {
  productId: string;
  businessId: string;
  integrationConnectionId?: string | null;
  externalProductId?: string | null;
  source?: ProductSource;
  sku?: string | null;
  productCode?: string | null;
  reference?: string | null;
  name: string;
  description?: string | null;
  categoryName?: string | null;
  areaName?: string | null;
  unitPrice: number;
  currency: string;
  manageStock: boolean;
  stockQuantity?: number | null;
  isActive: boolean;
  createdAt?: string;
  updatedAt?: string | null;
  lastSyncedAt?: string | null;
}

export interface ProductCategory {
  productCategoryId: string;
  parentProductCategoryId: string | null;
  name: string;
  displayOrder: number;
  isActive: boolean;
  isBrowsable: boolean;
  depth: number;
  path: string;
}

export interface ProductCategoryPayload {
  parentProductCategoryId: string | null;
  name: string;
  displayOrder: number;
  isBrowsable: boolean;
  isActive?: boolean;
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

export interface ProductConfiguration { aliases: ProductAlias[]; searchTerms: string[] }

export interface CreateCatalogProductRequest {
  businessId: string;
  productCode: string;
  reference: string | null;
  name: string;
  description: string | null;
  baseUnitCode: string;
  taxProfileId: string;
  purchaseTaxProfileId: string;
  purchaseTaxTreatment: "DeductibleInputVat" | "CapitalizedCost" | "NotApplicable";
  manageInventory: boolean;
  isWeighable: boolean;
  barcodes: Array<{ value: string; isPrimary: boolean }>;
  identifiers: Array<{ type: string; value: string }>;
  prices: Array<{
    amount: number;
    currencyCode: string;
    costBasisAmount: number;
    targetMarginPercent: number;
    preparedAmount?: number | null;
    inputMode?: "Margin" | "SalePrice";
    roundingIncrement?: number;
    roundingMode?: "Nearest" | "Up" | "Down";
  }>;
  suppliers: Array<{
    supplierId: string;
    identification: string;
    name: string;
    supplierProductCode: string | null;
    baseUnitCost: number;
    isPrimary: boolean;
    purchasePresentationName: string;
    unitsPerPresentation: number;
  }>;
  scale: null | {
    scaleCode: string;
    barcodePrefix: string;
    embeddedValueType: "Weight" | "Price";
    valueStart: number;
    valueLength: number;
    decimalPlaces: number;
  };
  productCategoryId: string | null;
  productBrandId: string | null;
  allowsFractionalSale: boolean;
  link: null | { parentProductId: string; sharesInventory: boolean; inventoryFactor: number | null;
    sharesPrice: boolean; priceFactor: number | null; allowsConversion: boolean; conversionFactor: number | null };
  linkedProducts?: Array<{ childProductId: string; sharesInventory: boolean; inventoryFactor: number | null;
    sharesPrice: boolean; priceFactor: number | null; allowsConversion: boolean; conversionFactor: number | null }>;
  conversionMaximumLossPercent?: number | null;
  aliases?: Array<{ alias: string }>;
  images?: Array<{
    productImageId: string;
    productOfferId: string | null;
    mediaReference: string;
    altText: string | null;
    displayOrder: number;
    isPrimary: boolean;
  }>;


}

export interface CatalogProductDetail { productId: string; businessId: string; productCode: string; reference: string | null; name: string; isActive: boolean }
export interface ReviewProductAliasRequest { action: ProductAliasReviewAction; resolutionMode: ProductAliasResolutionMode }
export interface PromoteProductAliasRequest { resolutionMode: ProductAliasResolutionMode }

export const productsApi = {
  createCatalog: (request: CreateCatalogProductRequest) => apiClient.post<CatalogProductDetail>("/commerce/v1/products", request),
  updateCatalog: (productId: string, request: CreateCatalogProductRequest) =>
    apiClient.put<CatalogProductDetail>(`/commerce/v1/products/${productId}`, request),
  getCatalog: (productId: string) => apiClient.get<{
    productId: string; businessId: string; productCode: string; reference: string | null; name: string;
    isActive: boolean; barcodes: string[]; prices: Array<{ amount: number; currencyCode: string; costBasisAmount: number | null; targetMarginPercent: number | null }>;
    suppliers: Array<{ supplierId: string; identification: string; name: string; supplierProductCode: string | null;
      baseUnitCost: number; isPrimary: boolean; purchasePresentationName: string; unitsPerPresentation: number }> | null;
    salesTaxProfileId: string; purchaseTaxProfileId: string; purchaseTaxTreatment: string; description: string | null;
    baseUnitCode: string; manageInventory: boolean; isWeighable: boolean;
  }>(`/commerce/v1/products/${productId}`),
  listCategories: (businessId: string, includeInactive = false) => apiClient.get<ProductCategory[]>(`/businesses/${businessId}/product-categories`, { includeInactive }),
  createCategory: (businessId: string, request: ProductCategoryPayload) => apiClient.post<ProductCategory>(`/businesses/${businessId}/product-categories`, request),
  updateCategory: (businessId: string, categoryId: string, request: ProductCategoryPayload) => apiClient.put<ProductCategory>(`/businesses/${businessId}/product-categories/${categoryId}`, request),
  list: (businessId: string, params?: Partial<PagedRequest> & { includeInactive?: boolean }) => apiClient.get<PagedResponse<Product>>(`/businesses/${businessId}/products`, withPagedDefaults(params)),
  getConfiguration: async (businessId: string, productId: string): Promise<ProductConfiguration> => {
    const [aliases, searchTerms] = await Promise.all([
      apiClient.get<ProductAlias[]>(`/businesses/${businessId}/products/${productId}/aliases`),
      apiClient.get<string[]>(`/businesses/${businessId}/products/${productId}/search-terms`),
    ]);
    return { aliases, searchTerms };
  },
  updateStatus: (_businessId: string, productId: string, isActive: boolean) => apiClient.patch<void>(`/commerce/v1/products/${productId}/status`, { isActive }),
  addManualAlias: (businessId: string, productId: string, alias: string) =>
    apiClient.post<{ created: number; updated: number; skipped: number; errors: Array<{ message: string }> }>(`/businesses/${businessId}/products/aliases/import`, {
      items: [{ alias, productId, kind: ProductAliasKind.Alias, resolutionMode: ProductAliasResolutionMode.AutoResolve,
        scope: ProductAliasScope.Business, status: ProductAliasStatus.Active }],
      dryRun: false,
    }),  reviewAlias: (businessId: string, productId: string, productAliasId: string, request: ReviewProductAliasRequest) => apiClient.put<ProductAlias>(`/businesses/${businessId}/products/${productId}/aliases/${productAliasId}/review`, request),
  promoteAlias: (businessId: string, productId: string, productAliasId: string, request: PromoteProductAliasRequest) => apiClient.post<ProductAlias>(`/businesses/${businessId}/products/${productId}/aliases/${productAliasId}/promote`, request),
};
