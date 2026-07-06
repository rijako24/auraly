import { apiClient } from "./client";
import type { WhatsAppTemplate } from "@/types/entities";

export const whatsAppTemplatesApi = {
  list: (params: { businessId: string; approvedOnly?: boolean }) =>
    apiClient.get<WhatsAppTemplate[]>("/whatsapp-templates", {
      businessId: params.businessId,
      approvedOnly: params.approvedOnly ?? true,
    }),
};
