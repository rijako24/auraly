import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Campaign, CreateCampaignRequest } from "@/types/entities";

export const campaignsApi = {
  list: (params?: Partial<PagedRequest> & { businessId?: string }) =>
    apiClient.get<PagedResponse<Campaign>>("/campaigns", withPagedDefaults(params)),
  getById: (id: string) => apiClient.get<Campaign>(`/campaigns/${id}`),
  create: (data: CreateCampaignRequest) => apiClient.post<Campaign>("/campaigns", data),
};
