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

export const productsApi = {
  list: (businessId: string, params?: Partial<PagedRequest> & { includeInactive?: boolean }) => apiClient.get<PagedResponse<Product>>(`/businesses/${businessId}/products`, withPagedDefaults(params)),
  updateStatus: (businessId: string, productId: string, isActive: boolean) => apiClient.patch<Product>(`/businesses/${businessId}/products/${productId}/status`, { isActive }),
};