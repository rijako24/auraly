"use client";

import Link from "next/link";
import { LayoutGrid, MessageSquare } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useAgents } from "@/hooks/use-agents";

export default function AgentWorkspacesPage() {
  const { data, isLoading, isError, refetch } = useAgents({ page: 1, pageSize: 100 });
  const agents = data?.items ?? [];

  if (isLoading) return <PageLoading cards={6} />;
  if (isError) return <PageError onRetry={() => refetch()} />;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Workspaces</h1>
        <p className="text-muted-foreground text-sm">Abre el editor visual de flujos por agente.</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {agents.map((a) => (
          <Card key={a.agentId} className="flex flex-col">
            <CardHeader className="flex-1">
              <div className="flex items-start justify-between gap-2">
                <CardTitle className="text-lg leading-tight">{a.name}</CardTitle>
                <Badge variant={a.isActive ? "default" : "secondary"}>{a.isActive ? "Activo" : "Inactivo"}</Badge>
              </div>
              <CardDescription>{a.agentTypeName}</CardDescription>
            </CardHeader>
            <div className="flex gap-2 p-6 pt-0">
              <Button asChild className="flex-1">
                <Link href={`/dashboard/agents/workspace/${a.agentId}`}>
                  <LayoutGrid className="mr-2 h-4 w-4" />
                  Workspace
                </Link>
              </Button>
              <Button variant="outline" size="icon" asChild title="Chat">
                <Link href={`/dashboard/agents/workspace/${a.agentId}`}>
                  <MessageSquare className="h-4 w-4" />
                </Link>
              </Button>
            </div>
          </Card>
        ))}
      </div>

      {agents.length === 0 && (
        <p className="text-sm text-muted-foreground">No hay agentes. Crea uno desde la lista de Agents.</p>
      )}
    </div>
  );
}
