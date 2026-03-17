import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { AuditLog } from "@/types/entities";

export interface AuditLogFilters extends Partial<PagedRequest> {
  userId?: string;
  tenantId?: string;
  businessId?: string;
  entityType?: string;
  entityId?: string;
  action?: string;
  fromDate?: string;
  toDate?: string;
}

export const auditLogsApi = {
  list: (params?: AuditLogFilters) =>
    apiClient.get<PagedResponse<AuditLog>>(
      "/audit-logs",
      params as Record<string, string | number | undefined>
    ),
  getById: (id: string) => apiClient.get<AuditLog>(`/audit-logs/${id}`),
};
