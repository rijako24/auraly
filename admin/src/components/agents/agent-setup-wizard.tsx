"use client";

import Link from "next/link";
import { useState } from "react";
import { ArrowLeft, ArrowRight, MessageCircle, Save } from "lucide-react";
import { toast } from "sonner";

import { AgentTestChat } from "@/components/agents/agent-test-chat";
import { AgentSettingsEditor, type AgentEditorSection } from "@/components/agents/agent-settings-editor";
import { CatalogImportStep } from "@/components/agents/catalog-import-step";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import type { Agent, BusinessInboundContact } from "@/types/entities";
import { isAdminCustomerFact, type AgentSettings } from "@/types/agent-settings";
import { AgentBotType } from "@/types/agent-bot-type";

const STEPS = [
  { id: "catalog", title: "Catalogo", description: "Importar servicios desde PDF o documento" },
  { id: "identity", title: "Rol e identidad", description: "Persona y modelo del agente" },
  { id: "policies", title: "Politicas", description: "Reglas operativas y saludo inicial" },
  { id: "facts", title: "Datos (facts)", description: "Que informacion rastrea el agente" },
  { id: "flow", title: "Flujo", description: "Etapas del motor conversacional" },
  { id: "followUp", title: "Retomas", description: "Cuando y que conversacion debe retomar el bot" },
  { id: "messages", title: "Mensajes", description: "Secuencias, notificaciones y webhooks" },
  { id: "checkout", title: "Checkout", description: "Pagos, comercio y modos de checkout" },
  { id: "external", title: "Externos", description: "Escalaciones e interacciones inbound" },
  { id: "actions", title: "Acciones", description: "Acciones transversales del agente" },
  { id: "templates", title: "Templates", description: "Plantillas del motor" },
  { id: "safety", title: "Seguridad", description: "Kill switch y escalacion" },
  { id: "review", title: "Revision", description: "Confirmar y guardar" },
] as const;

type StepId = (typeof STEPS)[number]["id"];

function stepsFor(botType: AgentBotType) {
  if (botType === AgentBotType.Delivery || botType === AgentBotType.PaymentValidator) {
    return STEPS.filter((step) => !["catalog", "followUp", "checkout"].includes(step.id));
  }

  const applicableSteps = botType === AgentBotType.Order
    ? STEPS.filter((step) => step.id !== "catalog")
    : STEPS;

  return applicableSteps.map((step) => {
    if (step.id === "catalog") {
      return {
        ...step,
        title: "Servicios",
        description: "Importar el catalogo de servicios reservables",
      };
    }

    if (step.id === "checkout") {
      return {
        ...step,
        title: botType === AgentBotType.Order ? "Entrega y pagos" : "Pagos de reserva",
      };
    }

    return step;
  });
}

interface AgentSetupWizardProps {
  agent: Agent;
  businessId: string;
  settings: AgentSettings;
  onSettingsChange: (next: AgentSettings) => void;
  onSave: () => Promise<void>;
  onPublish?: () => Promise<void>;
  availableInboundContacts?: BusinessInboundContact[];
  saving: boolean;
  dirty: boolean;
}

export function AgentSetupWizard({
  agent,
  businessId,
  settings,
  onSettingsChange,
  onSave,
  onPublish,
  availableInboundContacts = [],
  saving,
  dirty,
}: AgentSetupWizardProps) {
  const [stepIndex, setStepIndex] = useState(0);
  const [catalogDone, setCatalogDone] = useState(false);

  const steps = stepsFor(agent.botType);
  const current = steps[stepIndex];
  const progress = ((stepIndex + 1) / steps.length) * 100;

  const goNext = () => setStepIndex((i) => Math.min(i + 1, steps.length - 1));
  const goPrev = () => setStepIndex((i) => Math.max(i - 1, 0));

  const handleSave = async () => {
    try {
      await onSave();
      toast.success("Configuración guardada");
    } catch {
      toast.error("No se pudo guardar");
    }
  };

  const handlePublish = async () => {
    try {
      await (onPublish ? onPublish() : onSave());
      toast.success(onPublish ? "Agente publicado" : "Configuracion guardada");
    } catch {
      toast.error(onPublish ? "No se pudo publicar el agente" : "No se pudo guardar");
    }
  };

  const renderStep = () => {
    switch (current.id as StepId) {
      case "catalog":
        return (
          <CatalogImportStep
            businessId={businessId}
            onImported={() => setCatalogDone(true)}
          />
        );
      case "review":
        return (
          <Card>
            <CardHeader>
              <CardTitle className="text-base">Resumen</CardTitle>
            </CardHeader>
            <CardContent className="space-y-2 text-sm">
              <p>
                <span className="text-muted-foreground">Agente:</span> {agent.name}
              </p>
              <p>
                <span className="text-muted-foreground">Modelo:</span>{" "}
                {settings.model ?? "—"}
              </p>
              <p>
                <span className="text-muted-foreground">Etapas del flujo:</span>{" "}
                {settings.flows?.reduce((total, flow) => total + (flow.stages?.length ?? 0), 0) ?? 0}
              </p>
              <p>
                <span className="text-muted-foreground">Facts:</span>{" "}
                {settings.factSchema?.filter(isAdminCustomerFact).length ?? 0}
              </p>
              <p>
                <span className="text-muted-foreground">Saludo inicial:</span>{" "}
                {settings.conversationOpening?.enabled ? "Activo" : "Desactivado"}
              </p>
              <p>
                <span className="text-muted-foreground">Retomas:</span>{" "}
                {settings.conversationFollowUp?.enabled ? "Activas - " + (settings.conversationFollowUp?.delayMinutes ?? 120) + " min" : "Desactivadas"}
              </p>
              <p>
                <span className="text-muted-foreground">Catálogo importado:</span>{" "}
                {catalogDone ? "Sí" : "Opcional / pendiente"}
              </p>
              {!settings.persona?.trim() && (
                <p className="text-destructive">
                  Falta definir la persona (paso Rol e identidad).
                </p>
              )}
              {(settings.flows?.reduce((total, flow) => total + (flow.stages?.length ?? 0), 0) ?? 0) === 0 && (
                <p className="text-amber-600 dark:text-amber-400">
                  No hay etapas en el flujo — el motor no guiará conversaciones por stages.
                </p>
              )}
            </CardContent>
          </Card>
        );
      default:
        return (
          <AgentSettingsEditor
            value={settings}
            onChange={onSettingsChange}
            availableInboundContacts={availableInboundContacts}
            botType={agent.botType}
            section={current.id as AgentEditorSection}
          />
        );
    }
  };

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <div className="flex items-center justify-between text-sm">
          <span className="font-medium">
            Paso {stepIndex + 1} de {steps.length}: {current.title}
          </span>
          <span className="text-muted-foreground">{Math.round(progress)}%</span>
        </div>
        <Progress value={progress} />
        <p className="text-sm text-muted-foreground">{current.description}</p>
      </div>

      <div className="flex flex-wrap gap-1">
        {steps.map((s, i) => (
          <button
            key={s.id}
            type="button"
            onClick={() => setStepIndex(i)}
            className={`rounded-full px-2.5 py-0.5 text-xs transition-colors ${
              i === stepIndex
                ? "bg-primary text-primary-foreground"
                : "bg-muted text-muted-foreground hover:bg-muted/80"
            }`}
          >
            {s.title}
          </button>
        ))}
      </div>

      <div className="min-w-0 pb-20">
        <div className="flex min-w-0 flex-col">
          <div className="flex-1">{renderStep()}</div>
          <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t pt-4">
            <div className="flex gap-2">
              <Button type="button" variant="outline" onClick={goPrev} disabled={stepIndex === 0}>
                <ArrowLeft className="mr-1 h-4 w-4" />
                Anterior
              </Button>
              {stepIndex < steps.length - 1 ? (
                <Button type="button" onClick={goNext}>
                  Siguiente
                  <ArrowRight className="ml-1 h-4 w-4" />
                </Button>
              ) : (
                <Button type="button" onClick={handlePublish} disabled={saving}>
                  <Save className="mr-1 h-4 w-4" />
                  {saving ? "Publicando..." : onPublish ? "Publicar agente" : "Guardar configuracion"}
                </Button>
              )}
            </div>
          </div>
        </div>
        <details className="group fixed bottom-4 right-4 z-50">
          <summary className="flex cursor-pointer list-none items-center gap-2 rounded-full bg-primary px-4 py-3 font-medium text-primary-foreground shadow-xl transition-transform hover:scale-[1.02] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 [&::-webkit-details-marker]:hidden">
            <MessageCircle className="h-5 w-5" />
            <span className="group-open:hidden">Probar agente</span>
            <span className="hidden group-open:inline">Cerrar prueba</span>
          </summary>
          <div className="absolute bottom-[calc(100%+0.75rem)] right-0 w-[min(420px,calc(100vw-2rem))]">
            <AgentTestChat
              agent={agent}
              hasUnsavedChanges={dirty}
              onSaveChanges={onSave}
              savingChanges={saving}
              compact
              className="max-h-[calc(100vh-6rem)] overflow-y-auto shadow-2xl"
            />
          </div>
        </details>
      </div>
    </div>
  );
}
