import { apiClient } from "./client";
import type { Agent } from "@/types/entities";
import type { AgentSettings } from "@/types/agent-settings";

export interface AgentTestChatMessage {
  role: "user" | "assistant";
  content: string;
}

export interface AgentTestTurnRequest {
  message: string;
  customerPhone?: string;
  customerName?: string;
  facts?: Record<string, string>;
  history: AgentTestChatMessage[];
}

export interface AgentTestTurnResponse {
  success: boolean;
  response: string;
  errorMessage?: string;
  escalatedToHuman: boolean;
  reservationCreated: boolean;
  totalTokens: number;
  toolCallCount: number;
  facts: Record<string, string>;
  outboundMessages: Array<{
    body?: string;
    mediaUrl?: string;
    mediaType: string;
    filename?: string;
  }>;
  events: Array<{
    type: string;
    source: string;
    payload?: unknown;
    timestampUtc: string;
  }>;
}

export const agentsApi = {
  listByBusiness: (businessId: string) =>
    apiClient.get<Agent[]>(`/businesses/${businessId}/agents`),

  getById: (agentId: string) => apiClient.get<Agent>(`/agents/${agentId}`),

  updateSettings: (agentId: string, settings: AgentSettings) =>
    apiClient.put<Agent>(`/agents/${agentId}/settings`, { settings }),

  testTurn: (agentId: string, request: AgentTestTurnRequest) =>
    apiClient.post<AgentTestTurnResponse>(`/agents/${agentId}/test-turn`, request),
};
