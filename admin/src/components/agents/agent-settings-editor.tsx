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
import type { BusinessInboundContact } from "@/types/entities";
import type {
  AgentFlowStage,
  AgentSettings,
  FactSchemaEntry,
  GuardDefinition,
} from "@/types/agent-settings";

type GlobalAction = NonNullable<AgentSettings["globalActions"]>[number];
const HUMAN_ESCALATION_GLOBAL_ACTION_ID = "human_escalation";
const HUMAN_ESCALATION_TOOL = "escalate_to_human";
const HUMAN_ESCALATION_GOAL = "Notificar al equipo humano sin desactivar el bot.";
const isHumanEscalationAction = (action: GlobalAction) =>
  action.id === HUMAN_ESCALATION_GLOBAL_ACTION_ID ||
  (action.allowedTools ?? []).includes(HUMAN_ESCALATION_TOOL);

export type AgentEditorSection =
  | "identity"
  | "policies"
  | "flow"
  | "facts"
  | "tools"
  | "external"
  | "messages"
  | "checkout"
  | "actions"
  | "templates"
  | "safety"
  | "advanced";

interface AgentSettingsEditorProps {
  value: AgentSettings;
  onChange: (next: AgentSettings) => void;
  availableInboundContacts?: BusinessInboundContact[];
  /** Modo wizard: muestra solo una sección sin pestañas. */
  section?: AgentEditorSection;
}

export function AgentSettingsEditor({ value, onChange, availableInboundContacts = [], section }: AgentSettingsEditorProps) {
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

  const tabModeProps = section ? { value: section } : { defaultValue: "identity" };
  const sequenceNames = useMemo(() => Object.keys(value.messageSequences ?? {}), [value.messageSequences]);
  const humanEscalationAction = useMemo(
    () => (value.globalActions ?? []).find(isHumanEscalationAction),
    [value.globalActions]
  );
  const setHumanEscalationHint = useCallback(
    (hint: string) => {
      const actions = value.globalActions ?? [];
      const index = actions.findIndex(isHumanEscalationAction);
      const current = index >= 0 ? actions[index] : undefined;
      const allowedTools = Array.from(new Set([...(current?.allowedTools ?? []), HUMAN_ESCALATION_TOOL]));
      const nextAction: GlobalAction = {
        id: current?.id || HUMAN_ESCALATION_GLOBAL_ACTION_ID,
        priority: current?.priority ?? 1000,
        goal: current?.goal?.trim() ? current.goal : HUMAN_ESCALATION_GOAL,
        ...current,
        allowedTools,
        hint,
      };
      const nextActions =
        index >= 0
          ? actions.map((action, i) => (i === index ? nextAction : action))
          : [...actions, nextAction];
      patch({ globalActions: nextActions });
    },
    [patch, value.globalActions]
  );

  return (
    <Tabs {...tabModeProps} className="space-y-4">
      <TabsList className={section ? "hidden" : "flex h-auto flex-wrap gap-1"}>
        <TabsTrigger value="identity">Identidad</TabsTrigger>
        <TabsTrigger value="policies">Políticas</TabsTrigger>
        <TabsTrigger value="flow">Flujo</TabsTrigger>
        <TabsTrigger value="facts">Datos (facts)</TabsTrigger>
        <TabsTrigger value="tools">Tools y guards</TabsTrigger>
        <TabsTrigger value="external">Externos</TabsTrigger>
        <TabsTrigger value="messages">Mensajes</TabsTrigger>
        <TabsTrigger value="checkout">Checkout</TabsTrigger>
        <TabsTrigger value="actions">Acciones</TabsTrigger>
        <TabsTrigger value="templates">Templates</TabsTrigger>
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
            <div className="space-y-2">
              <Label htmlFor="historyWindowSize">Mensajes de historial</Label>
              <Input
                id="historyWindowSize"
                type="number"
                min={1}
                max={100}
                value={value.historyWindowSize ?? 20}
                onChange={(e) =>
                  patch({ historyWindowSize: parseInt(e.target.value, 10) })
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
                  <Label>Hint (instruccion al LLM)</Label>
                  <Textarea
                    className="min-h-[120px] font-mono text-xs"
                    value={selectedStage.hint ?? ""}
                    onChange={(e) =>
                      updateStage(selectedStageIndex, { hint: e.target.value })
                    }
                  />
                </div><div className="space-y-2">
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
          availableInboundContacts={availableInboundContacts}
          onChange={patch}
        />
      </TabsContent>

      <TabsContent value="messages" className="space-y-4">
        <MessageSequencesEditor
          sequences={value.messageSequences ?? {}}
          onChange={(messageSequences) => patch({ messageSequences })}
        />
        <NotificationsEditor
          notifications={value.notifications ?? {}}
          sequenceNames={sequenceNames}
          onChange={(notifications) => patch({ notifications })}
        />
        <ReservationAutomationsEditor
          automations={value.reservationAutomations ?? {}}
          sequenceNames={sequenceNames}
          onChange={(reservationAutomations) => patch({ reservationAutomations })}
        />
        <WompiWebhooksEditor
          webhooks={value.webhooks ?? { wompi: {} }}
          sequenceNames={sequenceNames}
          onChange={(webhooks) => patch({ webhooks })}
        />
      </TabsContent>

      <TabsContent value="checkout" className="space-y-4">
        <CheckoutCommerceEditor
          value={value}
          onChange={patch}
        />
      </TabsContent>

      <TabsContent value="actions" className="space-y-4">
        <GlobalActionsEditor
          actions={value.globalActions ?? []}
          onChange={(globalActions) => patch({ globalActions })}
        />
      </TabsContent>

      <TabsContent value="templates" className="space-y-4">
        <TemplatesEditor
          templates={value.templates ?? {}}
          onChange={(templates) => patch({ templates })}
        />
      </TabsContent>

      <TabsContent value="safety" className="space-y-4">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Escalación</CardTitle>
            <CardDescription>Define una acción global para avisar a un humano sin desactivar la conversación</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label>Cuándo escalar</Label>
              <Textarea
                className="min-h-[120px]"
                value={humanEscalationAction?.hint ?? ""}
                onChange={(e) => setHumanEscalationHint(e.target.value)}
              />
            </div>
            <div className="space-y-2">
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
            </div>
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
  availableInboundContacts,
  onChange,
}: {
  value: AgentSettings;
  availableInboundContacts: BusinessInboundContact[];
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
    const name = eventNames.includes("order_created")
      ? `external_event_${eventNames.length + 1}`
      : "order_created";
    patchExternal({
      ...events,
      [name]: {
        enabled: true,
        contactType: "delivery",
        pickupAddress: "",
        attemptTimeoutMinutes: 15,
        attemptCodePrefix: "PED",
        sendMessageSequence: "",
        attemptSentNotificationEvent: "",
        acceptedNotificationEvent: "",
        exhaustedNotificationEvent: "",
        contacts: [],
      },
    }, true);
  };

  const contactLabel = (contact: BusinessInboundContact) => {
    const agent = contact.inboundAgentName ? ` -> ${contact.inboundAgentName}` : "";
    return `${contact.name} (${contact.type}) ${contact.phoneNumber}${agent}`;
  };

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <div>
          <CardTitle className="text-base">Escalaciones externas</CardTitle>
          <CardDescription>Eventos que envian una interaccion a contactos inbound configurados</CardDescription>
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
          const eventContactType = (event.contactType ?? "").trim().toLowerCase();
          const matchingContacts = availableInboundContacts.filter((contact) =>
            !eventContactType || contact.type.toLowerCase() === eventContactType
          );
          const updateEvent = (nextEvent: typeof event, nextName = eventName) => {
            const next = { ...events };
            delete next[eventName];
            next[nextName] = nextEvent;
            patchExternal(next, externalEscalations.enabled ?? true);
          };

          return (
            <div key={eventName} className="space-y-3 rounded-md border p-3">
              <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
                <div className="space-y-1">
                  <Label>Evento</Label>
                  <Input value={eventName} onChange={(e) => updateEvent(event, e.target.value)} />
                </div>
                <div className="space-y-1">
                  <Label>Tipo contacto</Label>
                  <Input
                    placeholder="delivery"
                    value={event.contactType ?? ""}
                    onChange={(e) => updateEvent({ ...event, contactType: e.target.value })}
                  />
                </div>
                <div className="space-y-1">
                  <Label>Secuencia</Label>
                  <Input
                    placeholder="delivery_request"
                    value={event.sendMessageSequence ?? ""}
                    onChange={(e) => updateEvent({ ...event, sendMessageSequence: e.target.value })}
                  />
                </div>
                <div className="space-y-1">
                  <Label>Prefijo</Label>
                  <Input
                    value={event.attemptCodePrefix ?? "EXT"}
                    onChange={(e) => updateEvent({ ...event, attemptCodePrefix: e.target.value })}
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
                <div className="space-y-1 sm:col-span-2 lg:col-span-3">
                  <Label>Direccion de recogida</Label>
                  <Input
                    placeholder="Calle 16 # 9-35, Centro, Valledupar"
                    value={event.pickupAddress ?? ""}
                    onChange={(e) => updateEvent({ ...event, pickupAddress: e.target.value })}
                  />
                </div>
              </div>

              <div className="grid gap-2 sm:grid-cols-3">
                <div className="space-y-1">
                  <Label>Evento intento</Label>
                  <Input
                    placeholder="delivery_requested"
                    value={event.attemptSentNotificationEvent ?? ""}
                    onChange={(e) =>
                      updateEvent({ ...event, attemptSentNotificationEvent: e.target.value })
                    }
                  />
                </div>
                <div className="space-y-1">
                  <Label>Evento aceptado</Label>
                  <Input
                    placeholder="delivery_confirmed"
                    value={event.acceptedNotificationEvent ?? ""}
                    onChange={(e) =>
                      updateEvent({ ...event, acceptedNotificationEvent: e.target.value })
                    }
                  />
                </div>
                <div className="space-y-1">
                  <Label>Evento agotado</Label>
                  <Input
                    placeholder="delivery_unavailable"
                    value={event.exhaustedNotificationEvent ?? ""}
                    onChange={(e) =>
                      updateEvent({ ...event, exhaustedNotificationEvent: e.target.value })
                    }
                  />
                </div>
              </div>

              <div className="space-y-2">
                <div className="flex items-center justify-between">
                  <Label>Contactos inbound</Label>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => {
                      const selectedIds = new Set(
                        contacts
                          .map((contact) => contact.businessInboundContactId)
                          .filter(Boolean)
                      );
                      const firstAvailable = matchingContacts.find(
                        (contact) => !selectedIds.has(contact.businessInboundContactId)
                      );
                      updateEvent({
                        ...event,
                        contacts: [
                          ...contacts,
                          {
                            businessInboundContactId: firstAvailable?.businessInboundContactId ?? "",
                            priority: contacts.length + 1,
                            retryEnabled: true,
                            pickupAddress: "",
                          },
                        ],
                      });
                    }}
                  >
                    <Plus className="mr-1 h-4 w-4" />
                    Contacto
                  </Button>
                </div>

                {contacts.map((contact, index) => (
                  <div key={index} className="grid gap-2 rounded-md border p-3 sm:grid-cols-2 lg:grid-cols-5">
                    <select
                      className="h-9 rounded-md border border-input bg-background px-3 text-sm lg:col-span-2"
                      value={contact.businessInboundContactId ?? ""}
                      onChange={(e) => {
                        const next = [...contacts];
                        next[index] = { ...contact, businessInboundContactId: e.target.value };
                        updateEvent({ ...event, contacts: next });
                      }}
                    >
                      <option value="">Contacto inbound</option>
                      {matchingContacts.map((item) => (
                        <option key={item.businessInboundContactId} value={item.businessInboundContactId}>
                          {contactLabel(item)}
                        </option>
                      ))}
                    </select>
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
                    <label className="flex h-9 items-center gap-2 rounded-md border px-3 text-sm">
                      <Checkbox
                        checked={contact.retryEnabled !== false}
                        onCheckedChange={(checked) => {
                          const next = [...contacts];
                          next[index] = { ...contact, retryEnabled: checked === true };
                          updateEvent({ ...event, contacts: next });
                        }}
                      />
                      Reintenta
                    </label>
                    <div className="flex gap-2">
                      <Input
                        placeholder="direccion override"
                        value={contact.pickupAddress ?? ""}
                        onChange={(e) => {
                          const next = [...contacts];
                          next[index] = { ...contact, pickupAddress: e.target.value };
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

                {matchingContacts.length === 0 && (
                  <p className="text-xs text-muted-foreground">
                    No hay contactos inbound activos para este tipo en el negocio seleccionado.
                  </p>
                )}
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

type MessageSequencesMap = NonNullable<AgentSettings["messageSequences"]>;
type NotificationsMap = NonNullable<AgentSettings["notifications"]>;
type WebhookSettings = NonNullable<AgentSettings["webhooks"]>;
type ReservationAutomations = NonNullable<AgentSettings["reservationAutomations"]>;

const splitLines = (value: string) => value.split("\n").map((s) => s.trim()).filter(Boolean);
const joinLines = (value?: string[] | null) => (value ?? []).join("\n");

function SequenceOptions({ sequenceNames }: { sequenceNames: string[] }) {
  return <datalist id="agent-message-sequences">{sequenceNames.map((name) => <option key={name} value={name} />)}</datalist>;
}

function SequenceNameInput({ value, onChange }: { value?: string | null; onChange: (value: string) => void }) {
  return <Input list="agent-message-sequences" placeholder="sequence_name" value={value ?? ""} onChange={(e) => onChange(e.target.value)} />;
}

function MessageSequencesEditor({ sequences, onChange }: { sequences: MessageSequencesMap; onChange: (sequences: MessageSequencesMap) => void }) {
  return (
    <Card>
      <CardHeader><CardTitle className="text-base">Secuencias de mensajes</CardTitle><CardDescription>Catalogo nombrado para WhatsApp, webhooks y notificaciones</CardDescription></CardHeader>
      <CardContent><JsonEditor value={sequences as unknown as Record<string, unknown>} onChange={(v) => onChange(v as unknown as MessageSequencesMap)} /></CardContent>
    </Card>
  );
}

function NotificationsEditor({ notifications, sequenceNames, onChange }: { notifications: NotificationsMap; sequenceNames: string[]; onChange: (notifications: NotificationsMap) => void }) {
  const names = Object.keys(notifications);
  const add = () => {
    const name = names.includes("event_1") ? `event_${names.length + 1}` : "event_1";
    onChange({ ...notifications, [name]: { enabled: true, recipients: [], sendMessageSequence: "" } });
  };
  const rename = (oldName: string, nextName: string) => {
    const clean = nextName.trim();
    if (!clean || clean === oldName) return;
    const next: NotificationsMap = {};
    Object.entries(notifications).forEach(([key, item]) => { next[key === oldName ? clean : key] = item; });
    onChange(next);
  };
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between"><div><CardTitle className="text-base">Notificaciones por evento</CardTitle><CardDescription>Activa o desactiva cada evento y define a quien se envia</CardDescription></div><Button type="button" variant="outline" size="sm" onClick={add}><Plus className="mr-1 h-4 w-4" />Evento</Button></CardHeader>
      <CardContent className="space-y-3">
        <SequenceOptions sequenceNames={sequenceNames} />
        {names.map((name) => {
          const item = notifications[name] ?? {};
          const update = (nextItem: typeof item) => onChange({ ...notifications, [name]: nextItem });
          return (
            <div key={name} className="grid gap-3 rounded-md border p-3 lg:grid-cols-[1fr_1fr_160px_44px]">
              <div className="space-y-1"><Label>Evento</Label><Input value={name} onBlur={(e) => rename(name, e.target.value)} onChange={(e) => rename(name, e.target.value)} /></div>
              <div className="space-y-1"><Label>Secuencia</Label><SequenceNameInput value={item.sendMessageSequence} onChange={(sendMessageSequence) => update({ ...item, sendMessageSequence })} /></div>
              <label className="flex h-9 items-center gap-2 self-end rounded-md border px-3 text-sm"><Checkbox checked={item.enabled === true} onCheckedChange={(checked) => update({ ...item, enabled: checked === true })} />Activa</label>
              <Button type="button" variant="ghost" size="icon" className="self-end" onClick={() => { const next = { ...notifications }; delete next[name]; onChange(next); }}><Trash2 className="h-4 w-4 text-destructive" /></Button>
              <div className="space-y-1 lg:col-span-4"><Label>Destinatarios WhatsApp (uno por linea)</Label><Textarea value={joinLines(item.recipients)} onChange={(e) => update({ ...item, recipients: splitLines(e.target.value) })} /></div>
            </div>
          );
        })}
        {names.length === 0 && <p className="text-sm text-muted-foreground">No hay notificaciones configuradas.</p>}
      </CardContent>
    </Card>
  );
}

function ReservationAutomationsEditor({ automations, onChange }: { automations: ReservationAutomations; sequenceNames: string[]; onChange: (automations: ReservationAutomations) => void }) {
  return <Card><CardHeader><CardTitle className="text-base">Automatizaciones de reserva</CardTitle></CardHeader><CardContent><JsonEditor value={automations as unknown as Record<string, unknown>} onChange={(v) => onChange(v as unknown as ReservationAutomations)} /></CardContent></Card>;
}

function WompiWebhooksEditor({ webhooks, onChange }: { webhooks: WebhookSettings; sequenceNames: string[]; onChange: (webhooks: WebhookSettings) => void }) {
  return <Card><CardHeader><CardTitle className="text-base">Webhooks Wompi</CardTitle><CardDescription>Outcomes de pago que disparan secuencias</CardDescription></CardHeader><CardContent><JsonEditor value={webhooks as unknown as Record<string, unknown>} onChange={(v) => onChange(v as unknown as WebhookSettings)} /></CardContent></Card>;
}

function CheckoutCommerceEditor({ value, onChange }: { value: AgentSettings; onChange: (partial: Partial<AgentSettings>) => void }) {
  const commerce = value.commerce ?? { enabled: false, provider: "Local" as const };
  const checkout = value.checkout ?? { currency: "COP", modes: {} };
  return <><Card><CardHeader><CardTitle className="text-base">Comercio</CardTitle></CardHeader><CardContent className="grid gap-3 sm:grid-cols-3"><label className="flex h-9 items-center gap-2 rounded-md border px-3 text-sm"><Checkbox checked={commerce.enabled === true} onCheckedChange={(checked) => onChange({ commerce: { ...commerce, enabled: checked === true } })} />Activo</label><select className="h-9 rounded-md border border-input bg-background px-3 text-sm" value={commerce.provider ?? "Local"} onChange={(e) => onChange({ commerce: { ...commerce, provider: e.target.value as "Local" | "Siigo" | "CustomHttp" | "Mantis" } })}><option value="Local">Local</option><option value="Siigo">Siigo</option><option value="CustomHttp">CustomHttp</option><option value="Mantis">Mantis</option></select><Input value={checkout.currency ?? "COP"} onChange={(e) => onChange({ checkout: { ...checkout, currency: e.target.value } })} /></CardContent></Card><Card><CardHeader><CardTitle className="text-base">Checkout</CardTitle><CardDescription>Modos, metodos de pago, shipping y bindings</CardDescription></CardHeader><CardContent><JsonEditor value={checkout as unknown as Record<string, unknown>} onChange={(v) => onChange({ checkout: v as unknown as NonNullable<AgentSettings["checkout"]> })} /></CardContent></Card></>;
}

function GlobalActionsEditor({ actions, onChange }: { actions: GlobalAction[]; onChange: (actions: GlobalAction[]) => void }) {
  return <Card><CardHeader><CardTitle className="text-base">Acciones globales</CardTitle><CardDescription>Acciones transversales disponibles fuera de la etapa activa</CardDescription></CardHeader><CardContent><JsonEditor value={{ actions } as Record<string, unknown>} onChange={(v) => onChange(((v.actions as GlobalAction[]) ?? []))} /></CardContent></Card>;
}

function TemplatesEditor({ templates, onChange }: { templates: Record<string, string>; onChange: (templates: Record<string, string>) => void }) {
  return <Card><CardHeader><CardTitle className="text-base">Templates</CardTitle><CardDescription>Overrides de plantillas del motor</CardDescription></CardHeader><CardContent><JsonEditor value={templates as unknown as Record<string, unknown>} onChange={(v) => onChange(v as Record<string, string>)} /></CardContent></Card>;
}