import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { Conversation, Message } from "@/types/entities";

export const conversationsApi = {
  list: (
    params?: Partial<PagedRequest> & {
      businessId?: string;
      userNumber?: string;
    }
  ) =>
    apiClient.get<PagedResponse<Conversation>>(
      "/conversations",
      params as Record<string, string | number | undefined>
    ),
  getById: (id: string) =>
    apiClient.get<Conversation>(`/conversations/${id}`),
  /** Conversación + primer página grande de mensajes (API no soporta include=messages). */
  getByIdWithMessages: async (id: string) => {
    const [conversation, messagesPage] = await Promise.all([
      apiClient.get<Conversation>(`/conversations/${id}`),
      apiClient.get<PagedResponse<Message>>(
        `/conversations/${id}/messages`,
        { page: 1, pageSize: 500 } as Record<string, string | number | undefined>
      ),
    ]);
    return { ...conversation, messages: messagesPage.items };
  },
  listMessages: (
    conversationId: string,
    params?: Partial<PagedRequest>
  ) =>
    apiClient.get<PagedResponse<Message>>(
      `/conversations/${conversationId}/messages`,
      params as Record<string, string | number | undefined>
    ),
};
