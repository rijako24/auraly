import { apiClient } from "./client";

export interface ProductBrand { productBrandId: string; name: string; isActive: boolean }
export interface ProductUnit { productUnitId: string; code: string; name: string; symbol: string; allowsFractionalQuantity: boolean; decimalPlaces: number; isActive: boolean }
export interface ProductBarcode { value: string; isPrimary: boolean }
export interface ProductScale { scaleCode: string; barcodePrefix: string; embeddedValueType: "Weight" | "Price"; valueStart: number; valueLength: number; decimalPlaces: number }
export interface ProductLink { parentProductId: string; parentProductCode: string; parentProductName: string; sharesInventory: boolean; inventoryFactor: number | null; sharesPrice: boolean; priceFactor: number | null }
export interface LinkedProduct { childProductId: string; childProductCode: string; childProductName: string; sharesInventory: boolean; inventoryFactor: number | null; sharesPrice: boolean; priceFactor: number | null }
export interface ProductMerchandising {
  productId: string;
  productCategoryId: string | null;
  productBrandId: string | null;
  baseUnitCode: string;
  allowsFractionalSale: boolean;
  isWeighable: boolean;
  scale: ProductScale | null;
  barcodes: ProductBarcode[];
  link: ProductLink | null;
  linkedProducts: LinkedProduct[];
}
export type SaveProductMerchandising = Omit<ProductMerchandising, "productId" | "link" | "linkedProducts"> & {
  link: null | Omit<ProductLink, "parentProductCode" | "parentProductName">;
  linkedProducts: Array<Omit<LinkedProduct, "childProductCode" | "childProductName">>;
};

export const productMerchandisingApi = {
  get: (productId: string) => apiClient.get<ProductMerchandising>(`/commerce/v1/products/${productId}/merchandising`),
  save: (productId: string, request: SaveProductMerchandising) => apiClient.put<ProductMerchandising>(`/commerce/v1/products/${productId}/merchandising`, request),
  brands: () => apiClient.get<ProductBrand[]>("/commerce/v1/product-brands"),
  createBrand: (name: string) => apiClient.post<ProductBrand>("/commerce/v1/product-brands", { name, isActive: true }),
  units: () => apiClient.get<ProductUnit[]>("/commerce/v1/product-units"),
  createUnit: (request: Omit<ProductUnit, "productUnitId" | "isActive">) => apiClient.post<ProductUnit>("/commerce/v1/product-units", { ...request, isActive: true }),
};
