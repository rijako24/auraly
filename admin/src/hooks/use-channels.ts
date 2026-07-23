"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { channelsApi, type WhatsAppChannelPayload } from "@/services/api/channels";
import { useBusinessContextStore } from "@/stores/business-context-store";

export const channelKeys = {
  all: ["channels"] as const,
  list: (businessId: string | null) => [...channelKeys.all, businessId] as const,
};

export function useChannels() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({ queryKey: channelKeys.list(businessId), queryFn: () => channelsApi.list(businessId!), enabled: !!businessId });
}

export function useCreateWhatsAppChannel() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const client = useQueryClient();
  return useMutation({
    mutationFn: (payload: WhatsAppChannelPayload) => channelsApi.createWhatsApp(businessId!, payload),
    onSuccess: () => client.invalidateQueries({ queryKey: channelKeys.list(businessId) }),
  });
}

export function useUpdateWhatsAppChannel() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ channelId, payload }: { channelId: string; payload: WhatsAppChannelPayload }) => channelsApi.updateWhatsApp(businessId!, channelId, payload),
    onSuccess: () => client.invalidateQueries({ queryKey: channelKeys.list(businessId) }),
  });
}

export function useDeactivateWhatsAppChannel() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const client = useQueryClient();
  return useMutation({
    mutationFn: (channelId: string) => channelsApi.deactivateWhatsApp(businessId!, channelId),
    onSuccess: () => client.invalidateQueries({ queryKey: channelKeys.list(businessId) }),
  });
}

export function useValidateWhatsAppChannel() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useMutation({ mutationFn: (channelId: string) => channelsApi.validateWhatsApp(businessId!, channelId) });
}
