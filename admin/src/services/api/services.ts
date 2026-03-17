import { apiClient } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Service, ServiceCategory } from "@/types/entities";

export const servicesApi = {
  // Services CRUD
  list: (params?: Partial<PagedRequest> & { businessId?: string }) =>
    apiClient.get<PagedResponse<Service>>(
      "/services",
      params as Record<string, string | number | undefined>
    ),
  listByBusiness: (businessId: string, params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<Service>>(
      `/businesses/${businessId}/services`,
      params as Record<string, string | number | undefined>
    ),
  getById: (id: string) => apiClient.get<Service>(`/services/${id}`),
  create: (data: Partial<Service>) =>
    apiClient.post<Service>("/services", data),
  update: (id: string, data: Partial<Service>) =>
    apiClient.put<Service>(`/services/${id}`, data),
  delete: (id: string) => apiClient.delete(`/services/${id}`),

  // Service categories CRUD
  listCategories: (params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<ServiceCategory>>(
      "/service-categories",
      params as Record<string, string | number | undefined>
    ),
  listCategoriesByBusiness: (
    businessId: string,
    params?: Partial<PagedRequest>
  ) =>
    apiClient.get<PagedResponse<ServiceCategory>>(
      `/businesses/${businessId}/service-categories`,
      params as Record<string, string | number | undefined>
    ),
  getCategoryById: (id: string) =>
    apiClient.get<ServiceCategory>(`/service-categories/${id}`),
  createCategory: (data: Partial<ServiceCategory>) =>
    apiClient.post<ServiceCategory>("/service-categories", data),
  updateCategory: (id: string, data: Partial<ServiceCategory>) =>
    apiClient.put<ServiceCategory>(`/service-categories/${id}`, data),
  deleteCategory: (id: string) =>
    apiClient.delete(`/service-categories/${id}`),
};
