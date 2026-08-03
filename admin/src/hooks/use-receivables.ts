"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  receivablesApi,
  type ConfirmCustomerPaymentRequest,
  type ReceivableStatus,
} from "@/services/api/receivables";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useReceivables(params: {
  page: number;
  pageSize: number;
  search?: string;
  status?: ReceivableStatus;
  overdue?: boolean;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["receivables", businessId, params],
    queryFn: () => receivablesApi.list(params),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useReceivableDetail(receivableId?: string) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["receivable", businessId, receivableId],
    queryFn: () => receivablesApi.get(receivableId!),
    enabled: !!businessId && !!receivableId,
  });
}

export function useConfirmCustomerPayment() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: ConfirmCustomerPaymentRequest) =>
      receivablesApi.confirmPayment(request, `receivable-payment-${request.paymentId}`),
    onSuccess: (_, request) => {
      client.invalidateQueries({ queryKey: ["receivables", businessId] });
      request.allocations.forEach(({ receivableId }) =>
        client.invalidateQueries({ queryKey: ["receivable", businessId, receivableId] }),
      );
    },
  });
}
