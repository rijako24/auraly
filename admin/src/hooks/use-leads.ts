"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { leadsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";

export const leadKeys = {
  all: ["leads"] as const,
  lists: () => [...leadKeys.all, "list"] as const,
  list: (
    businessId: string | null,
    params?: Partial<PagedRequest> & { status?: string }
  ) => [...leadKeys.lists(), businessId, params] as const,
  details: () => [...leadKeys.all, "detail"] as const,
  detail: (id: string) => [...leadKeys.details(), id] as const,
};

export function useLeads(
  params?: Partial<PagedRequest> & { status?: string }
) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: leadKeys.list(businessId, params),
    queryFn: () =>
      leadsApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useLead(id: string) {
  return useQuery({
    queryKey: leadKeys.detail(id),
    queryFn: () => leadsApi.getById(id),
    enabled: !!id,
  });
}
