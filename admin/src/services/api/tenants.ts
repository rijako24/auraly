import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Tenant } from "@/types/entities";

export const tenantsApi = {
  list: (params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<Tenant>>(
      "/tenants",
      withPagedDefaults(params)
    ),
  getById: (id: string) => apiClient.get<Tenant>(`/tenants/${id}`),
  create: (data: Partial<Tenant>) =>
    apiClient.post<Tenant>("/tenants", data),
  update: (id: string, data: Partial<Tenant>) =>
    apiClient.put<Tenant>(`/tenants/${id}`, data),
  delete: (id: string) => apiClient.delete(`/tenants/${id}`),
};
