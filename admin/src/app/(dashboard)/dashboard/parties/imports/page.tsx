"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowLeft, CheckCircle2, Link2, RefreshCw, Search, TriangleAlert } from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  useExternalCustomers,
  useReconcileExternalCustomer,
  useReconcilePendingExternalCustomers,
} from "@/hooks/use-external-customers";
import type { ExternalCustomerReconciliationItem } from "@/services/api/external-customers";
import { useAuthStore } from "@/stores/auth-store";

export default function ExternalCustomerImportsPage() {
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("Pending");
  const query = useExternalCustomers({
    page,
    pageSize,
    search: search.trim() || undefined,
    status: status === "all" ? undefined : status,
  });
  const reconcile = useReconcileExternalCustomer();
  const reconcilePending = useReconcilePendingExternalCustomers();
  const canReconcile = permissions.has("parties.external-customers.reconcile");

  const reconcileOne = async (item: ExternalCustomerReconciliationItem) => {
    try {
      const result = await reconcile.mutateAsync(item.externalCommerceCustomerId);
      if (result.status === "Conflict") toast.error(result.error ?? "La identidad requiere revisión.");
      else toast.success(result.idempotentReplay ? "El cliente ya estaba relacionado." : "Cliente relacionado con Terceros.");
    } catch {
      toast.error("No fue posible reconciliar el cliente externo.");
    }
  };

  const columns = useMemo<ColumnDef<ExternalCustomerReconciliationItem>[]>(() => [
    {
      accessorKey: "name",
      header: "Cliente extraído",
      cell: ({ row }) => <div>
        <p className="font-semibold">{row.original.name || "Nombre no informado"}</p>
        <p className="text-xs text-muted-foreground">{row.original.phone || "Teléfono no informado"}</p>
      </div>,
    },
    {
      accessorKey: "integrationName",
      header: "Origen",
      cell: ({ row }) => <div>
        <p>{row.original.integrationName}</p>
        <p className="text-xs text-muted-foreground">{row.original.externalAccountId} · {row.original.externalCustomerId}</p>
      </div>,
    },
    {
      accessorKey: "status",
      header: "Estado",
      cell: ({ row }) => <div className="max-w-sm space-y-1">
        <StatusBadge status={row.original.status} />
        {row.original.error && <p className="text-xs text-destructive">{row.original.error}</p>}
      </div>,
    },
    {
      accessorKey: "lastSyncedAt",
      header: "Última extracción",
      cell: ({ row }) => new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short" })
        .format(new Date(row.original.lastSyncedAt)),
    },
    {
      id: "actions",
      header: "",
      cell: ({ row }) => <Button
        size="sm"
        variant={row.original.status === "Conflict" ? "outline" : "default"}
        disabled={!canReconcile || reconcile.isPending || row.original.status === "Linked"}
        onClick={(event) => {
          event.stopPropagation();
          void reconcileOne(row.original);
        }}
      >
        <Link2 className="mr-2 h-4 w-4" />
        {row.original.status === "Conflict" ? "Reintentar" : "Relacionar"}
      </Button>,
    },
  ], [canReconcile, reconcile.isPending]);

  const processPending = async () => {
    try {
      const result = await reconcilePending.mutateAsync(50);
      toast.success(`${result.linked} relacionados; ${result.conflicts} requieren revisión.`);
    } catch {
      toast.error("No fue posible procesar las importaciones pendientes.");
    }
  };

  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 xl:flex-row xl:items-end">
      <div>
        <Button asChild variant="ghost" className="mb-2 -ml-3">
          <Link href="/dashboard/parties"><ArrowLeft className="mr-2 h-4 w-4" />Volver a Terceros</Link>
        </Button>
        <p className="text-sm font-medium text-primary">Puente con sistemas externos</p>
        <h1 className="text-3xl font-semibold tracking-tight">Clientes por reconciliar</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">
          Auraly conserva la referencia externa y la relaciona con una Party. Los datos ausentes permanecen pendientes, nunca se inventan.
        </p>
      </div>
      <Button disabled={!canReconcile || reconcilePending.isPending} onClick={processPending}>
        <RefreshCw className={`mr-2 h-4 w-4 ${reconcilePending.isPending ? "animate-spin" : ""}`} />
        Procesar pendientes
      </Button>
    </header>

    <section className="grid gap-3 rounded-2xl border bg-card p-4 lg:grid-cols-[minmax(0,1fr)_15rem]">
      <label className="relative">
        <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          className="pl-9"
          value={search}
          onChange={(event) => { setSearch(event.target.value); setPage(1); }}
          placeholder="Nombre, teléfono o identificador externo"
        />
      </label>
      <Select value={status} onValueChange={(value) => { setStatus(value); setPage(1); }}>
        <SelectTrigger><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos los estados</SelectItem>
          <SelectItem value="Pending">Pendientes</SelectItem>
          <SelectItem value="Conflict">Requieren revisión</SelectItem>
          <SelectItem value="Linked">Relacionados</SelectItem>
        </SelectContent>
      </Select>
    </section>

    <DataTable
      columns={columns}
      data={query.data?.items ?? []}
      isLoading={query.isLoading}
      page={query.data?.page}
      pageSize={query.data?.pageSize}
      pageCount={query.data?.totalPages}
      totalItems={query.data?.totalCount}
      enableRowSelection={false}
      onPaginationChange={(nextPage, nextSize) => { setPage(nextPage); setPageSize(nextSize); }}
    />
  </div>;
}

function StatusBadge({ status }: { status: ExternalCustomerReconciliationItem["status"] }) {
  if (status === "Linked") return <Badge className="gap-1 bg-emerald-600"><CheckCircle2 className="h-3 w-3" />Relacionado</Badge>;
  if (status === "Conflict") return <Badge variant="destructive" className="gap-1"><TriangleAlert className="h-3 w-3" />Revisar identidad</Badge>;
  return <Badge variant="secondary">Pendiente</Badge>;
}
