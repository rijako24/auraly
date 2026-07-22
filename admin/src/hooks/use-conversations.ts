"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { conversationsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";

export const conversationKeys = {
  all: ["conversations"] as const,
  lists: () => [...conversationKeys.all, "list"] as const,
  list: (
    businessId: string | null,
    params?: Partial<PagedRequest> & {
      userNumber?: string;
      state?: number;
      agentId?: string;
    }
  ) => [...conversationKeys.lists(), businessId, params] as const,
  details: () => [...conversationKeys.all, "detail"] as const,
  detail: (id: string) => [...conversationKeys.details(), id] as const,
  messages: (id: string) =>
    [...conversationKeys.detail(id), "messages"] as const,
};

export function useConversations(
  params?: Partial<PagedRequest> & {
    userNumber?: string;
    agentId?: string;
    state?: number;
  }
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

export function useUpdateConversationOwner() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useMutation({
    mutationFn: ({ conversationId, owner }: { conversationId: string; owner: "Bot" | "Human" }) =>
      conversationsApi.updateOwner(conversationId, owner),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: conversationKeys.detail(variables.conversationId) });
      queryClient.invalidateQueries({ queryKey: conversationKeys.messages(variables.conversationId) });
      queryClient.invalidateQueries({ queryKey: conversationKeys.lists() });
      if (businessId) {
        queryClient.invalidateQueries({ queryKey: conversationKeys.list(businessId) });
      }
    },
  });
}

export function useSendWebConversationMessage() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useMutation({
    mutationFn: ({ conversationId, message }: { conversationId: string; message: string }) =>
      conversationsApi.sendWebMessage(conversationId, message),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: conversationKeys.messages(variables.conversationId) });
      queryClient.invalidateQueries({ queryKey: conversationKeys.detail(variables.conversationId) });
      queryClient.invalidateQueries({ queryKey: conversationKeys.lists() });
      if (businessId) {
        queryClient.invalidateQueries({ queryKey: conversationKeys.list(businessId) });
      }
    },
  });
}
