"use client";

import { useQuery } from "@tanstack/react-query";
import { auditLogsApi } from "@/services/api";
import type { AuditLogFilters } from "@/services/api/audit-logs";

export const auditLogKeys = {
  all: ["audit-logs"] as const,
  lists: () => [...auditLogKeys.all, "list"] as const,
  list: (params?: AuditLogFilters) => [...auditLogKeys.lists(), params] as const,
  details: () => [...auditLogKeys.all, "detail"] as const,
  detail: (id: string) => [...auditLogKeys.details(), id] as const,
};

export function useAuditLogs(params?: AuditLogFilters) {
  return useQuery({
    queryKey: auditLogKeys.list(params),
    queryFn: () => auditLogsApi.list(params),
  });
}

export function useAuditLog(id: string) {
  return useQuery({
    queryKey: auditLogKeys.detail(id),
    queryFn: () => auditLogsApi.getById(id),
    enabled: !!id,
  });
}
