import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";

export interface Product {
  productId: string;
  businessId: string;
  sku?: string | null;
  name: string;
  description?: string | null;
  categoryName?: string | null;
  unitPrice: number;
  currency: string;
  manageStock: boolean;
  stockQuantity?: number | null;
  isActive: boolean;
}

export interface UpdateProductRequest {
  name: string;
  description: string | null;
  categoryName: string | null;
  unitPrice: number;
  currency: string;
}

export const productsApi = {
  list: (businessId: string, params?: Partial<PagedRequest> & { includeInactive?: boolean }) => apiClient.get<PagedResponse<Product>>(`/businesses/${businessId}/products`, withPagedDefaults(params)),
  update: (businessId: string, productId: string, request: UpdateProductRequest) => apiClient.put<Product>(`/businesses/${businessId}/products/${productId}`, request),
  updateStatus: (businessId: string, productId: string, isActive: boolean) => apiClient.patch<Product>(`/businesses/${businessId}/products/${productId}/status`, { isActive }),
};
