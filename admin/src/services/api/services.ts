import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Service, ServiceCategory } from "@/types/entities";

export const servicesApi = {
  // Services CRUD
  list: (params?: Partial<PagedRequest> & { businessId?: string }) =>
    apiClient.get<PagedResponse<Service>>(
      "/services",
      withPagedDefaults(params)
    ),
  listByBusiness: (businessId: string, params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<Service>>(
      "/services",
      withPagedDefaults({ ...params, businessId })
    ),
  getById: (id: string) => apiClient.get<Service>(`/services/${id}`),
  create: (data: Partial<Service>) =>
    apiClient.post<Service>("/services", data),
  update: (id: string, data: Partial<Service>) =>
    apiClient.put<Service>(`/services/${id}`, data),
  delete: (id: string) => apiClient.delete(`/services/${id}`),

  // Service categories CRUD
  listCategories: (params?: Partial<PagedRequest> & { businessId?: string }) =>
    apiClient.get<PagedResponse<ServiceCategory>>(
      "/service-categories",
      withPagedDefaults(params)
    ),
  listCategoriesByBusiness: (
    businessId: string,
    params?: Partial<PagedRequest>
  ) =>
    apiClient.get<PagedResponse<ServiceCategory>>(
      "/service-categories",
      withPagedDefaults({ ...params, businessId })
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
