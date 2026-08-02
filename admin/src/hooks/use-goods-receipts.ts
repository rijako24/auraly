"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  goodsReceiptsApi, type GoodsReceiptStatus, type SaveGoodsReceiptDraftRequest,
} from "@/services/api/goods-receipts";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useGoodsReceiptOptions() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["goods-receipt-options", businessId],
    queryFn: goodsReceiptsApi.options,
    enabled: !!businessId,
  });
}

export function useGoodsReceipts(params: {
  page: number; pageSize: number; search?: string; status?: GoodsReceiptStatus;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["goods-receipts", businessId, params],
    queryFn: () => goodsReceiptsApi.list(params),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useGoodsReceiptProducts(supplierId?: string, search?: string) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["goods-receipt-products", businessId, supplierId, search],
    queryFn: () => goodsReceiptsApi.products(supplierId!, search?.trim() || undefined),
    enabled: !!businessId && !!supplierId,
    placeholderData: keepPreviousData,
  });
}

export function useSaveGoodsReceiptDraft() {
  const client = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useMutation({
    mutationFn: (request: SaveGoodsReceiptDraftRequest) => goodsReceiptsApi.saveDraft(request),
    onSuccess: (draft) => {
      client.setQueryData(["goods-receipt-draft", businessId, draft.draftId], draft);
      client.invalidateQueries({ queryKey: ["goods-receipts", businessId] });
    },
  });
}

export function useDeleteGoodsReceiptDraft() {
  const client = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useMutation({
    mutationFn: ({ draftId, concurrencyToken }: { draftId: string; concurrencyToken: string }) =>
      goodsReceiptsApi.deleteDraft(draftId, concurrencyToken),
    onSuccess: () => client.invalidateQueries({ queryKey: ["goods-receipts", businessId] }),
  });
}

export function useConfirmGoodsReceipt() {
  const client = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useMutation({
    mutationFn: goodsReceiptsApi.confirm,
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["goods-receipts", businessId] });
      client.invalidateQueries({ queryKey: ["payables", businessId] });
      client.invalidateQueries({ queryKey: ["pricing", businessId] });
    },
  });
}
