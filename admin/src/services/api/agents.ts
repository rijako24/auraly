import type { PagedRequest, PagedResponse } from "@/types/api";
import type {
  Agent,
  AgentChatRequest,
  AgentChatResponse,
  AgentDetail,
  AgentType,
  FlowDefinitionAdmin,
  FlowNodeCatalogEntry,
  KnowledgeSourceAdmin,
} from "@/types/entities";
import { apiClient } from "./client";

export type CreateAgentPayload = {
  businessId: string;
  agentTypeId: string;
  name: string;
  description?: string | null;
  settingsJson?: string | null;
  systemPrompt?: string | null;
};

export type UpdateAgentPayload = {
  name?: string | null;
  description?: string | null;
  settingsJson?: string | null;
  isActive?: boolean | null;
};

export type SaveWorkflowPayload = {
  name?: string | null;
  description?: string | null;
  definitionJson: string;
};

export const agentsApi = {
  list: (params: Partial<PagedRequest> & { businessId: string }) =>
    apiClient.get<PagedResponse<Agent>>("/agents", params as Record<string, string | number | boolean | undefined>),

  getById: (id: string) => apiClient.get<AgentDetail>(`/agents/${id}`),

  create: (body: CreateAgentPayload) => apiClient.post<Agent>("/agents", body),

  update: (id: string, body: UpdateAgentPayload) => apiClient.put<Agent>(`/agents/${id}`, body),

  getTypes: () => apiClient.get<AgentType[]>("/agents/types"),

  getNodeCatalog: () => apiClient.get<FlowNodeCatalogEntry[]>("/agents/node-catalog"),

  getWorkflow: (agentId: string) => apiClient.get<FlowDefinitionAdmin>(`/agents/${agentId}/workflow`),

  saveWorkflow: (agentId: string, body: SaveWorkflowPayload) =>
    apiClient.put<FlowDefinitionAdmin>(`/agents/${agentId}/workflow`, body),

  getKnowledge: (agentId: string) => apiClient.get<KnowledgeSourceAdmin[]>(`/agents/${agentId}/knowledge`),

  addKnowledge: (
    agentId: string,
    body: { name: string; type: string; content: string; autoInject?: boolean }
  ) => apiClient.post<KnowledgeSourceAdmin>(`/agents/${agentId}/knowledge`, body),

  chat: (agentId: string, body: AgentChatRequest) =>
    apiClient.post<AgentChatResponse>(`/agents/${agentId}/chat`, body),
};
