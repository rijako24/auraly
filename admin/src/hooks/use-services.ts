"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { servicesApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";

export const serviceKeys = {
  all: ["services"] as const,
  lists: () => [...serviceKeys.all, "list"] as const,
  list: (businessId: string | null, params?: Partial<PagedRequest>) =>
    [...serviceKeys.lists(), businessId, params] as const,
  details: () => [...serviceKeys.all, "detail"] as const,
  detail: (id: string) => [...serviceKeys.details(), id] as const,
  categories: (businessId: string | null, params?: Partial<PagedRequest>) =>
    [...serviceKeys.all, "categories", businessId, params] as const,
};

export function useServices(params?: Partial<PagedRequest>) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: serviceKeys.list(businessId, params),
    queryFn: () =>
      servicesApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
  });
}

export function useService(id: string) {
  return useQuery({
    queryKey: serviceKeys.detail(id),
    queryFn: () => servicesApi.getById(id),
    enabled: !!id,
  });
}

export function useServiceCategories(params?: Partial<PagedRequest> & { businessId?: string }) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: serviceKeys.categories(businessId, params),
    queryFn: () => servicesApi.listCategories({ ...params, businessId: businessId! }),
    enabled: !!businessId,
  });
}

export function useCreateService() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useMutation({
    mutationFn: servicesApi.create,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: serviceKeys.lists() });
      if (businessId) {
        queryClient.invalidateQueries({ queryKey: serviceKeys.list(businessId) });
      }
    },
  });
}
