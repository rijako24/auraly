"use client";

import { useQuery } from "@tanstack/react-query";
import { whatsAppTemplatesApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";

export const whatsAppTemplateKeys = {
  all: ["whatsapp-templates"] as const,
  list: (businessId: string | null) => [...whatsAppTemplateKeys.all, businessId] as const,
};

export function useWhatsAppTemplates() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: whatsAppTemplateKeys.list(businessId),
    queryFn: () => whatsAppTemplatesApi.list({ businessId: businessId!, approvedOnly: true }),
    enabled: !!businessId,
  });
}
