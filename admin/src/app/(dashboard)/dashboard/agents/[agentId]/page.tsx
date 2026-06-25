"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Save } from "lucide-react";
import { toast } from "sonner";

import { AgentOperationalModeControl } from "@/components/agents/agent-operational-mode-control";
import { AgentTestChat } from "@/components/agents/agent-test-chat";
import { AgentSettingsEditor } from "@/components/agents/agent-settings-editor";
import { Button } from "@/components/ui/button";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useAgent, useBusinessInboundContacts, useUpdateAgentSettings } from "@/hooks/use-agents";
import {
  parseAgentSettingsFromAgent,
  type AgentSettings,
} from "@/types/agent-settings";

export default function AgentConfigPage() {
  const params = useParams();
  const agentId = params.agentId as string;
  const { data: agent, isLoading, isError, refetch } = useAgent(agentId);
  const { data: inboundContacts } = useBusinessInboundContacts();
  const updateMutation = useUpdateAgentSettings(agentId);
  const [settings, setSettings] = useState<AgentSettings | null>(null);
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (!agent) return;
    const parsed = parseAgentSettingsFromAgent(agent);
    setSettings(parsed);
    setDirty(false);
  }, [agent]);

  const handleSave = async () => {
    if (!settings) return;
    try {
      await updateMutation.mutateAsync(settings);
      setDirty(false);
      toast.success("Configuración del agente guardada");
    } catch {
      toast.error("No se pudo guardar la configuración");
    }
  };

  if (isError) return <PageError onRetry={() => refetch()} />;
  if (isLoading) return <PageLoading />;
  if (!agent) return <PageError onRetry={() => refetch()} />;
  if (!settings) return <PageLoading />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="space-y-1">
          <div className="flex flex-wrap gap-2">
            <Button variant="ghost" size="sm" className="-ml-2 w-fit" asChild>
              <Link href="/dashboard/agents">
                <ArrowLeft className="mr-1 h-4 w-4" />
                Agentes
              </Link>
            </Button>
            <Button variant="outline" size="sm" asChild>
              <Link href={`/dashboard/agents/${agentId}/setup`}>
                Volver al asistente
              </Link>
            </Button>
          </div>
          <h1 className="text-2xl font-semibold tracking-tight">{agent.name}</h1>
          <p className="text-sm text-muted-foreground">
            Motor agentic — edición de <code className="text-xs">SettingsJson</code>
          </p>
        </div>
        <Button
          onClick={handleSave}
          disabled={!dirty || updateMutation.isPending}
        >
          <Save className="mr-2 h-4 w-4" />
          {updateMutation.isPending ? "Guardando…" : "Guardar"}
        </Button>
      </div>

      <AgentOperationalModeControl businessId={agent.businessId} />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_420px]">
        <div className="min-w-0">
          <AgentSettingsEditor
            value={settings}
            availableInboundContacts={inboundContacts ?? []}
            onChange={(next) => {
              setSettings(next);
              setDirty(true);
            }}
          />
        </div>
        <AgentTestChat
          agent={agent}
          hasUnsavedChanges={dirty}
          compact
          className="xl:sticky xl:top-4 xl:self-start"
        />
      </div>
    </div>
  );
}
