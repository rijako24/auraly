import { apiClient } from "./client";

export interface TaxProfile {
  taxProfileId: string;
  businessId: string;
  code: string;
  dianTaxCode: string;
  name: string;
  rate: number;
  isActive: boolean;
}

export interface SaveTaxProfile {
  businessId: string;
  code: string;
  dianTaxCode: string;
  name: string;
  rate: number;
  isActive: boolean;
}

export interface ProductTaxConfiguration {
  productId: string;
  salesTaxProfileId: string;
  purchaseTaxProfileId: string;
  purchaseTaxTreatment: "DeductibleInputVat" | "CapitalizedCost" | "NotApplicable";
}

export const taxProfilesApi = {
  list: (includeInactive = false) =>
    apiClient.get<TaxProfile[]>("/commerce/v1/tax-profiles", { includeInactive }),
  create: (request: SaveTaxProfile) =>
    apiClient.post<TaxProfile>("/commerce/v1/tax-profiles", request),
  update: (id: string, request: SaveTaxProfile) =>
    apiClient.put<TaxProfile>("/commerce/v1/tax-profiles/" + id, request),
  getProduct: (productId: string) =>
    apiClient.get<ProductTaxConfiguration>("/commerce/v1/products/" + productId + "/tax-configuration"),
  saveProduct: (productId: string, request: Omit<ProductTaxConfiguration, "productId">) =>
    apiClient.put<ProductTaxConfiguration>("/commerce/v1/products/" + productId + "/tax-configuration", request),
};