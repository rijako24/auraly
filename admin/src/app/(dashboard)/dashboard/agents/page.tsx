"use client";

import Link from "next/link";
import { Settings2, Sparkles } from "lucide-react";

import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { useAgents } from "@/hooks/use-agents";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function AgentsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data: agents, isLoading, isError, refetch } = useAgents();

  if (!businessId) {
    return (
      <div className="space-y-4">
        <h1 className="text-2xl font-semibold tracking-tight">Agente IA</h1>
        <p className="text-muted-foreground">
          Selecciona un negocio en el menú superior para configurar el agente.
        </p>
      </div>
    );
  }

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={() => refetch()} />;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Agente IA</h1>
        <p className="text-muted-foreground">
          Configura persona, flujo por etapas, facts, tools y guards según el motor
          agentic (<code className="text-xs">SettingsJson</code>).
        </p>
      </div>

      {(agents ?? []).length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center gap-3 py-12 text-center">
            <Sparkles className="h-10 w-10 text-muted-foreground" />
            <p className="text-muted-foreground">
              No hay agentes activos para este negocio. Créalos desde la base de datos
              o el seed <code className="text-xs">SeedAgenticConfiguration.sql</code>.
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2">
          {(agents ?? []).map((agent) => (
            <Card key={agent.agentId}>
              <CardHeader>
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <CardTitle className="flex items-center gap-2 text-lg">
                      <Sparkles className="h-5 w-5 text-primary" />
                      {agent.name}
                    </CardTitle>
                    <CardDescription>{agent.agentTypeName}</CardDescription>
                  </div>
                  <Badge variant={agent.isActive ? "default" : "secondary"}>
                    {agent.isActive ? "Activo" : "Inactivo"}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent>
                {agent.description && (
                  <p className="mb-4 text-sm text-muted-foreground line-clamp-2">
                    {agent.description}
                  </p>
                )}
                <div className="flex flex-wrap gap-2">
                  <Button asChild>
                    <Link href={`/dashboard/agents/${agent.agentId}/setup`}>
                      <Settings2 className="mr-2 h-4 w-4" />
                      Asistente de configuración
                    </Link>
                  </Button>
                  <Button asChild variant="outline">
                    <Link href={`/dashboard/agents/${agent.agentId}`}>
                      Modo avanzado
                    </Link>
                  </Button>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
