"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";

import { AgentOperationalModeControl } from "@/components/agents/agent-operational-mode-control";
import { AgentSetupWizard } from "@/components/agents/agent-setup-wizard";
import { Button } from "@/components/ui/button";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useAgent, useBusinessInboundContacts, useUpdateAgentSettings, useUpdateAgentStatus } from "@/hooks/use-agents";
import { useBusinessContextStore } from "@/stores/business-context-store";
import {
  parseAgentSettingsFromAgent,
  type AgentSettings,
} from "@/types/agent-settings";

export default function AgentSetupPage() {
  const params = useParams();
  const agentId = params.agentId as string;
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data: agent, isLoading, isError, refetch } = useAgent(agentId);
  const { data: inboundContacts } = useBusinessInboundContacts();
  const updateMutation = useUpdateAgentSettings(agentId);
  const statusMutation = useUpdateAgentStatus(agentId);
  const [settings, setSettings] = useState<AgentSettings | null>(null);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (!agent) return;
    setSettings(parseAgentSettingsFromAgent(agent));
    setDirty(false);
  }, [agent]);

  useEffect(() => {
    if (!dirty) return;

    const warnBeforeLeaving = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };

    window.addEventListener("beforeunload", warnBeforeLeaving);
    return () => window.removeEventListener("beforeunload", warnBeforeLeaving);
  }, [dirty]);

  const saveSettings = async () => {
    if (!settings) return;
    await updateMutation.mutateAsync(settings);
    setDirty(false);
  };

  const publishAgent = async () => {
    if (dirty) await saveSettings();
    await statusMutation.mutateAsync(true);
  };

  if (!businessId) {
    return (
      <p className="text-muted-foreground">
        Selecciona un negocio para configurar el agente.
      </p>
    );
  }

  if (isError) return <PageError onRetry={() => refetch()} />;
  if (isLoading) return <PageLoading />;
  if (!agent) return <PageError onRetry={() => refetch()} />;
  if (!settings) return <PageLoading />;

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Button variant="ghost" size="sm" className="-ml-2 w-fit" asChild>
          <Link href="/dashboard/agents">
            <ArrowLeft className="mr-1 h-4 w-4" />
            Agentes
          </Link>
        </Button>
        <h1 className="text-2xl font-semibold tracking-tight">
          Configurar {agent.name}
        </h1>
        <p className="text-sm text-muted-foreground">
          Asistente paso a paso — alineado al motor agentic (
          <code className="text-xs">SettingsJson</code>)
        </p>
      </div>

      <AgentOperationalModeControl businessId={agent.businessId} />

      <AgentSetupWizard
        businessId={agent.businessId}
        agent={agent}
        settings={settings}
        onSettingsChange={(next) => {
          setSettings(next);
          setDirty(true);
        }}
        onSave={saveSettings}
        onPublish={publishAgent}
        availableInboundContacts={inboundContacts ?? []}
        saving={updateMutation.isPending || statusMutation.isPending}
        dirty={dirty}
      />
    </div>
  );
}
