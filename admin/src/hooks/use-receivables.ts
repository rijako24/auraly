"use client";

import { useQuery } from "@tanstack/react-query";
import {
  receivablesApi,
  type ReceivableStatus,
} from "@/services/api/receivables";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useBusinessDates } from "@/hooks/use-business-dates";

export function useReceivables(params: {
  page: number;
  pageSize: number;
  search?: string;
  customerId?: string;
  partySiteId?: string;
  status?: ReceivableStatus;
  overdue?: boolean;
  from?: string;
  to?: string;
  enabled?: boolean;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const { timeZone } = useBusinessDates(businessId);
  return useQuery({
    queryKey: ["receivables", businessId, params, timeZone],
    queryFn: () => receivablesApi.list(params),
    enabled: !!businessId && params.enabled !== false,
    staleTime: 0,
    gcTime: 0,
  });
}

export function useReceivableDetail(receivableId?: string) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["receivable", businessId, receivableId],
    queryFn: () => receivablesApi.get(receivableId!),
    enabled: !!businessId && !!receivableId,
    staleTime: 0,
    gcTime: 0,
  });
}
