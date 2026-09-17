"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { salesReturnsApi, type ConfirmSalesReturnRequest } from "@/services/api/sales-returns";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { resolveSalesReturnBusinessId } from "@/lib/sales-return-business-context";
import type { PosClient, PosSalesReturnContext } from "@/services/pos/pos-edge-client";

export type PosSalesReturnRuntime = {
  client: PosClient;
  context: PosSalesReturnContext;
};

export function useReturnableSales(params: {
  page: number;
  pageSize: number;
  search?: string;
  customer?: string;
  from?: string;
  to?: string;
  withAvailableQuantity?: boolean;
}, businessIdOverride?: string | null, runtime?: PosSalesReturnRuntime) {
  const selectedBusinessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const businessId = resolveSalesReturnBusinessId(businessIdOverride, selectedBusinessId);
  return useQuery({
    queryKey: ["returnable-sales", runtime?.client.mode ?? "dashboard", businessId, params],
    queryFn: () => runtime
      ? runtime.client.searchServerReturnableSales(runtime.context, params)
      : salesReturnsApi.listSales({ ...params, businessId: businessId! }),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useConfirmSalesReturn(
  businessIdOverride?: string | null,
  runtime?: PosSalesReturnRuntime,
) {
  const selectedBusinessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const businessId = resolveSalesReturnBusinessId(businessIdOverride, selectedBusinessId);
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: ConfirmSalesReturnRequest) => runtime
      ? runtime.client.confirmServerSalesReturn({
          ...request,
          businessId: runtime.context.businessId,
          workSessionId: runtime.context.workSessionId,
        })
      : salesReturnsApi.confirm(request),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["returnable-sales"] });
      client.invalidateQueries({ queryKey: ["sales-returns", businessId] });
      client.invalidateQueries({ queryKey: ["receivables", businessId] });
    },
  });
}
