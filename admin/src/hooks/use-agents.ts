"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { agentsApi } from "@/services/api/agents";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { AgentSettings } from "@/types/agent-settings";

export const agentKeys = {
  all: ["agents"] as const,
  lists: () => [...agentKeys.all, "list"] as const,
  list: (businessId: string | null) => [...agentKeys.lists(), businessId] as const,
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
