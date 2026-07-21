import { apiClient } from "./client";
import type { Agent, BusinessInboundContact } from "@/types/entities";
import type { AgentSettings } from "@/types/agent-settings";

export interface AgentTestChatMessage {
  role: "user" | "assistant";
  content: string;
}

export interface CreateAgentRequest {
  name: string;
  description?: string;
}

export interface AgentTestTurnRequest {
  message: string;
  customerPhone?: string;
  customerName?: string;
  facts?: Record<string, string>;
  history: AgentTestChatMessage[];
}

export interface BusinessInboundContactPayload {
  type: string;
  key?: string;
  name: string;
  role?: string;
  phoneNumber: string;
  inboundAgentId: string;
  employeeId?: string | null;
  capabilitiesJson?: string | null;
  isActive?: boolean;
}
export interface AgentTestTurnResponse {
  success: boolean;
  response: string;
  errorMessage?: string;
  escalatedToHuman: boolean;
  reservationCreated: boolean;
  totalTokens: number;
  operationCount: number;
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

  create: (businessId: string, data: CreateAgentRequest) =>
    apiClient.post<Agent>(`/businesses/${businessId}/agents`, data),

  listInboundContactsByBusiness: (businessId: string, includeInactive = false) =>
    apiClient.get<BusinessInboundContact[]>(`/businesses/${businessId}/inbound-contacts`, includeInactive ? { includeInactive } : undefined),
  createInboundContact: (businessId: string, data: BusinessInboundContactPayload) =>
    apiClient.post<BusinessInboundContact>(`/businesses/${businessId}/inbound-contacts`, data),
  updateInboundContact: (businessId: string, contactId: string, data: Partial<BusinessInboundContactPayload>) =>
    apiClient.put<BusinessInboundContact>(`/businesses/${businessId}/inbound-contacts/${contactId}`, data),
  deleteInboundContact: (businessId: string, contactId: string) =>
    apiClient.delete(`/businesses/${businessId}/inbound-contacts/${contactId}`),

  getById: (agentId: string) => apiClient.get<Agent>(`/agents/${agentId}`),

  updateSettings: (agentId: string, settings: AgentSettings) =>
    apiClient.put<Agent>(`/agents/${agentId}/settings`, { settings }),

  updateStatus: (agentId: string, isActive: boolean) =>
    apiClient.put<Agent>(`/agents/${agentId}/status`, { isActive }),

  testTurn: (agentId: string, request: AgentTestTurnRequest) =>
    apiClient.post<AgentTestTurnResponse>(`/agents/${agentId}/test-turn`, request),
};
