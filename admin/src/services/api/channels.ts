import { apiClient } from "./client";

export interface WhatsAppChannel {
  businessWhatsAppNumberId: string;
  businessId: string;
  agentId: string;
  agentName: string;
  phoneNumber: string;
  whatsAppPhoneNumberId: string;
  whatsAppBusinessAccountId: string;
  hasAccessToken: boolean;
  isActive: boolean;
  createdAt: string;
}

export interface WhatsAppChannelPayload {
  agentId: string;
  phoneNumber: string;
  whatsAppPhoneNumberId: string;
  whatsAppBusinessAccountId: string;
  accessToken?: string | null;
  isActive: boolean;
}

export interface WhatsAppConnectionStatus {
  isConnected: boolean;
  status: "connected" | "error";
  message: string;
  verifiedName?: string | null;
  displayPhoneNumber?: string | null;
  qualityRating?: string | null;
  businessAccountName?: string | null;
  checkedAtUtc: string;
}

export const channelsApi = {
  list: (businessId: string) => apiClient.get<WhatsAppChannel[]>(`/businesses/${businessId}/channels`),
  createWhatsApp: (businessId: string, data: WhatsAppChannelPayload) =>
    apiClient.post<WhatsAppChannel>(`/businesses/${businessId}/channels/whatsapp`, data),
  updateWhatsApp: (businessId: string, channelId: string, data: WhatsAppChannelPayload) =>
    apiClient.put<WhatsAppChannel>(`/businesses/${businessId}/channels/whatsapp/${channelId}`, data),
  deactivateWhatsApp: (businessId: string, channelId: string) =>
    apiClient.delete(`/businesses/${businessId}/channels/whatsapp/${channelId}`),
  validateWhatsApp: (businessId: string, channelId: string) =>
    apiClient.post<WhatsAppConnectionStatus>(`/businesses/${businessId}/channels/whatsapp/${channelId}/validate`),
};
