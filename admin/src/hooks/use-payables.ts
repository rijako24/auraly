"use client";

import { useQuery } from "@tanstack/react-query";
import {
  payablesApi,
  type PayableStatus,
} from "@/services/api/payables";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function usePayables(params: {
  page: number;
  pageSize: number;
  search?: string;
  supplierId?: string;
  conceptId?: string;
  status?: PayableStatus;
  overdue?: boolean;
  from?: string;
  to?: string;
  enabled?: boolean;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["payables", businessId, params],
    queryFn: () => payablesApi.list(params),
    enabled: !!businessId && params.enabled !== false,
    staleTime: 0,
    gcTime: 0,
  });
}

export function usePayableDetail(payableId?: string) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["payable", businessId, payableId],
    queryFn: () => payablesApi.get(payableId!),
    enabled: !!businessId && !!payableId,
    staleTime: 0,
    gcTime: 0,
  });
}
