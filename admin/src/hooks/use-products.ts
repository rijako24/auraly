"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  productsApi,
  type PromoteProductAliasRequest,
  type ReviewProductAliasRequest,
  type UpdateProductRequest,
} from "@/services/api/products";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function useProducts(params?: { page?: number; pageSize?: number; search?: string; includeInactive?: boolean }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["products", businessId, params],
    queryFn: () => productsApi.list(businessId!, params),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useProductConfiguration(productId?: string) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  return useQuery({
    queryKey: ["product-configuration", businessId, productId],
    queryFn: () => productsApi.getConfiguration(businessId!, productId!),
    enabled: !!businessId && !!productId,
  });
}

export function useUpdateProduct() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, request }: { productId: string; request: UpdateProductRequest }) =>
      productsApi.update(businessId!, productId, request),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: ["products", businessId] });
      queryClient.invalidateQueries({ queryKey: ["product-configuration", businessId, variables.productId] });
    },
  });
}

export function useUpdateProductStatus() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, isActive }: { productId: string; isActive: boolean }) =>
      productsApi.updateStatus(businessId!, productId, isActive),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["products", businessId] }),
  });
}

export function useReviewProductAlias() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, productAliasId, request }: {
      productId: string;
      productAliasId: string;
      request: ReviewProductAliasRequest;
    }) => productsApi.reviewAlias(businessId!, productId, productAliasId, request),
    onSuccess: (_, variables) =>
      queryClient.invalidateQueries({ queryKey: ["product-configuration", businessId, variables.productId] }),
  });
}

export function usePromoteProductAlias() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ productId, productAliasId, request }: {
      productId: string;
      productAliasId: string;
      request: PromoteProductAliasRequest;
    }) => productsApi.promoteAlias(businessId!, productId, productAliasId, request),
    onSuccess: (_, variables) =>
      queryClient.invalidateQueries({ queryKey: ["product-configuration", businessId, variables.productId] }),
  });
}