"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { salesReturnsApi, type ConfirmSalesReturnRequest } from "@/services/api/sales-returns";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useReturnableSales(params: {
  page: number;
  pageSize: number;
  search?: string;
  customer?: string;
  from?: string;
  to?: string;
  withAvailableQuantity?: boolean;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["returnable-sales", businessId, params],
    queryFn: () => salesReturnsApi.listSales(params),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useConfirmSalesReturn() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: ConfirmSalesReturnRequest) => salesReturnsApi.confirm(request),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["returnable-sales", businessId] });
      client.invalidateQueries({ queryKey: ["sales-returns", businessId] });
      client.invalidateQueries({ queryKey: ["receivables", businessId] });
    },
  });
}
