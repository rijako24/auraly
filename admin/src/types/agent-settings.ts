/** Espejo declarativo de Agents.SettingsJson (motor agentic). */

export interface AgentHumanEscalationSettings {
  contacts?: string[];
  killSwitchPhrases?: string[];
}

export interface AgentFlowStageVariant {
  goal?: string;
  hint?: string;
}

export interface AgentFlowStage {
  id: string;
  goal: string;
  hint?: string;
  allowedTools?: string[];
  suggestedTools?: string[];
  advanceWhenFacts?: string[];
  reentryOnFactChanged?: string[];
  skipWhen?: string;
  autoSetOnSkip?: Record<string, string>;
  variants?: Record<string, AgentFlowStageVariant>;
}

export interface AgentFlowDefinition {
  stageDetection?: string;
  stages: AgentFlowStage[];
}

export interface FactSchemaEntry {
  key: string;
  role?: string;
  label: string;
  type: string;
  required?: boolean;
  source?: string;
  persistsAcrossConversations?: boolean;
  defaultValue?: string;
  captureMode?: string;
  aliases?: string[];
}

export interface GuardDefinition {
  requires: string[];
}

export interface CheckoutPaymentDefinition {
  type?: string;
  percentage?: number;
}

export interface OrderCheckoutShippingDefinition {
  enabled?: boolean;
  localCity?: string;
  localCost?: number;
  nationalCost?: number;
}

export interface CheckoutModeDefinition {
  payment?: CheckoutPaymentDefinition;
  templateWithPayment?: string;
  templateNoPayment?: string;
  confirmationOutcome?: string;
  shipping?: OrderCheckoutShippingDefinition;
  requiredFactRoles?: Record<string, string>;
  systemFactBindings?: Record<string, string>;
  templateFactBindings?: Record<string, string>;
}

export interface CheckoutDefinitions {
  categoryModes?: Record<string, string>;
  modes?: Record<string, CheckoutModeDefinition>;
}

export interface MessageSequenceButton {
  id: string;
  title: string;
}

export interface MessageSequenceStep {
  body?: string;
  attachmentId?: string;
  buttons?: MessageSequenceButton[];
}

export interface MessageSequence {
  messages: MessageSequenceStep[];
}

export interface ExternalEscalationContact {
  key: string;
  name?: string;
  role?: string;
  phone: string;
  priority?: number;
  inboundAgentId?: string;
  pickupAddress?: string;
}

export interface ExternalEscalationEvent {
  enabled?: boolean;
  strategy?: string;
  attemptTimeoutMinutes?: number;
  attemptCodePrefix?: string;
  sendMessageSequence?: string;
  attemptSentNotificationEvent?: string;
  acceptedNotificationEvent?: string;
  contacts?: ExternalEscalationContact[];
}

export interface ExternalEscalationDefinitions {
  enabled?: boolean;
  events?: Record<string, ExternalEscalationEvent>;
}

export interface AgentEscalationsSettings {
  human?: AgentHumanEscalationSettings;
  external?: ExternalEscalationDefinitions;
}

export interface EventNotificationConfig {
  enabled?: boolean;
  recipients?: string[];
  sendMessageSequence?: string | null;
}

export type AgentNotificationDefinitions = Record<string, EventNotificationConfig>;

export interface AgentCommerceSettings {
  enabled?: boolean;
  provider?: "Local" | "Siigo" | "CustomHttp";
}

export interface AgentSettings {
  model?: string;
  temperature?: number;
  maxToolIterations?: number;
  consecutiveErrorEscalationThreshold?: number;
  persona?: string;
  policies?: string;
  enabledTools?: string[];
  escalations?: AgentEscalationsSettings;
  flow?: AgentFlowDefinition;
  factSchema?: FactSchemaEntry[];
  guards?: Record<string, GuardDefinition>;
  templates?: Record<string, string>;
  messageSequences?: Record<string, MessageSequence>;
  notifications?: AgentNotificationDefinitions;
  checkout?: CheckoutDefinitions;
  commerce?: AgentCommerceSettings;
}

export const DEFAULT_AGENT_SETTINGS: AgentSettings = {
  model: "gpt-4.1-mini",
  temperature: 0.7,
  maxToolIterations: 6,
  consecutiveErrorEscalationThreshold: 3,
  persona: "",
  policies: "",
  enabledTools: [],
  escalations: { human: { contacts: [], killSwitchPhrases: [] }, external: { enabled: false, events: {} } },
  flow: { stageDetection: "automatic", stages: [] },
  factSchema: [],
  guards: {},
  templates: {},
  messageSequences: {},
  notifications: {},
  checkout: { categoryModes: {}, modes: {} },
  commerce: { enabled: false, provider: "Local" },
};

export function parseAgentSettingsFromAgent(agent: {
  settings?: unknown;
  settingsJson?: string | null;
}): AgentSettings {
  if (agent.settings && typeof agent.settings === "object") {
    return parseAgentSettings(agent.settings);
  }
  if (agent.settingsJson?.trim()) {
    try {
      return parseAgentSettings(JSON.parse(agent.settingsJson));
    } catch {
      return { ...DEFAULT_AGENT_SETTINGS };
    }
  }
  return { ...DEFAULT_AGENT_SETTINGS };
}

export function parseAgentSettings(raw: unknown): AgentSettings {
  if (!raw || typeof raw !== "object") return { ...DEFAULT_AGENT_SETTINGS };
  const s = raw as AgentSettings;
  return {
    ...DEFAULT_AGENT_SETTINGS,
    ...s,
    flow: s.flow ?? DEFAULT_AGENT_SETTINGS.flow,
    factSchema: s.factSchema ?? [],
    guards: s.guards ?? {},
    enabledTools: s.enabledTools ?? [],
    escalations: s.escalations ?? { human: { contacts: [], killSwitchPhrases: [] }, external: { enabled: false, events: {} } },
    messageSequences: s.messageSequences ?? {},
    notifications: s.notifications ?? {},
    commerce: s.commerce ?? { enabled: false, provider: "Local" },
  };
}
