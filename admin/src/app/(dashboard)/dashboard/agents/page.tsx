"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { LayoutGrid, MessageSquare, MoreHorizontal, Pencil, Plus } from "lucide-react";

import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { formatDateTime } from "@/lib/utils";
import { useAgents } from "@/hooks/use-agents";
import type { Agent } from "@/types/entities";

export default function AgentsPage() {
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const { data, isLoading, isError, refetch } = useAgents({ page: 1, pageSize: 50 });
  const agents = data?.items ?? [];

  const columns: ColumnDef<Agent>[] = useMemo(
    () => [
      {
        accessorKey: "name",
        header: "Nombre",
        cell: ({ row }) => <div className="font-medium">{row.original.name}</div>,
      },
      {
        accessorKey: "agentTypeName",
        header: "Tipo",
        cell: ({ row }) => row.original.agentTypeName || "—",
      },
      {
        accessorKey: "isActive",
        header: "Estado",
        cell: ({ row }) => (
          <Badge variant={row.original.isActive ? "default" : "secondary"}>
            {row.original.isActive ? "Activo" : "Inactivo"}
          </Badge>
        ),
      },
      {
        accessorKey: "updatedAt",
        header: "Actualizado",
        cell: ({ row }) => formatDateTime(row.original.updatedAt ?? row.original.createdAt),
      },
      {
        id: "actions",
        cell: ({ row }) => {
          const a = row.original;
          return (
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="h-8 w-8">
                  <MoreHorizontal className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/agents/${a.agentId}/edit`}>
                    <Pencil className="mr-2 h-4 w-4" />
                    Editar
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/agents/workspace/${a.agentId}`}>
                    <LayoutGrid className="mr-2 h-4 w-4" />
                    Workspace
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem asChild>
                  <Link href={`/dashboard/agents/workspace/${a.agentId}?chat=1`}>
                    <MessageSquare className="mr-2 h-4 w-4" />
                    Chat / probar
                  </Link>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          );
        },
      },
    ],
    []
  );

  const cardRenderer = (item: Agent) => (
    <div className="flex flex-col gap-2 p-1">
      <div className="font-medium">{item.name}</div>
      <div className="text-xs text-muted-foreground">{item.agentTypeName}</div>
      <Badge variant={item.isActive ? "default" : "secondary"} className="w-fit text-xs">
        {item.isActive ? "Activo" : "Inactivo"}
      </Badge>
      <div className="flex gap-2 pt-2">
        <Button size="sm" variant="outline" asChild>
          <Link href={`/dashboard/agents/workspace/${item.agentId}`}>Workspace</Link>
        </Button>
      </div>
    </div>
  );

  if (isLoading) return <PageLoading cards={4} />;
  if (isError) return <PageError onRetry={() => refetch()} />;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Agents</h1>
          <p className="text-muted-foreground text-sm">Gestiona agentes de IA y sus flujos.</p>
        </div>
        <Button asChild>
          <Link href="/dashboard/agents/new">
            <Plus className="mr-2 h-4 w-4" />
            Crear agente
          </Link>
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={agents}
        searchKey="name"
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        cardRenderer={cardRenderer}
      />
    </div>
  );
}
