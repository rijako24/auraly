import { apiClient, withPagedDefaults } from "./client";
import type { PagedRequest, PagedResponse } from "@/types/api";
import type { Tenant } from "@/types/entities";

export interface ProvisionTenantRequest {
  provisioningRequestId: string;
  legalName: string; tradeName: string; nit: string; verificationDigit: string;
  countryId: string; administrativeDivisionId: string; cityId: string;
  address: string; phone: string; email: string; taxResponsibilities: string;
  businessName: string; businessAddress: string; businessPhone: string; businessEmail: string;
  timeZone: string; inventoryCostBasis: "LatestReceiptCost" | "WeightedAverageCost";
  invitationEmail: string; maximumUsers: number; maximumEnrolledDevices: number;
}

export interface TenantEnrolledDevice {
  deviceId: string; name: string; isActive: boolean; createdAt: string;
  lastSeenAt: string | null; businessId: string | null; businessName: string | null;
}
export interface ProvisionTenantResult {
  provisioningRequestId: string; tenantId: string; tenantKey: string; businessId: string;
  salesWarehouseId: string; ordersWarehouseId: string; defaultCustomerId: string;
  administratorUserId: string | null; status: string;
}

export const tenantsApi = {
  list: (params?: Partial<PagedRequest>) => apiClient.get<PagedResponse<Tenant>>("/tenants", withPagedDefaults(params)),
  getById: (id: string) => apiClient.get<Tenant>(`/tenants/${id}`),
  create: (data: ProvisionTenantRequest) => apiClient.post<ProvisionTenantResult>("/tenants", data),
  update: (id: string, data: Partial<Tenant>) => apiClient.put<Tenant>(`/tenants/${id}`, data),
  deactivate: (id: string) => apiClient.delete(`/tenants/${id}`),
  activate: (id: string) => apiClient.post(`/tenants/${id}/activate`, {}),
  listDevices: (id: string) => apiClient.get<TenantEnrolledDevice[]>(`/tenants/${id}/devices`),
  deactivateDevice: (id: string, deviceId: string) => apiClient.delete(`/tenants/${id}/devices/${deviceId}`),
};
