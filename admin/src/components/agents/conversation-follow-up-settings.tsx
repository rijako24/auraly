"use client";

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { DEFAULT_AGENT_SETTINGS, type AgentSettings } from "@/types/agent-settings";

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

export function ConversationFollowUpSettingsEditor({
  settings,
  onChange,
}: {
  settings: AgentSettings;
  onChange: (settings: AgentSettings) => void;
}) {
  const followUp = { ...DEFAULT_FOLLOW_UP, ...settings.conversationFollowUp };
  const stages = (settings.flows ?? []).flatMap((flow, flowIndex) =>
    flow.stages.map((stage, stageIndex) => ({ flow, flowIndex, stage, stageIndex }))
  );
  const awaitedCount = stages.filter(({ stage }) => stage.awaitCustomerReply === true).length;
  const sequenceNames = Array.from(new Set([
    ...Object.keys(settings.messageSequences ?? {}),
    ...(followUp.fallbackSequence ? [followUp.fallbackSequence] : []),
  ])).sort((left, right) => left.localeCompare(right));

  const updatePolicy = (partial: Partial<NonNullable<AgentSettings["conversationFollowUp"]>>) => {
    onChange({
      ...settings,
      conversationFollowUp: ensureEnabledFollowUpHasContent({ ...followUp, ...partial }),
    });
  };

  const updateStage = (flowIndex: number, stageIndex: number, checked: boolean) => {
    const flows = (settings.flows ?? []).map((flow, candidateFlowIndex) =>
      candidateFlowIndex !== flowIndex
        ? flow
        : {
            ...flow,
            stages: flow.stages.map((stage, candidateStageIndex) =>
              candidateStageIndex === stageIndex
                ? { ...stage, awaitCustomerReply: checked ? true : undefined }
                : stage
            ),
          }
    );

    onChange({
      ...settings,
      flows,
      conversationFollowUp: checked
        ? ensureEnabledFollowUpHasContent({
            ...DEFAULT_FOLLOW_UP,
            ...settings.conversationFollowUp,
            enabled: true,
          })
        : settings.conversationFollowUp,
    });
  };

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Politica general de retomas</CardTitle>
          <CardDescription>
            Define cuando y como se envia una sola retoma. Cada etapa decide si queda esperando al cliente.
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
                  ? "Hay etapas configuradas para retomar. Desmarcalas antes de desactivar la politica."
                  : "La retoma se programa solamente en las etapas seleccionadas."}
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
          <CardTitle className="text-base">Etapas que esperan al cliente</CardTitle>
          <CardDescription>
            La seleccion se guarda una sola vez en el stage y se aplica a sus respuestas, outcomes y acciones globales.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {(settings.flows ?? []).map((flow, flowIndex) => (
            <section key={flow.id + "-" + flowIndex} className="space-y-2">
              <div>
                <p className="text-sm font-medium">{flow.id}</p>
                <p className="text-xs text-muted-foreground">Flujo {flow.type ?? "secondary"}</p>
              </div>
              <div className="grid gap-2 lg:grid-cols-2">
                {flow.stages.map((stage, stageIndex) => (
                  <label key={stage.id + "-" + stageIndex} className="flex items-start gap-3 rounded-lg border p-3 text-sm">
                    <Checkbox
                      className="mt-0.5"
                      checked={stage.awaitCustomerReply === true}
                      onCheckedChange={(checked) => updateStage(flowIndex, stageIndex, checked === true)}
                    />
                    <span className="min-w-0">
                      <span className="block break-words font-medium">{stage.name?.trim() || stage.id}</span>
                      <span className="block break-words text-xs text-muted-foreground">{stage.id}</span>
                    </span>
                  </label>
                ))}
              </div>
            </section>
          ))}
          {stages.length === 0 && (
            <p className="rounded-lg border border-dashed p-3 text-sm text-muted-foreground">
              No hay etapas configuradas.
            </p>
          )}
          <p className="text-xs text-muted-foreground">
            Las solicitudes completadas, escaladas o sin mensaje visible nunca programan una retoma.
          </p>
        </CardContent>
      </Card>
    </div>
  );
}
