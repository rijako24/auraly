"use client";

import { useQuery } from "@tanstack/react-query";
import { tenantsApi } from "@/services/api";
import { tenantCommercialApi } from "@/services/api/tenants";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import type { PagedRequest } from "@/types/api";

export const tenantKeys = {
  all: ["tenants"] as const,
  lists: () => [...tenantKeys.all, "list"] as const,
  list: (params?: Partial<PagedRequest>) => [...tenantKeys.lists(), params] as const,
  details: () => [...tenantKeys.all, "detail"] as const,
  detail: (id: string) => [...tenantKeys.details(), id] as const,
};

export const tenantCommercialSubscriptionKey = (tenantId: string | null) => ["tenant-commercial", "subscription", tenantId] as const;

export function useTenantCommercialSubscription(enabled = true) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  return useQuery({
    queryKey: tenantCommercialSubscriptionKey(tenantId),
    queryFn: tenantCommercialApi.subscription,
    enabled: enabled && !!tenantId,
  });
}

export function useTenants(params?: Partial<PagedRequest>) {
  return useQuery({
    queryKey: tenantKeys.list(params),
    queryFn: () => tenantsApi.list(params),
  });
}

export function useTenant(id: string) {
  return useQuery({
    queryKey: tenantKeys.detail(id),
    queryFn: () => tenantsApi.getById(id),
    enabled: !!id,
  });
}
