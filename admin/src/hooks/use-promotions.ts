"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { promotionsApi, type PromotionPayload } from "@/services/api/promotions";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";

export const promotionKeys = {
  all: ["promotions"] as const,
  lists: () => [...promotionKeys.all, "list"] as const,
  list: (businessId: string | null, params?: Partial<PagedRequest>) =>
    [...promotionKeys.lists(), businessId, params] as const,
};

export function usePromotions(params?: Partial<PagedRequest>) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: promotionKeys.list(businessId, params),
    queryFn: () => promotionsApi.list(businessId!, params),
    enabled: !!businessId,
  });
}

export function useCreatePromotion() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useMutation({
    mutationFn: (payload: PromotionPayload) => promotionsApi.create(businessId!, payload),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: promotionKeys.lists() }),
  });
}

export function useDeletePromotion() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useMutation({
    mutationFn: (promotionId: string) => promotionsApi.delete(businessId!, promotionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: promotionKeys.lists() }),
  });
}
