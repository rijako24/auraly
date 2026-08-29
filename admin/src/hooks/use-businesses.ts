"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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

export function useUpdateBusiness() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, values }: { id: string; values: Parameters<typeof businessesApi.update>[1] }) =>
      businessesApi.update(id, values),
    onSuccess: (business) => {
      queryClient.setQueryData(businessKeys.detail(business.businessId), business);
      void queryClient.invalidateQueries({ queryKey: businessKeys.lists() });
    },
  });
}

export function useCreateBusiness() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (values: Parameters<typeof businessesApi.create>[0]) => businessesApi.create(values),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: businessKeys.lists() }),
  });
}

export function useBusiness(id: string) {
  return useQuery({
    queryKey: businessKeys.detail(id),
    queryFn: () => businessesApi.getById(id),
    enabled: !!id,
  });
}
