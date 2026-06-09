import { apiClient } from "./client";
import type { Agent } from "@/types/entities";
import type { AgentSettings } from "@/types/agent-settings";

export const agentsApi = {
  listByBusiness: (businessId: string) =>
    apiClient.get<Agent[]>(`/businesses/${businessId}/agents`),

  getById: (agentId: string) => apiClient.get<Agent>(`/agents/${agentId}`),

  updateSettings: (agentId: string, settings: AgentSettings) =>
    apiClient.put<Agent>(`/agents/${agentId}/settings`, { settings }),
};
