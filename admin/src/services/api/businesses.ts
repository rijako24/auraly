import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Business } from "@/types/entities";

export const businessesApi = {
  list: (params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<Business>>(
      "/businesses",
      withPagedDefaults(params)
    ),
  getById: (id: string) => apiClient.get<Business>(`/businesses/${id}`),
  create: (data: Partial<Business>) =>
    apiClient.post<Business>("/businesses", data),
  update: (id: string, data: Partial<Business>) =>
    apiClient.put<Business>(`/businesses/${id}`, data),
  delete: (id: string) => apiClient.delete(`/businesses/${id}`),
};
