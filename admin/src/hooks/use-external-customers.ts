"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { externalCustomersApi } from "@/services/api/external-customers";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useExternalCustomers(params: {
  page: number;
  pageSize: number;
  search?: string;
  status?: string;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["external-customers", businessId, params],
    queryFn: () => externalCustomersApi.page(params),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useReconcileExternalCustomer() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useMutation({
    mutationFn: externalCustomersApi.reconcile,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["external-customers", businessId] }),
        queryClient.invalidateQueries({ queryKey: ["parties", businessId] }),
      ]);
    },
  });
}

export function useReconcilePendingExternalCustomers() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useMutation({
    mutationFn: externalCustomersApi.reconcilePending,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["external-customers", businessId] }),
        queryClient.invalidateQueries({ queryKey: ["parties", businessId] }),
      ]);
    },
  });
}
