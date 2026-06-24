"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { agentsApi, type BusinessInboundContactPayload } from "@/services/api/agents";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { AgentSettings } from "@/types/agent-settings";

export const agentKeys = {
  all: ["agents"] as const,
  lists: () => [...agentKeys.all, "list"] as const,
  list: (businessId: string | null) => [...agentKeys.lists(), businessId] as const,
  inboundContacts: (businessId: string | null, includeInactive = false) => [...agentKeys.all, "inbound-contacts", businessId, includeInactive] as const,
  details: () => [...agentKeys.all, "detail"] as const,
  detail: (id: string) => [...agentKeys.details(), id] as const,
};

export function useAgents() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useQuery({
    queryKey: agentKeys.list(businessId),
    queryFn: () => agentsApi.listByBusiness(businessId!),
    enabled: !!businessId,
  });
}

export function useBusinessInboundContacts(options?: { includeInactive?: boolean }) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const includeInactive = options?.includeInactive ?? false;

  return useQuery({
    queryKey: agentKeys.inboundContacts(businessId, includeInactive),
    queryFn: () => agentsApi.listInboundContactsByBusiness(businessId!, includeInactive),
    enabled: !!businessId,
  });
}

export function useAgent(agentId: string) {
  return useQuery({
    queryKey: agentKeys.detail(agentId),
    queryFn: () => agentsApi.getById(agentId),
    enabled: !!agentId,
  });
}

export function useUpdateAgentSettings(agentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (settings: AgentSettings) =>
      agentsApi.updateSettings(agentId, settings),
    onSuccess: (data) => {
      queryClient.setQueryData(agentKeys.detail(agentId), data);
      queryClient.invalidateQueries({ queryKey: agentKeys.lists() });
    },
  });
}
export function useCreateBusinessInboundContact() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useMutation({
    mutationFn: (payload: BusinessInboundContactPayload) =>
      agentsApi.createInboundContact(businessId!, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: agentKeys.inboundContacts(businessId, false) });
      queryClient.invalidateQueries({ queryKey: agentKeys.inboundContacts(businessId, true) });
    },
  });
}

export function useUpdateBusinessInboundContact() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useMutation({
    mutationFn: ({ contactId, payload }: { contactId: string; payload: Partial<BusinessInboundContactPayload> }) =>
      agentsApi.updateInboundContact(businessId!, contactId, payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: agentKeys.inboundContacts(businessId, false) });
      queryClient.invalidateQueries({ queryKey: agentKeys.inboundContacts(businessId, true) });
    },
  });
}

export function useDeleteBusinessInboundContact() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);

  return useMutation({
    mutationFn: (contactId: string) => agentsApi.deleteInboundContact(businessId!, contactId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: agentKeys.inboundContacts(businessId, false) });
      queryClient.invalidateQueries({ queryKey: agentKeys.inboundContacts(businessId, true) });
    },
  });
}