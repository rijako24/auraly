"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { ordersApi } from "@/services/api";
import type { OrderFilters } from "@/services/api/orders";
import { useBusinessContextStore } from "@/stores/business-context-store";

export const orderKeys = {
  all: ["orders"] as const,
  lists: () => [...orderKeys.all, "list"] as const,
  list: (params?: OrderFilters) => [...orderKeys.lists(), params] as const,
  summaries: () => [...orderKeys.all, "summary"] as const,
  summary: (params?: OrderFilters) => [...orderKeys.summaries(), params] as const,
  details: () => [...orderKeys.all, "detail"] as const,
  detail: (id: string) => [...orderKeys.details(), id] as const,
};

export function useOrders(params?: Omit<OrderFilters, "businessId">) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: orderKeys.list({ ...params, businessId: businessId ?? undefined }),
    queryFn: () => ordersApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useOrderSummary(params?: Omit<OrderFilters, "businessId" | "page" | "pageSize">) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: orderKeys.summary({ ...params, businessId: businessId ?? undefined }),
    queryFn: () => ordersApi.summary({ ...params, businessId: businessId! }),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useOrder(id: string) {
  return useQuery({
    queryKey: orderKeys.detail(id),
    queryFn: () => ordersApi.getById(id),
    enabled: !!id,
  });
}
