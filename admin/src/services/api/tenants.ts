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
  administratorIdentificationType: string; administratorIdentification: string;
  administratorFirstName: string; administratorLastName: string;
  administratorEmail: string; administratorPhone: string;
}

export interface ProvisionTenantResult {
  provisioningRequestId: string; tenantId: string; tenantKey: string; businessId: string;
  salesWarehouseId: string; ordersWarehouseId: string; defaultCustomerId: string;
  administratorUserId: string; status: string;
}

export const tenantsApi = {
  list: (params?: Partial<PagedRequest>) => apiClient.get<PagedResponse<Tenant>>("/tenants", withPagedDefaults(params)),
  getById: (id: string) => apiClient.get<Tenant>(`/tenants/${id}`),
  create: (data: ProvisionTenantRequest) => apiClient.post<ProvisionTenantResult>("/tenants", data),
  update: (id: string, data: Partial<Tenant>) => apiClient.put<Tenant>(`/tenants/${id}`, data),
  delete: (id: string) => apiClient.delete(`/tenants/${id}`),
};
