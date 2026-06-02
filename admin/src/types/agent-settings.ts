/** Espejo declarativo de Agents.SettingsJson (motor agentic). */

export interface AgentEscalationSettings {
  contacts?: string[];
}

export interface StageConstraints {
  maxQuestions?: number;
  presentationMode?: string;
}

export interface AgentFlowStageVariant {
  goal?: string;
  hint?: string;
  constraints?: StageConstraints;
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
  completesOnEnter?: boolean;
  constraints?: StageConstraints;
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
  captureMode?: string;
  aliases?: string[];
}

export interface GuardDefinition {
  requires: string[];
}

export interface AgentSettings {
  model?: string;
  temperature?: number;
  maxToolIterations?: number;
  consecutiveErrorEscalationThreshold?: number;
  persona?: string;
  policies?: string;
  killSwitchPhrases?: string[];
  enabledTools?: string[];
  escalation?: AgentEscalationSettings;
  flow?: AgentFlowDefinition;
  factSchema?: FactSchemaEntry[];
  guards?: Record<string, GuardDefinition>;
  templates?: Record<string, string>;
}

export const DEFAULT_AGENT_SETTINGS: AgentSettings = {
  model: "gpt-4.1-mini",
  temperature: 0.7,
  maxToolIterations: 6,
  consecutiveErrorEscalationThreshold: 3,
  persona: "",
  policies: "",
  killSwitchPhrases: [],
  enabledTools: [],
  escalation: { contacts: [] },
  flow: { stageDetection: "automatic", stages: [] },
  factSchema: [],
  guards: {},
  templates: {},
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
    killSwitchPhrases: s.killSwitchPhrases ?? [],
    escalation: s.escalation ?? { contacts: [] },
  };
}
