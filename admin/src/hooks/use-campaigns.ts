"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { campaignsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";
import type { CreateCampaignRequest } from "@/types/entities";

export const campaignKeys = {
  all: ["campaigns"] as const,
  lists: () => [...campaignKeys.all, "list"] as const,
  list: (businessId: string | null, params?: Partial<PagedRequest>) =>
    [...campaignKeys.lists(), businessId, params] as const,
  details: () => [...campaignKeys.all, "detail"] as const,
  detail: (id: string) => [...campaignKeys.details(), id] as const,
};

export function useCampaigns(params?: Partial<PagedRequest>) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: campaignKeys.list(businessId, params),
    queryFn: () => campaignsApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
    placeholderData: keepPreviousData,
  });
}

export function useCampaign(id: string) {
  return useQuery({
    queryKey: campaignKeys.detail(id),
    queryFn: () => campaignsApi.getById(id),
    enabled: !!id,
  });
}

export function useCreateCampaign() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateCampaignRequest) => campaignsApi.create(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: campaignKeys.lists() });
    },
  });
}
