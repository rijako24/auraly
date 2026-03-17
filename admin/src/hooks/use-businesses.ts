"use client";

import { useQuery } from "@tanstack/react-query";
import { businessesApi } from "@/services/api";
import type { PagedRequest } from "@/types/api";

export const businessKeys = {
  all: ["businesses"] as const,
  lists: () => [...businessKeys.all, "list"] as const,
  list: (params?: Partial<PagedRequest>) => [...businessKeys.lists(), params] as const,
  details: () => [...businessKeys.all, "detail"] as const,
  detail: (id: string) => [...businessKeys.details(), id] as const,
};

export function useBusinesses(params?: Partial<PagedRequest>) {
  return useQuery({
    queryKey: businessKeys.list(params),
    queryFn: () => businessesApi.list(params),
  });
}

export function useBusiness(id: string) {
  return useQuery({
    queryKey: businessKeys.detail(id),
    queryFn: () => businessesApi.getById(id),
    enabled: !!id,
  });
}
