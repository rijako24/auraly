"use client";

import { useQuery } from "@tanstack/react-query";
import { conversationsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";

export const conversationKeys = {
  all: ["conversations"] as const,
  lists: () => [...conversationKeys.all, "list"] as const,
  list: (
    businessId: string | null,
    params?: Partial<PagedRequest> & { userNumber?: string }
  ) => [...conversationKeys.lists(), businessId, params] as const,
  details: () => [...conversationKeys.all, "detail"] as const,
  detail: (id: string) => [...conversationKeys.details(), id] as const,
  messages: (id: string) =>
    [...conversationKeys.detail(id), "messages"] as const,
};

export function useConversations(
  params?: Partial<PagedRequest> & { userNumber?: string }
) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: conversationKeys.list(businessId, params),
    queryFn: () =>
      conversationsApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
  });
}

export function useConversation(id: string) {
  return useQuery({
    queryKey: conversationKeys.detail(id),
    queryFn: () => conversationsApi.getById(id),
    enabled: !!id,
  });
}

export function useConversationWithMessages(id: string | null) {
  return useQuery({
    queryKey: conversationKeys.messages(id ?? ""),
    queryFn: () => conversationsApi.getByIdWithMessages(id!),
    enabled: !!id,
  });
}

