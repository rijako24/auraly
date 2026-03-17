import { apiClient } from "./client";
import { PagedResponse, PagedRequest } from "@/types/api";
import { Lead } from "@/types/entities";

export const leadsApi = {
  list: (params?: Partial<PagedRequest> & { businessId?: string; status?: string }) =>
    apiClient.get<PagedResponse<Lead>>("/leads", params as Record<string, string | number | undefined>),
  getById: (id: string) => apiClient.get<Lead>(`/leads/${id}`),
  create: (data: Partial<Lead>) => apiClient.post<Lead>("/leads", data),
  update: (id: string, data: Partial<Lead>) => apiClient.put<Lead>(`/leads/${id}`, data),
  delete: (id: string) => apiClient.delete(`/leads/${id}`),
};
