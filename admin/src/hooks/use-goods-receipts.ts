"use client";

import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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

export function useGoodsReceiptProducts(
  supplierId?: string, search?: string, includeUnassociated = false,
) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const normalizedSearch = search?.trim() ?? "";
  return useInfiniteQuery({
    queryKey: ["goods-receipt-products", businessId, supplierId, normalizedSearch, includeUnassociated],
    queryFn: ({ pageParam }) => goodsReceiptsApi.products(
      supplierId!, normalizedSearch, includeUnassociated, pageParam, 50,
    ),
    initialPageParam: 1,
    getNextPageParam: (lastPage) =>
      lastPage.page < lastPage.totalPages ? lastPage.page + 1 : undefined,
    enabled: !!businessId && !!supplierId && normalizedSearch.length > 0,
  });
}

export function useAssociateGoodsReceiptProduct() {
  const client = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useMutation({
    mutationFn: goodsReceiptsApi.associateProduct,
    onSuccess: () => client.invalidateQueries({
      queryKey: ["goods-receipt-products", businessId],
    }),
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
