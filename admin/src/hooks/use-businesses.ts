"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { businessesApi } from "@/services/api";
import type { PagedRequest } from "@/types/api";
import type { PagedResponse } from "@/types/api";
import type { Business } from "@/types/entities";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import type { BusinessContextOption } from "@/stores/business-context-store";

export const businessKeys = {
  all: ["businesses"] as const,
  lists: () => [...businessKeys.all, "list"] as const,
  list: (params?: Partial<PagedRequest>, tenantId?: string | null) => [...businessKeys.lists(), tenantId, params] as const,
  details: () => [...businessKeys.all, "detail"] as const,
  detail: (id: string, tenantId?: string | null) => [...businessKeys.details(), tenantId, id] as const,
};

export function useBusinesses(params?: Partial<PagedRequest>, enabled = true) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  return useQuery({
    queryKey: businessKeys.list(params, tenantId),
    queryFn: () => businessesApi.list(params),
    enabled: enabled && Boolean(tenantId),
  });
}

export function useUpdateBusiness() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, values }: { id: string; values: Parameters<typeof businessesApi.update>[1] }) =>
      businessesApi.update(id, values),
    onSuccess: (business) => {
      const previous = queryClient.getQueryData<Business>(businessKeys.detail(business.businessId, business.tenantId));
      queryClient.setQueryData(businessKeys.detail(business.businessId, business.tenantId), business);
      const lists = [...businessKeys.lists(), business.tenantId];
      queryClient.setQueriesData<PagedResponse<Business>>({ queryKey: lists }, current => current && {
        ...current, items: current.items.map(item => item.businessId === business.businessId ? business : item),
      });
      queryClient.setQueriesData<BusinessContextOption[]>({ predicate: query => query.queryKey[0] === "execution-context" && query.queryKey[2] === "businesses" && query.queryKey[3] === business.tenantId }, current => current?.map(item => item.businessId === business.businessId ? { ...item, name: business.name, sharesProductPrices: business.sharesProductPrices } : item));
      // Name determines ordering and name/email determine server-side search.
      // A single row cannot determine membership in a changed SQL page.
      if (!previous || previous.name !== business.name || previous.email !== business.email)
        void queryClient.invalidateQueries({ queryKey: lists, predicate: query => !previous || previous.name !== business.name || Boolean((query.queryKey[3] as Partial<PagedRequest> | undefined)?.search) });
    },
  });
}

export function useCreateBusiness() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (values: Parameters<typeof businessesApi.create>[0]) => businessesApi.create(values),
    onSuccess: business => {
      queryClient.setQueryData(businessKeys.detail(business.businessId, business.tenantId), business);
      // The create response does not contain its place in the visible page.
      void queryClient.invalidateQueries({ queryKey: [...businessKeys.lists(), business.tenantId] });
      // Creation does not return the user's authorized workspace membership.
      void queryClient.invalidateQueries({ predicate: query => query.queryKey[0] === "execution-context" && query.queryKey[2] === "businesses" && query.queryKey[3] === business.tenantId });
    },
  });
}

export function useBusiness(id: string, initialBusiness?: Business) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  return useQuery({
    queryKey: businessKeys.detail(id, tenantId),
    queryFn: () => businessesApi.getById(id),
    enabled: Boolean(id && tenantId),
    initialData: initialBusiness?.tenantId === tenantId ? initialBusiness : undefined,
    staleTime: 60_000,
  });
}
