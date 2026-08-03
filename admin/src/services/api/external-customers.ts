import { apiClient } from "./client";

export type ExternalCustomerReconciliationStatus = "Pending" | "Linked" | "Conflict";

export interface ExternalCustomerReconciliationItem {
  externalCommerceCustomerId: string;
  integrationConnectionId: string;
  integrationName: string;
  externalAccountId: string;
  externalCustomerId: string;
  name: string | null;
  phone: string;
  phoneNormalized: string;
  status: ExternalCustomerReconciliationStatus;
  error: string | null;
  partyId: string | null;
  customerId: string | null;
  lastSyncedAt: string;
  reconciledAt: string | null;
}

export interface ExternalCustomerReconciliationPage {
  items: ExternalCustomerReconciliationItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ExternalCustomerReconciliationResult {
  externalCommerceCustomerId: string;
  status: ExternalCustomerReconciliationStatus;
  partyId: string | null;
  customerId: string | null;
  error: string | null;
  idempotentReplay: boolean;
}

export interface ReconcilePendingExternalCustomersResult {
  requested: number;
  linked: number;
  conflicts: number;
  alreadyLinked: number;
}

export const externalCustomersApi = {
  page: (params: {
    page: number;
    pageSize: number;
    search?: string;
    status?: string;
    integrationConnectionId?: string;
  }) => apiClient.get<ExternalCustomerReconciliationPage>(
    "/commerce/v1/external-customers",
    params,
  ),
  reconcile: (externalCommerceCustomerId: string) =>
    apiClient.post<ExternalCustomerReconciliationResult>(
      `/commerce/v1/external-customers/${externalCommerceCustomerId}/reconcile`,
      {},
    ),
  reconcilePending: (maximumItems = 50) =>
    apiClient.post<ReconcilePendingExternalCustomersResult>(
      "/commerce/v1/external-customers/reconcile-pending",
      { maximumItems },
    ),
};
