"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { paymentsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";

export const paymentKeys = {
  all: ["payments"] as const,
  lists: () => [...paymentKeys.all, "list"] as const,
  list: (params?: Partial<PagedRequest> & { businessId?: string; status?: string }) =>
    [...paymentKeys.lists(), params] as const,
  details: () => [...paymentKeys.all, "detail"] as const,
  detail: (id: string) => [...paymentKeys.details(), id] as const,
};

export function usePayments(params?: Partial<PagedRequest> & { status?: string }) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: paymentKeys.list({ ...params, businessId: businessId ?? undefined }),
    queryFn: () => paymentsApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function usePayment(id: string) {
  return useQuery({
    queryKey: paymentKeys.detail(id),
    queryFn: () => paymentsApi.getById(id),
    enabled: !!id,
  });
}
export function useConfirmManualPayment() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => paymentsApi.confirmManual(id),
    onSuccess: (payment) => {
      queryClient.invalidateQueries({ queryKey: paymentKeys.lists() });
      queryClient.setQueryData(paymentKeys.detail(payment.paymentTransactionId), payment);
    },
  });
}