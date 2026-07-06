"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Plus, Eye, UserPlus, Users, Megaphone } from "lucide-react";

import { DataTable } from "@/components/tables/data-table";
import { StatCard } from "@/components/cards/stat-card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import {
  ConversationLifecycleStatusLabels,
  ConversationLifecycleStatus,
  LeadStatus,
  LeadStatusLabels,
  LeadStatusColors,
  getConversationStageLabel,
  getConversationStageStyle,
} from "@/types/enums";
import type { Lead } from "@/types/entities";
import { formatDateTime, cn } from "@/lib/utils";
import { useLeads } from "@/hooks/use-leads";

function renderLeadState(lead: Lead) {
  if (lead.currentStageName) {
    return (
      <Badge
        variant="secondary"
        className="border text-white"
        style={getConversationStageStyle(lead.currentStageName)}
      >
        {getConversationStageLabel(lead.currentStageName)}
      </Badge>
    );
  }

  if (lead.conversationStatus) {
    return (
      <Badge variant="secondary">
        {ConversationLifecycleStatusLabels[lead.conversationStatus]}
      </Badge>
    );
  }

  return (
    <Badge variant="secondary" className={cn(LeadStatusColors[lead.status])}>
      {LeadStatusLabels[lead.status]}
    </Badge>
  );
}

export default function LeadsPage() {
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const { data, isLoading, isError, refetch } = useLeads({
    page,
    pageSize,
    search: search || undefined,
  });
  const leads = data?.items ?? [];

  const stats = useMemo(() => ({
    total: data?.totalCount ?? leads.length,
    new: leads.filter((l) => !l.conversationStatus).length,
    contacted: leads.filter((l) => l.conversationStatus === ConversationLifecycleStatus.Active).length,
    closed: leads.filter((l) => l.conversationStatus === ConversationLifecycleStatus.Closed).length,
  }), [data?.totalCount, leads]);

  const columns: ColumnDef<Lead>[] = useMemo(() => [
    {
      accessorKey: "customerName",
      header: "Cliente",
      cell: ({ row }) => (
        <div className="font-medium">
          {row.original.customerName ?? row.original.userNumber}
        </div>
      ),
    },
    { accessorKey: "userNumber", header: "Telefono" },
    {
      accessorKey: "currentStageName",
      header: "Estado",
      cell: ({ row }) => renderLeadState(row.original),
    },
    {
      accessorKey: "notes",
      header: "Notas",
      cell: ({ row }) => (
        <span className="text-muted-foreground max-w-[200px] block truncate">
          {row.original.notes ?? "-"}
        </span>
      ),
    },
    {
      accessorKey: "timestamp",
      header: "Fecha",
      cell: ({ row }) => formatDateTime(row.original.lastActivityAt ?? row.original.timestamp),
    },
    {
      id: "actions",
      cell: ({ row }) => {
        const lead = row.original;
        return (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-8 w-8">
                <MoreHorizontal className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem asChild>
                <Link href={`/dashboard/leads/${lead.leadId}`}>
                  <Eye className="mr-2 h-4 w-4" />
                  Ver detalle
                </Link>
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        );
      },
    },
  ], []);

  const facetedFilters = useMemo(() => [
    {
      column: "status",
      title: "Estado",
      options: Object.entries(LeadStatusLabels).map(([value, label]) => ({ label, value })),
    },
  ], []);

  const cardRenderer = (item: Lead) => (
    <Card key={item.leadId} className="overflow-hidden">
      <CardContent className="pt-4">
        <div className="flex items-start justify-between gap-2">
          <div>
            <h3 className="font-semibold">{item.customerName ?? item.userNumber}</h3>
            <p className="text-sm text-muted-foreground">{item.userNumber}</p>
          </div>
          {renderLeadState(item)}
        </div>
        <p className="mt-2 text-sm text-muted-foreground line-clamp-2">
          {item.notes ?? "Sin notas"}
        </p>
        <p className="mt-2 text-xs text-muted-foreground">
          {formatDateTime(item.lastActivityAt ?? item.timestamp)}
        </p>
        <Button variant="outline" size="sm" className="mt-3 w-full" asChild>
          <Link href={`/dashboard/leads/${item.leadId}`}>
            <Eye className="mr-2 h-4 w-4" />
            Ver detalle
          </Link>
        </Button>
      </CardContent>
    </Card>
  );

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Leads</h1>
          <p className="text-muted-foreground">Gestiona los leads y seguimiento de clientes potenciales</p>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" asChild>
            <Link href="/dashboard/campaigns">
              <Megaphone className="mr-2 h-4 w-4" />
              Campañas
            </Link>
          </Button>
          <Button asChild>
            <Link href="/dashboard/leads/new">
              <Plus className="mr-2 h-4 w-4" />
              Nuevo Lead
            </Link>
          </Button>
        </div>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard title="Total Leads" value={stats.total} icon={Users} />
        <StatCard title="Nuevos" value={stats.new} icon={UserPlus} />
        <StatCard title="Contactados" value={stats.contacted} icon={UserPlus} />
        <StatCard title="Cerrados" value={stats.closed} icon={UserPlus} />
      </div>
      <DataTable
        columns={columns}
        data={leads}
        searchKey="customerName"
        searchPlaceholder="Buscar por nombre..."
        facetedFilters={facetedFilters}
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        cardRenderer={cardRenderer}
        enableRowSelection={false}
        page={page}
        pageSize={pageSize}
        pageCount={data?.totalPages}
        totalItems={data?.totalCount}
        onPaginationChange={(nextPage, nextPageSize) => {
          setPage(nextPageSize === pageSize ? nextPage : 1);
          setPageSize(nextPageSize);
        }}
        onSearch={(value) => {
          setSearch(value);
          setPage(1);
        }}
      />
    </div>
  );
}
