"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  pricingApi,
  type PriceCalculationRequest,
  type PriceProposalStatus,
  type PublishPricesRequest,
  type ReviewPriceProposalRequest,
} from "@/services/api/pricing";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function usePriceProposals(params: {
  page: number;
  pageSize: number;
  search?: string;
  status?: PriceProposalStatus | "Pending";
  supplierId?: string;
  sourceDocumentId?: string;
}) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["pricing-proposals", businessId, params],
    queryFn: () => pricingApi.list(params),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

function usePricingMutation<T, TResult>(mutationFn: (value: T) => Promise<TResult>) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const client = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["pricing-proposals", businessId] });
      client.invalidateQueries({ queryKey: ["products", businessId] });
    },
  });
}

export const useCalculatePrice = () =>
  useMutation({ mutationFn: (request: PriceCalculationRequest) => pricingApi.calculate(request) });

export const useReviewPrice = () =>
  useMutation({ mutationFn: ({ proposalId, request }: {
    proposalId: string;
    request: ReviewPriceProposalRequest;
  }) => pricingApi.review(proposalId, request) });

export const useRejectPrice = () =>
  usePricingMutation((value: { proposalId: string; concurrencyToken: string; reason?: string }) =>
    pricingApi.reject(value.proposalId, value.concurrencyToken, value.reason));

export const usePublishPrices = () =>
  usePricingMutation((request: PublishPricesRequest) => pricingApi.publish(request));
export function useProductPricingContext(productId?: string) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["product-pricing", businessId, productId],
    queryFn: () => pricingApi.getProductContext(productId!),
    enabled: !!businessId && !!productId,
  });
}

export function useSavePreparedProductPrice() {
  return usePricingMutation(({ productId, request }: {
    productId: string;
    request: import("@/services/api/pricing").PublishProductPriceRequest;
  }) => pricingApi.savePreparedProduct(productId, request));
}

export function useProductPriceHistory(productId?: string, enabled = true, page = 1, pageSize = 5, activityType?:"Preparation"|"Publication") {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["product-price-history", businessId, productId, page, pageSize, activityType],
    queryFn: () => pricingApi.getProductHistory(productId!, page, pageSize, activityType),
    enabled: enabled && !!businessId && !!productId,
  });
}
