"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  payablesApi,
  type ConfirmSupplierPaymentRequest,
  type PayableStatus,
} from "@/services/api/payables";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function usePayables(params: {
  page: number;
  pageSize: number;
  search?: string;
  status?: PayableStatus;
  overdue?: boolean;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["payables", businessId, params],
    queryFn: () => payablesApi.list(params),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function usePayableDetail(payableId?: string) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["payable", businessId, payableId],
    queryFn: () => payablesApi.get(payableId!),
    enabled: !!businessId && !!payableId,
  });
}

export function useConfirmSupplierPayment() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: ConfirmSupplierPaymentRequest) =>
      payablesApi.confirmPayment(request, `payable-payment-${request.paymentId}`),
    onSuccess: (_, request) => {
      queryClient.invalidateQueries({ queryKey: ["payables", businessId] });
      request.allocations.forEach((allocation) =>
        queryClient.invalidateQueries({
          queryKey: ["payable", businessId, allocation.payableId],
        }),
      );
    },
  });
}
