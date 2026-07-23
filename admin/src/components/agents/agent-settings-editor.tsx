"use client";

import { useCallback, useMemo, useState } from "react";
import { Eye, Plus, Route, Trash2 } from "lucide-react";

import { StageTechnicalEditor } from "@/components/agents/stage-technical-editor";
import { CheckoutCommerceEditor, GlobalActionsEditor, MessageSequencesEditor, ReservationAutomationsEditor, StringMapEditor, WompiWebhooksEditor } from "@/components/agents/agent-message-commerce-editors";
import { ConversationFollowUpSettingsEditor } from "@/components/agents/conversation-follow-up-settings";
import { ConfigurationSelect } from "@/components/agents/configuration-controls";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Textarea } from "@/components/ui/textarea";
import type { BusinessInboundContact } from "@/types/entities";
import type {
  AgentFlowDefinition,
  AgentFlowStage,
  AgentSettings,
  FactSchemaEntry,
} from "@/types/agent-settings";

type GlobalAction = NonNullable<AgentSettings["globalActions"]>[number];
const HUMAN_ESCALATION_GLOBAL_ACTION_ID = "human_escalation";
const HUMAN_ESCALATION_OPERATION = "escalation.request_human";
const HUMAN_ESCALATION_GOAL = "Notificar al equipo humano sin desactivar el bot.";
const isHumanEscalationAction = (action: GlobalAction) =>
  action.id === HUMAN_ESCALATION_GLOBAL_ACTION_ID;

export type AgentEditorSection =
  | "identity"
  | "policies"
  | "flow"
  | "followUp"
  | "facts"
  | "external"
  | "messages"
  | "checkout"
  | "actions"
  | "templates"
  | "safety"
;

interface AgentSettingsEditorProps {
  value: AgentSettings;
  onChange: (next: AgentSettings) => void;
  availableInboundContacts?: BusinessInboundContact[];
  /** Modo wizard: muestra solo una sección sin pestañas. */
  section?: AgentEditorSection;
}

export function AgentSettingsEditor({ value, onChange, availableInboundContacts = [], section }: AgentSettingsEditorProps) {
  const flows = value.flows ?? [];
  const factKeys = useMemo(
    () => (value.factSchema ?? []).filter((fact) => (fact.source ?? "user") === "user").map((fact) => fact.key),
    [value.factSchema]
  );

  const patch = useCallback(
    (partial: Partial<AgentSettings>) => onChange({ ...value, ...partial }),
    [value, onChange]
  );

  const tabModeProps = section ? { value: section } : { defaultValue: "identity" };
  const sequenceNames = useMemo(() => Object.keys(value.messageSequences ?? {}), [value.messageSequences]);
  const humanEscalationAction = useMemo(
    () => (value.globalActions ?? []).find(isHumanEscalationAction),
    [value.globalActions]
  );
  const setHumanEscalationGuidance = useCallback(
    (conversationGuidance: string) => {
      const actions = value.globalActions ?? [];
      const index = actions.findIndex(isHumanEscalationAction);
      const current = index >= 0 ? actions[index] : undefined;
      const nextAction: GlobalAction = {
        id: current?.id || HUMAN_ESCALATION_GLOBAL_ACTION_ID,
        priority: current?.priority ?? 1000,
        goal: current?.goal?.trim() ? current.goal : HUMAN_ESCALATION_GOAL,
        signal: current?.signal ?? {
          type: "human_requested",
          description: "El cliente pide hablar con una persona.",
          valueSchema: { type: "string" },
        },
        actions: current?.actions?.length
          ? current.actions
          : [{
              id: "notify_human",
              operation: HUMAN_ESCALATION_OPERATION,
              trigger: "on_signal",
              signal: "human_requested",
              arguments: {
                reason: "{{signal.human_requested.value}}",
                last_user_message: "{{user.message}}",
              },
            }],
        response: current?.response ?? { guidance: "Confirma que el equipo humano fue notificado." },
        ...current,
        conversationGuidance,
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
        <TabsTrigger value="followUp">Retomas</TabsTrigger>
        <TabsTrigger value="facts">Datos (facts)</TabsTrigger>
        <TabsTrigger value="external">Externos</TabsTrigger>
        <TabsTrigger value="messages">Mensajes</TabsTrigger>
        <TabsTrigger value="checkout">Checkout</TabsTrigger>
        <TabsTrigger value="actions">Acciones</TabsTrigger>
        <TabsTrigger value="templates">Templates</TabsTrigger>
        <TabsTrigger value="safety">Seguridad</TabsTrigger>
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
        <FlowEditor flows={flows} userFactKeys={factKeys} onChange={(nextFlows) => patch({ flows: nextFlows })} />
      </TabsContent>

      <TabsContent value="followUp" className="space-y-4">
        <ConversationFollowUpSettingsEditor settings={value} onChange={onChange} />
      </TabsContent>

      <TabsContent value="facts">
        <FactSchemaEditor
          entries={value.factSchema ?? []}
          onChange={(factSchema) => patch({ factSchema })}
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
                value={humanEscalationAction?.conversationGuidance ?? ""}
                onChange={(e) => setHumanEscalationGuidance(e.target.value)}
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
          </CardContent>
        </Card>
      </TabsContent>
      </Tabs>
  );
}

function FlowEditor({ flows, userFactKeys, onChange }: { flows: AgentFlowDefinition[]; userFactKeys: string[]; onChange: (flows: AgentFlowDefinition[]) => void }) {
  const updateFlow = (flowIndex: number, next: AgentFlowDefinition) => onChange(flows.map((flow, index) => index === flowIndex ? next : flow));
  const addFlow = () => onChange([...flows, { id: `flow_${flows.length + 1}`, type: flows.length === 0 ? "primary" : "secondary", routingGuidance: "", stages: [] }]);
  const removeFlow = (flowIndex: number) => onChange(flows.filter((_, index) => index !== flowIndex));
  const addStage = (flowIndex: number) => {
    const flow = flows[flowIndex];
    const stageNumber = flow.stages.length + 1;
    updateFlow(flowIndex, { ...flow, stages: [...flow.stages, { id: `stage_${stageNumber}`, name: `Etapa ${stageNumber}`, goal: "", collect: [], signals: [], actions: [], transitions: [] }] });
  };
  const updateStage = (flowIndex: number, stageIndex: number, next: AgentFlowStage) => {
    const flow = flows[flowIndex];
    updateFlow(flowIndex, { ...flow, stages: flow.stages.map((stage, index) => index === stageIndex ? next : stage) });
  };

  return <Card>
    <CardHeader className="flex flex-row items-start justify-between gap-4">
      <div><CardTitle className="flex items-center gap-2 text-base"><Route className="h-4 w-4 text-primary" />Recorrido conversacional</CardTitle><CardDescription>Organiza checkpoints durables. Las señales, operaciones y transiciones técnicas se conservan dentro de cada etapa.</CardDescription></div>
      <Button type="button" size="sm" variant="outline" onClick={addFlow}><Plus className="mr-1 h-4 w-4" />Flujo</Button>
    </CardHeader>
    <CardContent className="space-y-5">
      {flows.map((flow, flowIndex) => <section key={`${flow.id}-${flowIndex}`} className="overflow-hidden rounded-xl border bg-card shadow-sm">
        <div className="grid gap-3 border-b bg-muted/30 p-4 md:grid-cols-[minmax(160px,1fr)_160px_minmax(220px,2fr)_40px]">
          <div className="space-y-1"><Label>Identificador del flujo</Label><Input value={flow.id} onChange={(event) => updateFlow(flowIndex, { ...flow, id: event.target.value })} /></div>
          <div className="space-y-1"><Label>Tipo</Label><ConfigurationSelect value={flow.type ?? "secondary"} onChange={(next) => updateFlow(flowIndex, { ...flow, type: next as "primary" | "secondary" })} options={[{ value: "primary", label: "Principal" }, { value: "secondary", label: "Secundario" }]} /></div>
          <div className="space-y-1"><Label>Cuándo usarlo</Label><Input value={flow.routingGuidance ?? ""} onChange={(event) => updateFlow(flowIndex, { ...flow, routingGuidance: event.target.value })} placeholder="Ej. Atender una compra nueva" /></div>
          <Button type="button" variant="ghost" size="icon" className="self-end text-muted-foreground hover:text-destructive" onClick={() => removeFlow(flowIndex)} aria-label="Eliminar flujo"><Trash2 className="h-4 w-4" /></Button>
        </div>
        <div className="space-y-3 p-4">
          {flow.stages.map((stage, stageIndex) => {
            return <details key={`${stage.id}-${stageIndex}`} className="group rounded-xl border bg-background">
              <summary className="flex cursor-pointer list-none items-center gap-3 px-4 py-3 marker:hidden">
                <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-primary/10 text-sm font-semibold text-primary">{stageIndex + 1}</span>
                <div className="min-w-0"><p className="truncate font-medium">{stage.name || stage.id || `Etapa ${stageIndex + 1}`}</p><p className="text-xs text-muted-foreground">{stage.collect?.length ?? 0} datos · {stage.actions?.length ?? 0} acciones · {stage.transitions?.length ?? 0} transiciones</p></div>
                <span className="ml-auto text-xs text-muted-foreground group-open:hidden">Editar</span><span className="ml-auto hidden text-xs text-muted-foreground group-open:inline">Cerrar</span>
              </summary>
              <div className="space-y-4 border-t p-4">
                <div className="grid gap-3 sm:grid-cols-2"><div className="space-y-1"><Label>Nombre visible</Label><Input value={stage.name ?? ""} onChange={(event) => updateStage(flowIndex, stageIndex, { ...stage, name: event.target.value })} /></div><div className="space-y-1"><Label>Identificador</Label><Input value={stage.id} onChange={(event) => updateStage(flowIndex, stageIndex, { ...stage, id: event.target.value })} /></div></div>
                <div className="space-y-1"><Label>Objetivo de esta etapa</Label><Textarea className="min-h-20" value={stage.goal ?? ""} onChange={(event) => updateStage(flowIndex, stageIndex, { ...stage, goal: event.target.value })} placeholder="Qué debe conseguir el agente antes de avanzar" /></div>
                <div className="space-y-2"><Label>Datos del usuario que debe reunir</Label><div className="flex flex-wrap gap-2">{userFactKeys.map((factKey) => { const selected = (stage.collect ?? []).includes(factKey); return <label key={factKey} className={`flex cursor-pointer items-center gap-2 rounded-full border px-3 py-1.5 text-sm ${selected ? "border-primary bg-primary/10 text-primary" : "bg-background"}`}><Checkbox checked={selected} onCheckedChange={(checked) => { const collect = stage.collect ?? []; updateStage(flowIndex, stageIndex, { ...stage, collect: checked === true ? [...collect, factKey] : collect.filter((key) => key !== factKey) }); }} />{factKey}</label>; })}{userFactKeys.length === 0 && <span className="text-sm text-muted-foreground">Crea primero los datos del usuario en el paso Datos.</span>}</div></div>
                <div className="space-y-1"><Label>Guía conversacional</Label><Textarea value={stage.conversationGuidance ?? ""} onChange={(event) => updateStage(flowIndex, stageIndex, { ...stage, conversationGuidance: event.target.value })} placeholder="Cómo acompañar al cliente en este checkpoint" /></div>
                <StageTechnicalEditor stage={stage} onChange={(nextStage) => updateStage(flowIndex, stageIndex, nextStage)} />
                <div className="flex justify-end"><Button type="button" variant="ghost" size="sm" className="text-destructive" onClick={() => updateFlow(flowIndex, { ...flow, stages: flow.stages.filter((_, index) => index !== stageIndex) })}><Trash2 className="mr-1 h-4 w-4" />Eliminar etapa</Button></div>
              </div>
            </details>;
          })}
          {flow.stages.length === 0 && <p className="rounded-xl border border-dashed p-6 text-center text-sm text-muted-foreground">Este flujo todavía no tiene etapas.</p>}
          <Button type="button" variant="outline" size="sm" onClick={() => addStage(flowIndex)}><Plus className="mr-1 h-4 w-4" />Añadir etapa</Button>
        </div>
      </section>)}
      {flows.length === 0 && <p className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay flujos configurados. Crea el flujo principal para empezar.</p>}
    </CardContent>
  </Card>;
}

function FactSchemaEditor({ entries, onChange }: { entries: FactSchemaEntry[]; onChange: (entries: FactSchemaEntry[]) => void }) {
  const visible = entries.map((entry, index) => ({ entry, index })).filter(({ entry }) => (entry.source ?? "user") === "user");
  const update = (sourceIndex: number, partial: Partial<FactSchemaEntry>) => onChange(entries.map((entry, index) => index === sourceIndex ? { ...entry, ...partial, source: "user" } : entry));
  const updateOption = (sourceIndex: number, optionIndex: number, partial: Partial<NonNullable<FactSchemaEntry["options"]>[number]>) => {
    const options = entries[sourceIndex].options ?? [];
    update(sourceIndex, { options: options.map((option, index) => index === optionIndex ? { ...option, ...partial } : option) });
  };
  const addOption = (sourceIndex: number) => {
    const options = entries[sourceIndex].options ?? [];
    update(sourceIndex, { options: [...options, { selector: "", label: "", value: "" }] });
  };
  const removeOption = (sourceIndex: number, optionIndex: number) => {
    const options = entries[sourceIndex].options ?? [];
    update(sourceIndex, { options: options.filter((_, index) => index !== optionIndex) });
  };
  const remove = (sourceIndex: number) => onChange(entries.filter((_, index) => index !== sourceIndex));
  const add = () => onChange([...entries, { key: `customer_fact_${visible.length + 1}`, label: "", type: "string", source: "user", scope: "request" }]);
  return <Card>
    <CardHeader className="flex flex-row items-start justify-between gap-4"><div><CardTitle className="text-base">Datos del usuario</CardTitle><CardDescription>Información que el cliente puede proporcionar durante la conversación.</CardDescription></div><Button type="button" size="sm" variant="outline" onClick={add}><Plus className="mr-1 h-4 w-4" />Dato</Button></CardHeader>
    <CardContent className="space-y-3">
      {visible.map(({ entry, index }) => <div key={`${entry.key}-${index}`} className="grid gap-3 rounded-xl border bg-card p-4 md:grid-cols-[minmax(150px,1fr)_minmax(180px,1.3fr)_140px_auto]">
        <div className="space-y-1"><Label>Identificador</Label><Input value={entry.key} onChange={(event) => update(index, { key: event.target.value })} /></div>
        <div className="space-y-1"><Label>Nombre para el equipo</Label><Input value={entry.label} onChange={(event) => update(index, { label: event.target.value })} placeholder="Ej. Nombre completo" /></div>
        <div className="space-y-1"><Label>Tipo de dato</Label><ConfigurationSelect value={entry.type} onChange={(type) => update(index, { type })} options={[{ value: "string", label: "Texto" }, { value: "number", label: "Número" }, { value: "boolean", label: "Sí / No" }, { value: "date", label: "Fecha" }, { value: "datetime", label: "Fecha y hora" }]} /></div>
        <Button type="button" variant="ghost" size="icon" className="self-end text-muted-foreground hover:text-destructive" onClick={() => remove(index)} aria-label="Eliminar dato"><Trash2 className="h-4 w-4" /></Button>
        <div className="space-y-1 md:col-span-2"><Label>Ayuda para reconocerlo</Label><Input value={entry.extractionGuidance ?? ""} onChange={(event) => update(index, { extractionGuidance: event.target.value })} placeholder="Qué expresiones o formato puede usar el cliente" /></div>
        <label className="flex h-10 items-center gap-2 self-end rounded-md border px-3 text-sm"><Checkbox checked={entry.required === true} onCheckedChange={(checked) => update(index, { required: checked === true })} />Requerido</label>
        <label className="flex h-10 items-center gap-2 self-end rounded-md border px-3 text-sm"><Checkbox checked={entry.showInCollectedInfo !== false} onCheckedChange={(checked) => update(index, { showInCollectedInfo: checked === true })} />Mostrar</label>
        {entry.type === "string" && <div className="space-y-3 rounded-lg border bg-muted/10 p-3 md:col-span-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div><Label>Opciones permitidas</Label><p className="text-xs text-muted-foreground">Define la letra que puede responder el cliente, el nombre que ve y el valor can&oacute;nico que guarda el motor.</p></div>
            <Button type="button" size="sm" variant="outline" onClick={() => addOption(index)}><Plus className="mr-1 h-4 w-4" />Opci&oacute;n</Button>
          </div>
          {(entry.options ?? []).map((option, optionIndex) => <div key={`${entry.key}-option-${optionIndex}`} className="grid gap-2 sm:grid-cols-[100px_minmax(160px,1fr)_minmax(160px,1fr)_40px]">
            <div className="space-y-1"><Label className="text-xs">Letra</Label><Input value={option.selector ?? ""} onChange={(event) => updateOption(index, optionIndex, { selector: event.target.value })} placeholder="A" /></div>
            <div className="space-y-1"><Label className="text-xs">Nombre visible</Label><Input value={option.label} onChange={(event) => updateOption(index, optionIndex, { label: event.target.value })} placeholder="Tienda o minimercado" /></div>
            <div className="space-y-1"><Label className="text-xs">Valor guardado</Label><Input value={option.value} onChange={(event) => updateOption(index, optionIndex, { value: event.target.value })} placeholder="TiendaMinimercado" /></div>
            <Button type="button" variant="ghost" size="icon" className="self-end text-muted-foreground hover:text-destructive" onClick={() => removeOption(index, optionIndex)} aria-label={`Eliminar opcion ${option.label || optionIndex + 1}`}><Trash2 className="h-4 w-4" /></Button>
          </div>)}
          {(entry.options ?? []).length === 0 && <p className="rounded-md border border-dashed p-3 text-center text-xs text-muted-foreground">Este dato acepta texto libre. A&ntilde;ade opciones para limitarlo a valores can&oacute;nicos.</p>}
        </div>}
      </div>)}
      {visible.length === 0 && <p className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay datos del usuario configurados.</p>}
    </CardContent>
  </Card>;
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
        outcomeEvents: {},
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

              <div className="space-y-2">
                <Label>Eventos por outcome</Label>
                <StringMapEditor value={event.outcomeEvents ?? {}} onChange={(outcomeEvents) => updateEvent({ ...event, outcomeEvents })} keyLabel="Outcome" valueLabel="Evento" />
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
                    <ConfigurationSelect
                      className="h-9 lg:col-span-2"
                      value={contact.businessInboundContactId ?? ""}
                      allowEmpty
                      emptyLabel="Contacto inbound"
                      onChange={(businessInboundContactId) => {
                        const next = [...contacts];
                        next[index] = { ...contact, businessInboundContactId };
                        updateEvent({ ...event, contacts: next });
                      }}
                      options={matchingContacts.map((item) => ({
                        value: item.businessInboundContactId,
                        label: contactLabel(item),
                      }))}
                    />
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

type NotificationsMap = NonNullable<AgentSettings["notifications"]>;

const splitLines = (value: string) => value.split("\n").map((s) => s.trim()).filter(Boolean);
const joinLines = (value?: string[] | null) => (value ?? []).join("\n");

function SequenceOptions({ sequenceNames }: { sequenceNames: string[] }) {
  return <datalist id="agent-message-sequences">{sequenceNames.map((name) => <option key={name} value={name} />)}</datalist>;
}

function SequenceNameInput({ value, onChange }: { value?: string | null; onChange: (value: string) => void }) {
  return <Input list="agent-message-sequences" placeholder="sequence_name" value={value ?? ""} onChange={(e) => onChange(e.target.value)} />;
}

function NotificationsEditor({ notifications, sequenceNames, onChange }: { notifications: NotificationsMap; sequenceNames: string[]; onChange: (notifications: NotificationsMap) => void }) {
  const names = Object.keys(notifications);
  const add = () => {
    const name = names.includes("event_1") ? `event_${names.length + 1}` : "event_1";
    onChange({ ...notifications, [name]: { enabled: true, deliveries: [{ id: "delivery_1", enabled: true, recipients: [], sendMessageSequence: "" }] } });
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
          const deliveries = item.deliveries ?? [];
          const update = (nextItem: typeof item) => onChange({ ...notifications, [name]: nextItem });
          return (
            <div key={name} className="grid gap-3 rounded-md border p-3 lg:grid-cols-[1fr_1fr_160px_44px]">
              <div className="space-y-1"><Label>Evento</Label><Input value={name} onBlur={(e) => rename(name, e.target.value)} onChange={(e) => rename(name, e.target.value)} /></div>
              <div className="space-y-1"><Label>Entregas</Label><div className="flex h-9 items-center rounded-md border px-3 text-sm">{deliveries.length} configuradas</div></div>
              <label className="flex h-9 items-center gap-2 self-end rounded-md border px-3 text-sm"><Checkbox checked={item.enabled === true} onCheckedChange={(checked) => update({ ...item, enabled: checked === true, deliveries: checked === true && deliveries.length === 0 ? [{ id: "delivery_1", enabled: true, recipients: [], sendMessageSequence: "" }] : deliveries })} />Activa</label>
              <Button type="button" variant="ghost" size="icon" className="self-end" onClick={() => { const next = { ...notifications }; delete next[name]; onChange(next); }}><Trash2 className="h-4 w-4 text-destructive" /></Button>
              <div className="space-y-3 lg:col-span-4">
                {deliveries.map((delivery, index) => {
                  const updateDelivery = (nextDelivery: typeof delivery) => update({ ...item, deliveries: deliveries.map((current, currentIndex) => currentIndex === index ? nextDelivery : current) });
                  return <div key={`${delivery.id}-${index}`} className="grid gap-3 rounded-md bg-muted/30 p-3 lg:grid-cols-[1fr_1fr_140px_44px]">
                    <div className="space-y-1"><Label>Entrega</Label><Input value={delivery.id} onChange={(e) => updateDelivery({ ...delivery, id: e.target.value })} /></div>
                    <div className="space-y-1"><Label>Secuencia</Label><SequenceNameInput value={delivery.sendMessageSequence} onChange={(sendMessageSequence) => updateDelivery({ ...delivery, sendMessageSequence })} /></div>
                    <label className="flex h-9 items-center gap-2 self-end rounded-md border bg-background px-3 text-sm"><Checkbox checked={delivery.enabled !== false} onCheckedChange={(checked) => updateDelivery({ ...delivery, enabled: checked === true })} />Activa</label>
                    <Button type="button" variant="ghost" size="icon" className="self-end" onClick={() => update({ ...item, deliveries: deliveries.filter((_, currentIndex) => currentIndex !== index) })}><Trash2 className="h-4 w-4 text-destructive" /></Button>
                    <div className="space-y-1 lg:col-span-4"><Label>Destinatarios (uno por linea; source:conversation envia al cliente)</Label><Textarea value={joinLines(delivery.recipients)} onChange={(e) => updateDelivery({ ...delivery, recipients: splitLines(e.target.value) })} /></div>
                  </div>;
                })}
                <Button type="button" variant="outline" size="sm" onClick={() => update({ ...item, deliveries: [...deliveries, { id: `delivery_${deliveries.length + 1}`, enabled: true, recipients: [], sendMessageSequence: "" }] })}><Plus className="mr-1 h-4 w-4" />Entrega</Button>
              </div>
            </div>
          );
        })}
        {names.length === 0 && <p className="text-sm text-muted-foreground">No hay notificaciones configuradas.</p>}
      </CardContent>
    </Card>
  );
}

function TemplatesEditor({ templates, onChange }: { templates: Record<string, string>; onChange: (templates: Record<string, string>) => void }) {
  const names = Object.keys(templates);
  const add = () => { let index = names.length + 1; let name = `template_${index}`; while (name in templates) name = `template_${++index}`; onChange({ ...templates, [name]: "" }); };
  const update = (name: string, body: string) => onChange({ ...templates, [name]: body });
  const rename = (oldName: string, nextName: string) => { const clean = nextName.trim(); if (!clean || clean === oldName) return; const next: Record<string, string> = {}; Object.entries(templates).forEach(([name, body]) => { next[name === oldName ? clean : name] = body; }); onChange(next); };
  const remove = (name: string) => { const next = { ...templates }; delete next[name]; onChange(next); };
  return <Card>
    <CardHeader className="flex flex-row items-start justify-between gap-4"><div><CardTitle className="text-base">Templates del motor</CardTitle><CardDescription>Edita el contenido y revisa cómo se verá; las variables se resaltan en la vista previa.</CardDescription></div><Button type="button" size="sm" variant="outline" onClick={add}><Plus className="mr-1 h-4 w-4" />Template</Button></CardHeader>
    <CardContent className="space-y-4">
      {names.map((name) => <section key={name} className="overflow-hidden rounded-xl border bg-card shadow-sm">
        <div className="flex items-center gap-2 border-b bg-muted/30 p-3"><Input className="max-w-md font-medium" defaultValue={name} onBlur={(event) => rename(name, event.target.value)} /><Button type="button" variant="ghost" size="icon" className="ml-auto text-muted-foreground hover:text-destructive" onClick={() => remove(name)} aria-label="Eliminar template"><Trash2 className="h-4 w-4" /></Button></div>
        <div className="grid lg:grid-cols-2"><div className="space-y-2 border-b p-4 lg:border-b-0 lg:border-r"><Label>Contenido</Label><Textarea className="min-h-44 resize-y font-mono text-sm" value={templates[name]} onChange={(event) => update(name, event.target.value)} /></div><div className="space-y-2 bg-muted/10 p-4"><Label className="flex items-center gap-2"><Eye className="h-4 w-4 text-primary" />Vista previa renderizada</Label><TemplatePreview body={templates[name]} /></div></div>
      </section>)}
      {names.length === 0 && <p className="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay templates personalizados.</p>}
    </CardContent>
  </Card>;
}

function TemplatePreview({ body }: { body: string }) {
  if (!body.trim()) return <div className="grid min-h-44 place-items-center rounded-xl border border-dashed text-sm text-muted-foreground">Escribe el contenido para ver la vista previa.</div>;
  const parts = body.split(/(\{\{[^}]+\}\})/g);
  return <div className="min-h-44 whitespace-pre-wrap rounded-2xl rounded-tl-sm border bg-background p-4 text-sm leading-6 shadow-sm">{parts.map((part, index) => /^\{\{[^}]+\}\}$/.test(part) ? <span key={index} className="mx-0.5 inline-flex rounded bg-primary/10 px-1.5 py-0.5 font-mono text-xs text-primary">{part}</span> : <span key={index}>{part}</span>)}</div>;
}
