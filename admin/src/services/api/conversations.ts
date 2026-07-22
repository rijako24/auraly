import { apiClient, withPagedDefaults } from "./client";
import type { PagedResponse, PagedRequest } from "@/types/api";
import type { Conversation, ConversationOwner, Message } from "@/types/entities";

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
      agentId?: string;
    }
  ) =>
    apiClient.get<PagedResponse<Conversation>>(
      "/conversations",
      withPagedDefaults(params)
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
      withPagedDefaults(params)
    ),
  sendWebMessage: (conversationId: string, message: string) =>
    apiClient.post<WebConversationMessageResponse>(
      `/conversations/${conversationId}/messages/web`,
      { message }
    ),
  updateOwner: (conversationId: string, owner: ConversationOwner) =>
    apiClient.patch<Conversation>(`/conversations/${conversationId}/owner`, { owner }),
};
