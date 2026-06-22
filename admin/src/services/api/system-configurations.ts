import type { PagedRequest, PagedResponse } from "@/types/api";
import type { SystemConfiguration } from "@/types/entities";
import { apiClient, withPagedDefaults } from "./client";

export const systemConfigurationsApi = {
  listSystemConfigurations: (params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<SystemConfiguration>>("/system/configurations", withPagedDefaults(params)),

  updateSystemConfiguration: (
    id: number | string,
    data: Partial<SystemConfiguration>
  ) => apiClient.put<SystemConfiguration>(`/system/configurations/${id}`, data),
};


