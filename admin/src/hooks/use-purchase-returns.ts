"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { purchaseReturnsApi, type ConfirmPurchaseReturnRequest } from "@/services/api/purchase-returns";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useTenantContextStore } from "@/stores/tenant-context-store";

export function useReturnableReceipts(params: { search?: string; from?: string; to?: string; withAvailableQuantity?: boolean; page: number; pageSize: number }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const tenantId = useTenantContextStore((state) => state.selectedTenantId);
  return useQuery({
    queryKey: ["purchase-return-receipts", tenantId, businessId, params],
    queryFn: () => purchaseReturnsApi.listReceipts(params),
    enabled: !!tenantId && !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useConfirmPurchaseReturn() {
  const client = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const tenantId = useTenantContextStore((state) => state.selectedTenantId);
  return useMutation({
    mutationFn: (request: ConfirmPurchaseReturnRequest) => purchaseReturnsApi.confirm(request),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["purchase-return-receipts", tenantId, businessId] });
      client.invalidateQueries({ queryKey: ["goods-receipts", businessId] });
      client.invalidateQueries({ queryKey: ["payables", businessId] });
      client.invalidateQueries({ queryKey: ["inventory", businessId] });
    },
  });
}
