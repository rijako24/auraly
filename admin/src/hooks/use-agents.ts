"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  agentsApi,
  type CreateAgentPayload,
  type SaveWorkflowPayload,
  type UpdateAgentPayload,
} from "@/services/api/agents";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { PagedRequest } from "@/types/api";
import type { AgentChatRequest } from "@/types/entities";

export const agentKeys = {
  all: ["agents"] as const,
  lists: () => [...agentKeys.all, "list"] as const,
  list: (businessId: string | null, params?: Partial<PagedRequest>) =>
    [...agentKeys.lists(), businessId, params] as const,
  details: () => [...agentKeys.all, "detail"] as const,
  detail: (id: string | null) => [...agentKeys.details(), id] as const,
  types: () => [...agentKeys.all, "types"] as const,
  nodeCatalog: () => [...agentKeys.all, "node-catalog"] as const,
  workflow: (agentId: string | null) => [...agentKeys.all, "workflow", agentId] as const,
  knowledge: (agentId: string | null) => [...agentKeys.all, "knowledge", agentId] as const,
};

export function useAgents(params?: Partial<PagedRequest>) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: agentKeys.list(businessId, params),
    queryFn: () => agentsApi.list({ ...params, businessId: businessId! }),
    enabled: !!businessId,
  });
}

export function useAgentDetail(id: string | null) {
  return useQuery({
    queryKey: agentKeys.detail(id),
    queryFn: () => agentsApi.getById(id!),
    enabled: !!id,
  });
}

export function useAgentTypes() {
  return useQuery({
    queryKey: agentKeys.types(),
    queryFn: () => agentsApi.getTypes(),
  });
}

export function useNodeCatalog() {
  return useQuery({
    queryKey: agentKeys.nodeCatalog(),
    queryFn: () => agentsApi.getNodeCatalog(),
  });
}

export function useAgentWorkflow(agentId: string | null) {
  return useQuery({
    queryKey: agentKeys.workflow(agentId),
    queryFn: () => agentsApi.getWorkflow(agentId!),
    enabled: !!agentId,
  });
}

export function useAgentKnowledge(agentId: string | null) {
  return useQuery({
    queryKey: agentKeys.knowledge(agentId),
    queryFn: () => agentsApi.getKnowledge(agentId!),
    enabled: !!agentId,
  });
}

export function useCreateAgent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateAgentPayload) => agentsApi.create(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: agentKeys.lists() }),
  });
}

export function useUpdateAgent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateAgentPayload }) => agentsApi.update(id, body),
    onSuccess: (_, { id }) => {
      qc.invalidateQueries({ queryKey: agentKeys.lists() });
      qc.invalidateQueries({ queryKey: agentKeys.detail(id) });
    },
  });
}

export function useSaveAgentWorkflow(agentId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveWorkflowPayload) => agentsApi.saveWorkflow(agentId, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: agentKeys.workflow(agentId) }),
  });
}

export function useAddAgentKnowledge(agentId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { name: string; type: string; content: string; autoInject?: boolean }) =>
      agentsApi.addKnowledge(agentId, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: agentKeys.knowledge(agentId) }),
  });
}

export function useAgentChat(agentId: string) {
  return useMutation({
    mutationFn: (body: AgentChatRequest) => agentsApi.chat(agentId, body),
  });
}
