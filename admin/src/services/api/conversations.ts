import { apiClient } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { Conversation, Message } from "@/types/entities";

export interface WebConversationMessageResponse {
  response: string;
  escalatedToHuman: boolean;
  reservationCreated: boolean;
}

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
    Promise.all([
      conversationsApi.getById(id),
      conversationsApi.listMessages(id, { page: 1, pageSize: 200 }),
    ]).then(([conversation, messages]) => ({
      ...conversation,
      messages: messages.items,
    })),
  listMessages: (
    conversationId: string,
    params?: Partial<PagedRequest>
  ) =>
    apiClient.get<PagedResponse<Message>>(
      `/conversations/${conversationId}/messages`,
      params as Record<string, string | number | undefined>
    ),
  sendWebMessage: (conversationId: string, message: string) =>
    apiClient.post<WebConversationMessageResponse>(
      `/conversations/${conversationId}/messages/web`,
      { message }
    ),
};
