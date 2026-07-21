"use client";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import {
  DEFAULT_AGENT_SETTINGS,
  type AgentSettings,
  type StageOutcomeHandlerDefinition,
  type StageResponseDefinition,
} from "@/types/agent-settings";

type ResponseOwnerKind = "stage" | "stage_outcome" | "global" | "global_outcome";

interface ResponseOwner {
  key: string;
  kind: ResponseOwnerKind;
  title: string;
  detail: string;
  response?: StageResponseDefinition;
  terminal: boolean;
  apply: (settings: AgentSettings, response: StageResponseDefinition) => AgentSettings;
}

const DEFAULT_FOLLOW_UP = DEFAULT_AGENT_SETTINGS.conversationFollowUp ?? {
  enabled: false,
  delayMinutes: 120,
  guidance: "Retoma brevemente la conversacion desde la pregunta pendiente, sin repetir toda la respuesta.",
  respectOperatingHours: true,
};

function ensureEnabledFollowUpHasContent(
  candidate: NonNullable<AgentSettings["conversationFollowUp"]>
): NonNullable<AgentSettings["conversationFollowUp"]> {
  if (candidate.enabled === true && !candidate.guidance?.trim() && !candidate.fallbackSequence?.trim()) {
    return { ...candidate, guidance: DEFAULT_FOLLOW_UP.guidance };
  }

  return candidate;
}

const isTerminalHandler = (handler: StageOutcomeHandlerDefinition) =>
  (handler.effects ?? []).some((effect) =>
    effect.type === "request.complete" || effect.type === "escalation.human"
  );

function buildResponseOwners(settings: AgentSettings): ResponseOwner[] {
  const owners: ResponseOwner[] = [];

  (settings.flows ?? []).forEach((flow, flowIndex) => {
    flow.stages.forEach((stage, stageIndex) => {
      owners.push({
        key: `flow:${flow.id}:stage:${stage.id}`,
        kind: "stage",
        title: stage.name?.trim() || stage.id,
        detail: `Respuesta predeterminada de la etapa · ${flow.id}`,
        response: stage.response,
        terminal: false,
        apply: (current, response) => ({
          ...current,
          flows: (current.flows ?? []).map((candidateFlow, candidateFlowIndex) =>
            candidateFlowIndex !== flowIndex
              ? candidateFlow
              : {
                  ...candidateFlow,
                  stages: candidateFlow.stages.map((candidateStage, candidateStageIndex) =>
                    candidateStageIndex === stageIndex
                      ? { ...candidateStage, response }
                      : candidateStage
                  ),
                }
          ),
        }),
      });

      (stage.actions ?? []).forEach((action, actionIndex) => {
        Object.entries(action.onOutcome ?? {}).forEach(([outcome, handler]) => {
          if (!handler.response) return;
          owners.push({
            key: `flow:${flow.id}:stage:${stage.id}:action:${action.id}:outcome:${outcome}`,
            kind: "stage_outcome",
            title: outcome,
            detail: `${stage.name?.trim() || stage.id} · ${action.id}`,
            response: handler.response,
            terminal: isTerminalHandler(handler),
            apply: (current, response) => ({
              ...current,
              flows: (current.flows ?? []).map((candidateFlow, candidateFlowIndex) =>
                candidateFlowIndex !== flowIndex
                  ? candidateFlow
                  : {
                      ...candidateFlow,
                      stages: candidateFlow.stages.map((candidateStage, candidateStageIndex) =>
                        candidateStageIndex !== stageIndex
                          ? candidateStage
                          : {
                              ...candidateStage,
                              actions: (candidateStage.actions ?? []).map((candidateAction, candidateActionIndex) =>
                                candidateActionIndex !== actionIndex
                                  ? candidateAction
                                  : {
                                      ...candidateAction,
                                      onOutcome: {
                                        ...candidateAction.onOutcome,
                                        [outcome]: {
                                          ...candidateAction.onOutcome?.[outcome],
                                          response,
                                        },
                                      },
                                    }
                              ),
                            }
                      ),
                    }
              ),
            }),
          });
        });
      });
    });
  });

  (settings.globalActions ?? []).forEach((globalAction, globalActionIndex) => {
    if (globalAction.response) {
      owners.push({
        key: `global:${globalAction.id}`,
        kind: "global",
        title: globalAction.id,
        detail: "Respuesta predeterminada de la accion global",
        response: globalAction.response,
        terminal: false,
        apply: (current, response) => ({
          ...current,
          globalActions: (current.globalActions ?? []).map((candidate, candidateIndex) =>
            candidateIndex === globalActionIndex ? { ...candidate, response } : candidate
          ),
        }),
      });
    }

    (globalAction.actions ?? []).forEach((action, actionIndex) => {
      Object.entries(action.onOutcome ?? {}).forEach(([outcome, handler]) => {
        if (!handler.response) return;
        owners.push({
          key: `global:${globalAction.id}:action:${action.id}:outcome:${outcome}`,
          kind: "global_outcome",
          title: outcome,
          detail: `${globalAction.id} · ${action.id}`,
          response: handler.response,
          terminal: isTerminalHandler(handler),
          apply: (current, response) => ({
            ...current,
            globalActions: (current.globalActions ?? []).map((candidateGlobalAction, candidateGlobalIndex) =>
              candidateGlobalIndex !== globalActionIndex
                ? candidateGlobalAction
                : {
                    ...candidateGlobalAction,
                    actions: (candidateGlobalAction.actions ?? []).map((candidateAction, candidateActionIndex) =>
                      candidateActionIndex !== actionIndex
                        ? candidateAction
                        : {
                            ...candidateAction,
                            onOutcome: {
                              ...candidateAction.onOutcome,
                              [outcome]: {
                                ...candidateAction.onOutcome?.[outcome],
                                response,
                              },
                            },
                          }
                    ),
                  }
            ),
          }),
        });
      });
    });
  });

  return owners;
}

function ResponseOwnerToggle({
  owner,
  onChange,
}: {
  owner: ResponseOwner;
  onChange: (checked: boolean) => void;
}) {
  const checked = owner.response?.awaitCustomerReply === true;
  const hasVisibleResponse = owner.response?.suppressText !== true || Boolean(owner.response.sendMessageSequence);
  const blocked = owner.terminal || !hasVisibleResponse;

  return (
    <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
      <Checkbox
        className="mt-0.5"
        checked={checked}
        disabled={blocked && !checked}
        onCheckedChange={(next) => onChange(next === true)}
      />
      <span className="min-w-0 flex-1">
        <span className="block break-words font-medium">{owner.title}</span>
        <span className="block break-words text-xs text-muted-foreground">{owner.detail}</span>
        {owner.terminal && (
          <span className="mt-1 block text-xs text-amber-700 dark:text-amber-400">
            Cierra la solicitud o escala a una persona; no puede generar retoma.
          </span>
        )}
        {!owner.terminal && !hasVisibleResponse && (
          <span className="mt-1 block text-xs text-amber-700 dark:text-amber-400">
            La respuesta no entrega texto ni secuencia visible al cliente.
          </span>
        )}
      </span>
    </label>
  );
}

export function ConversationFollowUpSettingsEditor({
  settings,
  onChange,
}: {
  settings: AgentSettings;
  onChange: (settings: AgentSettings) => void;
}) {
  const followUp = { ...DEFAULT_FOLLOW_UP, ...settings.conversationFollowUp };
  const owners = buildResponseOwners(settings);
  const stageOwners = owners.filter((owner) => owner.kind === "stage");
  const specificOwners = owners.filter((owner) => owner.kind !== "stage");
  const awaitedCount = owners.filter((owner) => owner.response?.awaitCustomerReply === true).length;
  const sequenceNames = Array.from(new Set([
    ...Object.keys(settings.messageSequences ?? {}),
    ...(followUp.fallbackSequence ? [followUp.fallbackSequence] : []),
  ])).sort((left, right) => left.localeCompare(right));

  const updatePolicy = (partial: Partial<NonNullable<AgentSettings["conversationFollowUp"]>>) => {
    const conversationFollowUp = ensureEnabledFollowUpHasContent({ ...followUp, ...partial });
    onChange({
      ...settings,
      conversationFollowUp,
    });
  };

  const updateOwner = (owner: ResponseOwner, checked: boolean) => {
    let next = owner.apply(settings, {
      ...owner.response,
      awaitCustomerReply: checked ? true : undefined,
    });
    if (checked) {
      next = {
        ...next,
        conversationFollowUp: ensureEnabledFollowUpHasContent({
          ...DEFAULT_FOLLOW_UP,
          ...next.conversationFollowUp,
          enabled: true,
        }),
      };
    }
    onChange(next);
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Retomas de conversacion</CardTitle>
          <CardDescription>
            Envia una sola retoma contextual cuando el ultimo mensaje entregado por el bot dejo una respuesta real pendiente.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <Checkbox
              className="mt-0.5"
              checked={followUp.enabled === true}
              disabled={followUp.enabled === true && awaitedCount > 0}
              onCheckedChange={(checked) => updatePolicy({ enabled: checked === true })}
            />
            <span>
              <span className="block font-medium">Activar retomas</span>
              <span className="block text-xs text-muted-foreground">
                {awaitedCount > 0
                  ? `Hay ${awaitedCount} respuesta${awaitedCount === 1 ? "" : "s"} marcada${awaitedCount === 1 ? "" : "s"}. Desmarcala${awaitedCount === 1 ? "" : "s"} antes de desactivar la politica.`
                  : "La retoma solo se programa para respuestas marcadas debajo."}
              </span>
            </span>
          </label>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="conversation-follow-up-delay">Espera antes de retomar (minutos)</Label>
              <Input
                id="conversation-follow-up-delay"
                type="number"
                min={1}
                max={43200}
                value={followUp.delayMinutes ?? 120}
                onChange={(event) => {
                  const delayMinutes = event.currentTarget.valueAsNumber;
                  if (Number.isFinite(delayMinutes)) updatePolicy({ delayMinutes });
                }}
              />
              <p className="text-xs text-muted-foreground">Entre 1 minuto y 30 dias.</p>
            </div>
            <div className="space-y-2">
              <Label>Secuencia de respaldo (opcional)</Label>
              <Select
                value={followUp.fallbackSequence?.trim() || "none"}
                onValueChange={(fallbackSequence) =>
                  updatePolicy({ fallbackSequence: fallbackSequence === "none" ? undefined : fallbackSequence })
                }
              >
                <SelectTrigger><SelectValue placeholder="Sin mensaje generico" /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Sin secuencia generica</SelectItem>
                  {sequenceNames.map((sequence) => (
                    <SelectItem key={sequence} value={sequence}>{sequence}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">Si se omite, el motor redacta desde el contexto vigente.</p>
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="conversation-follow-up-guidance">Como debe retomar</Label>
            <Textarea
              id="conversation-follow-up-guidance"
              className="min-h-28"
              value={followUp.guidance ?? ""}
              onChange={(event) => updatePolicy({ guidance: event.target.value })}
              placeholder="Ejemplo: Retoma brevemente la pregunta pendiente sin repetir el catalogo ni presionar al cliente."
            />
            <p className="text-xs text-muted-foreground">
              Esta guia redacta el mensaje futuro; no decide operaciones ni modifica el pedido.
            </p>
          </div>

          <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
            <Checkbox
              className="mt-0.5"
              checked={followUp.respectOperatingHours !== false}
              onCheckedChange={(checked) => updatePolicy({ respectOperatingHours: checked === true })}
            />
            <span>
              <span className="block font-medium">Respetar horarios de atencion</span>
              <span className="block text-xs text-muted-foreground">
                Si el bot tiene control horario activo, difiere la retoma hasta la siguiente apertura.
              </span>
            </span>
          </label>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Respuestas que esperan al cliente</CardTitle>
          <CardDescription>
            La marca se guarda como <code>response.awaitCustomerReply</code>. Una respuesta nueva despues de que el cliente conteste crea una espera independiente.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="space-y-2">
            <div>
              <p className="text-sm font-medium">Etapas</p>
              <p className="text-xs text-muted-foreground">Usa la respuesta predeterminada del checkpoint activo.</p>
            </div>
            {stageOwners.length > 0 ? (
              <div className="grid gap-2 lg:grid-cols-2">
                {stageOwners.map((owner) => (
                  <ResponseOwnerToggle key={owner.key} owner={owner} onChange={(checked) => updateOwner(owner, checked)} />
                ))}
              </div>
            ) : (
              <p className="rounded-lg border border-dashed p-3 text-sm text-muted-foreground">No hay etapas configuradas.</p>
            )}
          </div>

          <details className="rounded-lg border p-3" open={specificOwners.some((owner) => owner.response?.awaitCustomerReply)}>
            <summary className="cursor-pointer text-sm font-medium">
              Outcomes y acciones globales ({specificOwners.length})
            </summary>
            <p className="mt-1 text-xs text-muted-foreground">
              Marca respuestas especificas que reemplazan la respuesta de su etapa, como candidatos, carrito o checkout.
            </p>
            <div className="mt-3 grid gap-2 lg:grid-cols-2">
              {specificOwners.map((owner) => (
                <ResponseOwnerToggle key={owner.key} owner={owner} onChange={(checked) => updateOwner(owner, checked)} />
              ))}
              {specificOwners.length === 0 && (
                <p className="text-sm text-muted-foreground">No hay respuestas especificas configuradas.</p>
              )}
            </div>
          </details>
        </CardContent>
      </Card>
    </div>
  );
}
