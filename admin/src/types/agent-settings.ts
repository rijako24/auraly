/** Espejo declarativo de Agents.SettingsJson para el motor determinístico. */
export interface SignalAmbiguityRuleDefinition {
  type: "distinct_values"; valueProperty: string; field: string; minimumDistinctValues?: number;
}
export interface StageSignalDefinition {
  type: string; description?: string; valueSchema?: unknown; ambiguityRules?: SignalAmbiguityRuleDefinition[];
}
export interface StageConditionDefinition {
  all?: StageConditionDefinition[]; any?: StageConditionDefinition[]; not?: StageConditionDefinition;
  factPresent?: string; factMissing?: string; factChanged?: string; signalPresent?: string;
  verificationActive?: string; verificationMissing?: string; factEquals?: { key: string; value: unknown };
}
export interface StageEffectDefinition {
  type: string; fact?: string; value?: unknown; bindings?: Record<string, string>; facts?: string[];
  template?: string; dataPath?: string; mode?: string; priority?: string; sequence?: string;
  event?: string; reason?: string;
}
export interface StageResponseDefinition { mode?: string; guidance?: string; template?: string; sendMessageSequence?: string; suppressText?: boolean; }
export interface StageOutcomeHandlerDefinition { effects?: StageEffectDefinition[]; response?: StageResponseDefinition; }
export interface StageActionDefinition {
  id: string; operation: string; trigger?: "on_enter" | "when_ready" | "on_signal" | "on_fact_changed" | "manual";
  signal?: string; condition?: StageConditionDefinition; arguments?: Record<string, unknown>;
  execution?: { idempotency?: "input_version" | "once_per_request" | "none"; timeoutSeconds?: number; maxAttempts?: number };
  onOutcome?: Record<string, StageOutcomeHandlerDefinition>;
}
export interface StageTransitionDefinition {
  id: string; priority?: number; condition: StageConditionDefinition; to: string; effects?: StageEffectDefinition[];
}
export interface AgentFlowStage {
  id: string; name?: string; goal: string; collect?: string[]; advanceWhenFacts?: string[];
  signals?: StageSignalDefinition[]; actions?: StageActionDefinition[]; transitions?: StageTransitionDefinition[];
  response?: StageResponseDefinition; conversationGuidance?: string; reentryOnFactChanged?: string[];
}
export interface AgentFlowDefinition {
  id: string; type?: "primary" | "secondary"; routingGuidance?: string; ttlSeconds?: number; stages: AgentFlowStage[];
}
export interface AgentGlobalAction {
  id: string; priority?: number; goal: string; conversationGuidance?: string;
  signal: StageSignalDefinition; actions?: StageActionDefinition[]; response?: StageResponseDefinition;
}
export interface FactSchemaEntry {
  key: string; role?: string; label: string; type: string; extractionGuidance?: string; options?: { value: string; label: string; selector?: string }[]; required?: boolean;
  source?: string; showInCollectedInfo?: boolean; defaultValue?: string;
  scope?: "customer" | "request" | "ephemeral"; retentionDays?: number; expireOnBusinessDayChange?: boolean;
  dependsOn?: string[]; valueSource?: string;
}
export interface AgentHumanEscalationSettings { contacts?: string[]; }
export interface ExternalEscalationContact { businessInboundContactId?: string; priority?: number; pickupAddress?: string; }
export interface ExternalEscalationEvent {
  enabled?: boolean; contactType?: string; pickupAddress?: string; attemptTimeoutMinutes?: number; attemptCodePrefix?: string;
  sendMessageSequence?: string
  suppressText?: boolean; outcomeEvents?: Record<string, string>; contacts?: ExternalEscalationContact[];
}
export interface ExternalEscalationDefinitions { enabled?: boolean; events?: Record<string, ExternalEscalationEvent>; }
export interface AgentEscalationsSettings { human?: AgentHumanEscalationSettings; external?: ExternalEscalationDefinitions; }
export interface CheckoutPaymentDefinition { type?: string; percentage?: number; }
export interface CheckoutPaymentMethodDefinition { label?: string; aliases?: string[]; payment?: CheckoutPaymentDefinition | null; template?: string; confirmationOutcome?: string; }
export interface OrderCheckoutShippingDefinition { enabled?: boolean; localCity?: string; localCost?: number; nationalCost?: number; }
export interface CheckoutModeDefinition {
  paymentMethods?: Record<string, CheckoutPaymentMethodDefinition>; shipping?: OrderCheckoutShippingDefinition;
  requiredFactRoles?: Record<string, string>; systemFactBindings?: Record<string, string>; templateFactBindings?: Record<string, string>;
}
export interface CheckoutDefinitions { currency?: string; modes?: Record<string, CheckoutModeDefinition>; }
export interface MessageSequenceButton { id: string; title: string; }
export interface MessageSequenceStep {
  type?: string; body?: string; attachmentId?: string; buttons?: MessageSequenceButton[]; templateName?: string;
  language?: string; headerParameters?: string[]; bodyParameters?: string[];
}
export interface MessageSequence { messages: MessageSequenceStep[]; }
export interface ReservationAutomationTrigger { type?: string; hoursBefore?: number | null; daysBefore?: number; time?: string | null; }
export interface ReservationAutomationActionConfig { operation?: string; arguments?: Record<string, unknown>; sendMessageSequence?: string | null; }
export interface ReservationAutomationConfig {
  enabled?: boolean; trigger?: ReservationAutomationTrigger; sendMessageSequence?: string | null;
  actions?: Record<string, ReservationAutomationActionConfig>;
}
export interface ReservationAutomationDefinitions { confirmation?: ReservationAutomationConfig | null; reminder?: ReservationAutomationConfig | null; }
export interface WompiWebhookOutcomeConfig { sendMessageSequence?: string | null; }
export interface WebhookDefinitions { wompi?: Record<string, WompiWebhookOutcomeConfig>; }
export interface EventNotificationConfig { enabled?: boolean; recipients?: string[]; sendMessageSequence?: string | null; }
export type AgentNotificationDefinitions = Record<string, EventNotificationConfig>;
export interface AgentCommerceSettings { enabled?: boolean; provider?: "Local" | "Siigo" | "CustomHttp" | "Mantis"; }
export interface AgentOperatingHoursSettings { enforce?: boolean; outsideHours?: StageResponseDefinition; }

export interface AgentSettings {
  model?: string; temperature?: number; historyWindowSize?: number; persona?: string; policies?: string;
  flows?: AgentFlowDefinition[]; globalActions?: AgentGlobalAction[]; factSchema?: FactSchemaEntry[];
  templates?: Record<string, string>; messageSequences?: Record<string, MessageSequence>; webhooks?: WebhookDefinitions;
  notifications?: AgentNotificationDefinitions; reservationAutomations?: ReservationAutomationDefinitions;
  reservationManagement?: Record<string, unknown>; escalations?: AgentEscalationsSettings; checkout?: CheckoutDefinitions;
  commerce?: AgentCommerceSettings; operatingHours?: AgentOperatingHoursSettings;
}
export const DEFAULT_AGENT_SETTINGS: AgentSettings = {
  model: "gpt-4.1-mini", temperature: 0.2, historyWindowSize: 20, persona: "", policies: "", flows: [],
  globalActions: [], factSchema: [], templates: {}, messageSequences: {}, webhooks: { wompi: {} }, notifications: {},
  reservationAutomations: {}, reservationManagement: {}, escalations: { human: { contacts: [] }, external: { enabled: false, events: {} } },
  checkout: { currency: "COP", modes: {} }, commerce: { enabled: false, provider: "Local" },
  operatingHours: { enforce: false, outsideHours: {} },
};
export function parseAgentSettingsFromAgent(agent: { settings?: unknown; settingsJson?: string | null }): AgentSettings {
  if (agent.settings && typeof agent.settings === "object") return parseAgentSettings(agent.settings);
  if (agent.settingsJson?.trim()) { try { return parseAgentSettings(JSON.parse(agent.settingsJson)); } catch { return { ...DEFAULT_AGENT_SETTINGS }; } }
  return { ...DEFAULT_AGENT_SETTINGS };
}
export function parseAgentSettings(raw: unknown): AgentSettings {
  if (!raw || typeof raw !== "object") return { ...DEFAULT_AGENT_SETTINGS };
  const s = raw as AgentSettings;
  return {
    ...DEFAULT_AGENT_SETTINGS, ...s, flows: s.flows ?? [], globalActions: s.globalActions ?? [], factSchema: s.factSchema ?? [],
    templates: s.templates ?? {}, messageSequences: s.messageSequences ?? {}, webhooks: s.webhooks ?? { wompi: {} },
    notifications: s.notifications ?? {}, reservationAutomations: s.reservationAutomations ?? {}, reservationManagement: s.reservationManagement ?? {},
    escalations: s.escalations ?? DEFAULT_AGENT_SETTINGS.escalations, checkout: s.checkout ?? DEFAULT_AGENT_SETTINGS.checkout,
    commerce: s.commerce ?? DEFAULT_AGENT_SETTINGS.commerce, operatingHours: s.operatingHours ?? DEFAULT_AGENT_SETTINGS.operatingHours,
  };
}
