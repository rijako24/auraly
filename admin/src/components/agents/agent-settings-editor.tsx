"use client";

import { useCallback, useMemo, useState } from "react";
import { Plus, Trash2 } from "lucide-react";

import { AgentFlowCanvas } from "@/components/agents/agent-flow-canvas";
import { JsonEditor } from "@/components/forms/json-editor";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import { AGENT_TOOLS_CATALOG } from "@/lib/agent-tools-catalog";
import type {
  AgentFlowStage,
  AgentSettings,
  FactSchemaEntry,
  GuardDefinition,
} from "@/types/agent-settings";

export type AgentEditorSection =
  | "identity"
  | "policies"
  | "flow"
  | "facts"
  | "tools"
  | "external"
  | "safety"
  | "advanced";

interface AgentSettingsEditorProps {
  value: AgentSettings;
  onChange: (next: AgentSettings) => void;
  availableAgents?: Array<{ agentId: string; name: string }>;
  /** Modo wizard: muestra solo una sección sin pestañas. */
  section?: AgentEditorSection;
}

export function AgentSettingsEditor({ value, onChange, availableAgents = [], section }: AgentSettingsEditorProps) {
  const [selectedStageId, setSelectedStageId] = useState<string | null>(
    value.flow?.stages[0]?.id ?? null
  );

  const stages = value.flow?.stages ?? [];
  const factKeys = useMemo(
    () => (value.factSchema ?? []).map((f) => f.key),
    [value.factSchema]
  );

  const patch = useCallback(
    (partial: Partial<AgentSettings>) => onChange({ ...value, ...partial }),
    [value, onChange]
  );

  const patchStages = useCallback(
    (nextStages: AgentFlowStage[]) =>
      patch({
        flow: {
          stageDetection: value.flow?.stageDetection ?? "automatic",
          stages: nextStages,
        },
      }),
    [patch, value.flow?.stageDetection]
  );

  const selectedStageIndex = stages.findIndex((s) => s.id === selectedStageId);
  const selectedStage = selectedStageIndex >= 0 ? stages[selectedStageIndex] : null;

  const updateStage = (index: number, partial: Partial<AgentFlowStage>) => {
    const next = stages.map((s, i) => (i === index ? { ...s, ...partial } : s));
    patchStages(next);
  };

  const addStage = () => {
    const id = `stage_${stages.length + 1}`;
    const next: AgentFlowStage = {
      id,
      goal: "",
      allowedTools: [],
      advanceWhenFacts: [],
    };
    patchStages([...stages, next]);
    setSelectedStageId(id);
  };

  const removeStage = (index: number) => {
    const next = stages.filter((_, i) => i !== index);
    patchStages(next);
    setSelectedStageId(next[0]?.id ?? null);
  };

  const toggleTool = (tool: string, list: string[] | undefined) => {
    const set = new Set(list ?? []);
    if (set.has(tool)) set.delete(tool);
    else set.add(tool);
    return [...set];
  };

  const defaultTab = section ?? "identity";

  return (
    <Tabs value={defaultTab} className="space-y-4">
      <TabsList className={section ? "hidden" : "flex h-auto flex-wrap gap-1"}>
        <TabsTrigger value="identity">Identidad</TabsTrigger>
        <TabsTrigger value="policies">Políticas</TabsTrigger>
        <TabsTrigger value="flow">Flujo</TabsTrigger>
        <TabsTrigger value="facts">Datos (facts)</TabsTrigger>
        <TabsTrigger value="tools">Tools y guards</TabsTrigger>
        <TabsTrigger value="external">Externos</TabsTrigger>
        <TabsTrigger value="safety">Seguridad</TabsTrigger>
        <TabsTrigger value="advanced">JSON</TabsTrigger>
      </TabsList>

      <TabsContent value="identity" className="space-y-4">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Modelo</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="model">Modelo</Label>
              <Input
                id="model"
                value={value.model ?? ""}
                onChange={(e) => patch({ model: e.target.value })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="temperature">Temperature</Label>
              <Input
                id="temperature"
                type="number"
                step="0.1"
                min={0}
                max={2}
                value={value.temperature ?? 0.7}
                onChange={(e) => patch({ temperature: parseFloat(e.target.value) })}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="maxToolIterations">Máx. iteraciones de tools</Label>
              <Input
                id="maxToolIterations"
                type="number"
                min={1}
                max={20}
                value={value.maxToolIterations ?? 6}
                onChange={(e) =>
                  patch({ maxToolIterations: parseInt(e.target.value, 10) })
                }
              />
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">Persona (rol e identidad)</CardTitle>
            <CardDescription>Markdown — bloque principal del agente</CardDescription>
          </CardHeader>
          <CardContent>
            <Textarea
              className="min-h-[200px] font-mono text-sm"
              value={value.persona ?? ""}
              onChange={(e) => patch({ persona: e.target.value })}
            />
          </CardContent>
        </Card>
      </TabsContent>

      <TabsContent value="policies" className="space-y-4">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Políticas</CardTitle>
            <CardDescription>Reglas operativas en markdown</CardDescription>
          </CardHeader>
          <CardContent>
            <Textarea
              className="min-h-[240px] font-mono text-sm"
              value={value.policies ?? ""}
              onChange={(e) => patch({ policies: e.target.value })}
            />
          </CardContent>
        </Card>
      </TabsContent>

      <TabsContent value="flow" className="space-y-4">
        <AgentFlowCanvas
          stages={stages}
          selectedStageId={selectedStageId}
          onSelectStage={setSelectedStageId}
        />

        <div className="flex justify-end">
          <Button type="button" variant="outline" size="sm" onClick={addStage}>
            <Plus className="mr-1 h-4 w-4" />
            Añadir etapa
          </Button>
        </div>

        <div className="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Etapas</CardTitle>
            </CardHeader>
            <CardContent className="space-y-2">
              {stages.map((stage, index) => (
                <button
                  key={stage.id}
                  type="button"
                  onClick={() => setSelectedStageId(stage.id)}
                  className={`flex w-full items-center justify-between rounded-md border px-3 py-2 text-left text-sm transition-colors hover:bg-muted/50 ${
                    stage.id === selectedStageId ? "border-primary bg-muted/50" : ""
                  }`}
                >
                  <span>
                    <span className="font-medium">{stage.id}</span>
                    <span className="ml-2 text-muted-foreground line-clamp-1">
                      {stage.goal || "Sin objetivo"}
                    </span>
                  </span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="shrink-0"
                    onClick={(e) => {
                      e.stopPropagation();
                      removeStage(index);
                    }}
                  >
                    <Trash2 className="h-4 w-4 text-destructive" />
                  </Button>
                </button>
              ))}
            </CardContent>
          </Card>

          {selectedStage && selectedStageIndex >= 0 && (
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Editar: {selectedStage.id}</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="space-y-2">
                  <Label>ID</Label>
                  <Input
                    value={selectedStage.id}
                    onChange={(e) =>
                      updateStage(selectedStageIndex, { id: e.target.value })
                    }
                  />
                </div>
                <div className="space-y-2">
                  <Label>Objetivo (goal)</Label>
                  <Textarea
                    value={selectedStage.goal}
                    onChange={(e) =>
                      updateStage(selectedStageIndex, { goal: e.target.value })
                    }
                  />
                </div>
                <div className="space-y-2">
                  <Label>Hint (instrucción al LLM)</Label>
                  <Textarea
                    className="min-h-[120px] font-mono text-xs"
                    value={selectedStage.hint ?? ""}
                    onChange={(e) =>
                      updateStage(selectedStageIndex, { hint: e.target.value })
                    }
                  />
                </div>
                <div className="space-y-2">
                  <Label>Tools permitidas</Label>
                  <div className="flex flex-wrap gap-2">
                    {AGENT_TOOLS_CATALOG.map((t) => {
                      const on = (selectedStage.allowedTools ?? []).includes(t.name);
                      return (
                        <Badge
                          key={t.name}
                          variant={on ? "default" : "outline"}
                          className="cursor-pointer"
                          onClick={() =>
                            updateStage(selectedStageIndex, {
                              allowedTools: toggleTool(
                                t.name,
                                selectedStage.allowedTools
                              ),
                            })
                          }
                        >
                          {t.name}
                        </Badge>
                      );
                    })}
                  </div>
                </div>
                <div className="space-y-2">
                  <Label>Facts para avanzar</Label>
                  <div className="flex flex-wrap gap-2">
                    {factKeys.map((key) => {
                      const on = (selectedStage.advanceWhenFacts ?? []).includes(key);
                      return (
                        <Badge
                          key={key}
                          variant={on ? "default" : "outline"}
                          className="cursor-pointer"
                          onClick={() => {
                            const set = new Set(selectedStage.advanceWhenFacts ?? []);
                            if (set.has(key)) set.delete(key);
                            else set.add(key);
                            updateStage(selectedStageIndex, {
                              advanceWhenFacts: [...set],
                            });
                          }}
                        >
                          {key}
                        </Badge>
                      );
                    })}
                    {factKeys.length === 0 && (
                      <p className="text-xs text-muted-foreground">
                        Define facts en la pestaña Datos.
                      </p>
                    )}
                  </div>
                </div>
              </CardContent>
            </Card>
          )}
        </div>
      </TabsContent>

      <TabsContent value="facts">
        <FactSchemaEditor
          entries={value.factSchema ?? []}
          onChange={(factSchema) => patch({ factSchema })}
        />
      </TabsContent>

      <TabsContent value="tools" className="space-y-4">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Tools habilitadas (enabledTools)</CardTitle>
          </CardHeader>
          <CardContent className="flex flex-wrap gap-2">
            {AGENT_TOOLS_CATALOG.map((t) => {
              const on = (value.enabledTools ?? []).includes(t.name);
              return (
                <Badge
                  key={t.name}
                  variant={on ? "default" : "outline"}
                  className="cursor-pointer"
                  onClick={() => {
                    const set = new Set(value.enabledTools ?? []);
                    if (set.has(t.name)) set.delete(t.name);
                    else set.add(t.name);
                    patch({ enabledTools: [...set] });
                  }}
                >
                  {t.label}
                </Badge>
              );
            })}
          </CardContent>
        </Card>

        <GuardsEditor
          guards={value.guards ?? {}}
          enabledTools={value.enabledTools ?? []}
          onChange={(guards) => patch({ guards })}
        />
      </TabsContent>

      <TabsContent value="external">
        <ExternalEscalationsEditor
          value={value}
          availableAgents={availableAgents}
          onChange={patch}
        />
      </TabsContent>

      <TabsContent value="safety" className="space-y-4">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Kill switch</CardTitle>
            <CardDescription>Una frase por línea</CardDescription>
          </CardHeader>
          <CardContent>
            <Textarea
              className="min-h-[120px]"
              value={(value.escalations?.human?.killSwitchPhrases ?? []).join("\n")}
              onChange={(e) =>
                patch({
                  escalations: {
                    ...value.escalations,
                    human: {
                      ...value.escalations?.human,
                      killSwitchPhrases: e.target.value
                        .split("\n")
                        .map((s) => s.trim())
                        .filter(Boolean),
                    },
                  },
                })
              }
            />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Escalación</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            <Label>Contactos WhatsApp (uno por línea)</Label>
            <Textarea
              value={(value.escalations?.human?.contacts ?? []).join("\n")}
              onChange={(e) =>
                patch({
                  escalations: {
                    ...value.escalations,
                    human: {
                      ...value.escalations?.human,
                      contacts: e.target.value
                        .split("\n")
                        .map((s) => s.trim())
                        .filter(Boolean),
                    },
                  },
                })
              }
            />
            <div className="space-y-2 pt-2">
              <Label>Umbral errores consecutivos</Label>
              <Input
                type="number"
                min={1}
                value={value.consecutiveErrorEscalationThreshold ?? 3}
                onChange={(e) =>
                  patch({
                    consecutiveErrorEscalationThreshold: parseInt(
                      e.target.value,
                      10
                    ),
                  })
                }
              />
            </div>
          </CardContent>
        </Card>
      </TabsContent>

      <TabsContent value="advanced">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">SettingsJson completo</CardTitle>
            <CardDescription>Modo avanzado — edición directa del documento</CardDescription>
          </CardHeader>
          <CardContent>
            <JsonEditor
              value={value as unknown as Record<string, unknown>}
              onChange={(v) => onChange(v as unknown as AgentSettings)}
            />
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>
  );
}

function FactSchemaEditor({
  entries,
  onChange,
}: {
  entries: FactSchemaEntry[];
  onChange: (entries: FactSchemaEntry[]) => void;
}) {
  const update = (index: number, partial: Partial<FactSchemaEntry>) => {
    onChange(entries.map((e, i) => (i === index ? { ...e, ...partial } : e)));
  };

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="text-base">Fact schema</CardTitle>
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={() =>
            onChange([
              ...entries,
              {
                key: `fact_${entries.length + 1}`,
                label: "",
                type: "string",
                source: "user",
              },
            ])
          }
        >
          <Plus className="mr-1 h-4 w-4" />
          Añadir
        </Button>
      </CardHeader>
      <CardContent className="space-y-4">
        {entries.map((entry, index) => (
          <div key={index} className="grid gap-2 rounded-md border p-3 sm:grid-cols-2">
            <div className="space-y-1">
              <Label>Key</Label>
              <Input
                value={entry.key}
                onChange={(e) => update(index, { key: e.target.value })}
              />
            </div>
            <div className="space-y-1">
              <Label>Label</Label>
              <Input
                value={entry.label}
                onChange={(e) => update(index, { label: e.target.value })}
              />
            </div>
            <div className="space-y-1">
              <Label>Tipo</Label>
              <Input
                value={entry.type}
                onChange={(e) => update(index, { type: e.target.value })}
              />
            </div>
            <div className="space-y-1">
              <Label>Source</Label>
              <Input
                value={entry.source ?? "user"}
                onChange={(e) => update(index, { source: e.target.value })}
              />
            </div>
            <div className="flex items-center gap-2 sm:col-span-2">
              <Checkbox
                checked={!!entry.required}
                onCheckedChange={(c) => update(index, { required: c === true })}
              />
              <Label>Requerido</Label>
              <Checkbox
                className="ml-4"
                checked={entry.captureMode === "eager"}
                onCheckedChange={(c) =>
                  update(index, { captureMode: c === true ? "eager" : "onDemand" })
                }
              />
              <Label>Captura eager</Label>
            </div>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

function ExternalEscalationsEditor({
  value,
  availableAgents,
  onChange,
}: {
  value: AgentSettings;
  availableAgents: Array<{ agentId: string; name: string }>;
  onChange: (partial: Partial<AgentSettings>) => void;
}) {
  const externalEscalations = value.escalations?.external ?? { enabled: false, events: {} };
  const events = externalEscalations.events ?? {};
  const eventNames = Object.keys(events);

  const patchExternal = (nextEvents: typeof events, enabled = externalEscalations.enabled ?? true) =>
    onChange({
      escalations: {
        ...value.escalations,
        external: {
          ...externalEscalations,
          enabled,
          events: nextEvents,
        },
      },
    });

  const addEvent = () => {
    const name = eventNames.includes("order_paid")
      ? `external_event_${eventNames.length + 1}`
      : "order_paid";
    patchExternal({
      ...events,
      [name]: {
        enabled: true,
        strategy: "sequential",
        attemptTimeoutMinutes: 5,
        attemptCodePrefix: "PED",
        sendMessageSequence: "",
        contacts: [],
      },
    }, true);
  };

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <div>
          <CardTitle className="text-base">Escalaciones externas</CardTitle>
          <CardDescription>Contactos externos y agente que procesa sus respuestas</CardDescription>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={addEvent}>
          <Plus className="mr-1 h-4 w-4" />
          Evento
        </Button>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-center gap-2">
          <Checkbox
            checked={externalEscalations.enabled === true}
            onCheckedChange={(checked) => patchExternal(events, checked === true)}
          />
          <Label>Habilitado</Label>
        </div>

        {eventNames.map((eventName) => {
          const event = events[eventName] ?? {};
          const contacts = event.contacts ?? [];
          const updateEvent = (nextEvent: typeof event, nextName = eventName) => {
            const next = { ...events };
            delete next[eventName];
            next[nextName] = nextEvent;
            patchExternal(next, externalEscalations.enabled ?? true);
          };

          return (
            <div key={eventName} className="space-y-3 rounded-md border p-3">
              <div className="grid gap-2 sm:grid-cols-4">
                <div className="space-y-1">
                  <Label>Evento</Label>
                  <Input value={eventName} onChange={(e) => updateEvent(event, e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label>Secuencia</Label>
                  <Input
                    value={event.sendMessageSequence ?? ""}
                    onChange={(e) => updateEvent({ ...event, sendMessageSequence: e.target.value })}
                  />
                </div>
                <div className="space-y-1">
                  <Label>Timeout min</Label>
                  <Input
                    type="number"
                    min={1}
                    value={event.attemptTimeoutMinutes ?? 5}
                    onChange={(e) => updateEvent({ ...event, attemptTimeoutMinutes: parseInt(e.target.value, 10) })}
                  />
                </div>
                <div className="space-y-1">
                  <Label>Prefijo</Label>
                  <Input
                    value={event.attemptCodePrefix ?? "EXT"}
                    onChange={(e) => updateEvent({ ...event, attemptCodePrefix: e.target.value })}
                  />
                </div>
              </div>

              <div className="grid gap-2 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label>Evento al llamar</Label>
                  <Input
                    placeholder="delivery_called"
                    value={event.attemptSentNotificationEvent ?? ""}
                    onChange={(e) =>
                      updateEvent({ ...event, attemptSentNotificationEvent: e.target.value })
                    }
                  />
                </div>
                <div className="space-y-1">
                  <Label>Evento al aceptar</Label>
                  <Input
                    placeholder="delivery_confirmed"
                    value={event.acceptedNotificationEvent ?? ""}
                    onChange={(e) =>
                      updateEvent({ ...event, acceptedNotificationEvent: e.target.value })
                    }
                  />
                </div>
              </div>

              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <Label>Contactos</Label>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() =>
                      updateEvent({
                        ...event,
                        contacts: [
                          ...contacts,
                          {
                            key: `contacto_${contacts.length + 1}`,
                            name: "",
                            role: "",
                            phone: "",
                            priority: contacts.length + 1,
                            inboundAgentId: availableAgents[0]?.agentId ?? "",
                            pickupAddress: "",
                          },
                        ],
                      })
                    }
                  >
                    <Plus className="mr-1 h-4 w-4" />
                    Contacto
                  </Button>
                </div>

                {contacts.map((contact, index) => (
                  <div key={index} className="grid gap-2 rounded-md border p-3 sm:grid-cols-2 lg:grid-cols-7">
                    <Input
                      placeholder="key"
                      value={contact.key}
                      onChange={(e) => {
                        const next = [...contacts];
                        next[index] = { ...contact, key: e.target.value };
                        updateEvent({ ...event, contacts: next });
                      }}
                    />
                    <Input
                      placeholder="nombre"
                      value={contact.name ?? ""}
                      onChange={(e) => {
                        const next = [...contacts];
                        next[index] = { ...contact, name: e.target.value };
                        updateEvent({ ...event, contacts: next });
                      }}
                    />
                    <Input
                      placeholder="rol"
                      value={contact.role ?? ""}
                      onChange={(e) => {
                        const next = [...contacts];
                        next[index] = { ...contact, role: e.target.value };
                        updateEvent({ ...event, contacts: next });
                      }}
                    />
                    <Input
                      placeholder="+57..."
                      value={contact.phone}
                      onChange={(e) => {
                        const next = [...contacts];
                        next[index] = { ...contact, phone: e.target.value };
                        updateEvent({ ...event, contacts: next });
                      }}
                    />
                    <Input
                      placeholder="direccion recogida"
                      value={contact.pickupAddress ?? ""}
                      onChange={(e) => {
                        const next = [...contacts];
                        next[index] = { ...contact, pickupAddress: e.target.value };
                        updateEvent({ ...event, contacts: next });
                      }}
                    />
                    <select
                      className="h-9 rounded-md border border-input bg-background px-3 text-sm"
                      value={contact.inboundAgentId ?? ""}
                      onChange={(e) => {
                        const next = [...contacts];
                        next[index] = { ...contact, inboundAgentId: e.target.value };
                        updateEvent({ ...event, contacts: next });
                      }}
                    >
                      <option value="">Agente</option>
                      {availableAgents.map((agent) => (
                        <option key={agent.agentId} value={agent.agentId}>
                          {agent.name}
                        </option>
                      ))}
                    </select>
                    <div className="flex gap-2">
                      <Input
                        type="number"
                        min={1}
                        value={contact.priority ?? index + 1}
                        onChange={(e) => {
                          const next = [...contacts];
                          next[index] = { ...contact, priority: parseInt(e.target.value, 10) };
                          updateEvent({ ...event, contacts: next });
                        }}
                      />
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        onClick={() => updateEvent({ ...event, contacts: contacts.filter((_, i) => i !== index) })}
                      >
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          );
        })}
      </CardContent>
    </Card>
  );
}

function GuardsEditor({
  guards,
  enabledTools,
  onChange,
}: {
  guards: Record<string, GuardDefinition>;
  enabledTools: string[];
  onChange: (g: Record<string, GuardDefinition>) => void;
}) {
  const tools = enabledTools.length > 0 ? enabledTools : Object.keys(guards);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Guards (precondiciones por tool)</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {tools.map((toolName) => {
          const requires = guards[toolName]?.requires ?? [];
          return (
            <div key={toolName} className="space-y-2 rounded-md border p-3">
              <Label className="font-mono text-sm">{toolName}</Label>
              <Textarea
                className="font-mono text-xs"
                placeholder="fact:service&#10;verification:availability_checked"
                value={requires.join("\n")}
                onChange={(e) => {
                  const next = { ...guards };
                  next[toolName] = {
                    requires: e.target.value
                      .split("\n")
                      .map((s) => s.trim())
                      .filter(Boolean),
                  };
                  onChange(next);
                }}
              />
            </div>
          );
        })}
        {tools.length === 0 && (
          <p className="text-sm text-muted-foreground">
            Habilita tools para configurar guards.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
