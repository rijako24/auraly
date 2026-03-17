import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { Conversation, Message } from "@/types/entities";

export const conversationsApi = {
  list: (
    params?: Partial<PagedRequest> & {
      businessId?: string;
      userNumber?: string;
      state?: number;
    }
  ) =>
    apiClient.get<PagedResponse<Conversation>>(
      "/conversations",
      params as Record<string, string | number | undefined>
    ),
  getById: (id: string) =>
    apiClient.get<Conversation & { messages?: Message[] }>(
      `/conversations/${id}`
    ),
  getByIdWithMessages: (id: string) =>
    apiClient.get<Conversation & { messages: Message[] }>(
      `/conversations/${id}?include=messages`
    ),
  listMessages: (
    conversationId: string,
    params?: Partial<PagedRequest>
  ) =>
    apiClient.get<PagedResponse<Message>>(
      `/conversations/${conversationId}/messages`,
      params as Record<string, string | number | undefined>
    ),
};
