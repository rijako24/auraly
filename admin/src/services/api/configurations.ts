import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type {
  BusinessConfiguration,
  SystemConfiguration,
} from "@/types/entities";

/** Backend returns { configurations: Record<string, string> } - keys are BusinessConfigurationKey enum values */
export interface BusinessConfigurationsResponse {
  configurations: Record<string, string>;
}

export const configurationsApi = {
  // Business configuration (GET/PUT dictionary by business)
  getBusinessConfigurations: (businessId: string) =>
    apiClient.get<BusinessConfigurationsResponse>(
      `/businesses/${businessId}/configurations`
    ),
  updateBusinessConfigurations: (
    businessId: string,
    data: { configurations: Record<string, string> }
  ) =>
    apiClient.put<BusinessConfigurationsResponse>(
      `/businesses/${businessId}/configurations`,
      data
    ),

  // Legacy CRUD (not used by current backend - kept for compatibility)
  listBusinessConfigurations: (
    businessId: string,
    params?: Partial<PagedRequest>
  ) =>
    apiClient.get<PagedResponse<BusinessConfiguration>>(
      `/businesses/${businessId}/configurations`,
      params as Record<string, string | number | undefined>
    ),
  getBusinessConfigurationById: (
    businessId: string,
    configurationId: string
  ) =>
    apiClient.get<BusinessConfiguration>(
      `/businesses/${businessId}/configurations/${configurationId}`
    ),
  createBusinessConfiguration: (
    businessId: string,
    data: Partial<BusinessConfiguration>
  ) =>
    apiClient.post<BusinessConfiguration>(
      `/businesses/${businessId}/configurations`,
      data
    ),
  updateBusinessConfiguration: (
    businessId: string,
    configurationId: string,
    data: Partial<BusinessConfiguration>
  ) =>
    apiClient.put<BusinessConfiguration>(
      `/businesses/${businessId}/configurations/${configurationId}`,
      data
    ),
  deleteBusinessConfiguration: (
    businessId: string,
    configurationId: string
  ) =>
    apiClient.delete(
      `/businesses/${businessId}/configurations/${configurationId}`
    ),

  // SystemConfiguration
  listSystemConfigurations: (params?: Partial<PagedRequest>) =>
    apiClient.get<PagedResponse<SystemConfiguration>>(
      "/configurations/system",
      params as Record<string, string | number | undefined>
    ),
  getSystemConfigurationById: (id: string) =>
    apiClient.get<SystemConfiguration>(`/configurations/system/${id}`),
  updateSystemConfiguration: (
    id: string,
    data: Partial<SystemConfiguration>
  ) =>
    apiClient.put<SystemConfiguration>(`/configurations/system/${id}`, data),
};
