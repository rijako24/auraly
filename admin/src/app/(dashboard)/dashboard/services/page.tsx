"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { Clock3, Eye, MoreHorizontal, Plus, PowerOff, Sparkles } from "lucide-react";
import { toast } from "sonner";

import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageError } from "@/components/ui/page-error";
import { useDeleteService, useServices } from "@/hooks/use-services";
import { formatCurrency } from "@/lib/utils";
import { useAuthStore } from "@/stores/auth-store";
import type { Service } from "@/types/entities";
import { ServiceTierLabels, ServiceTypeLabels } from "@/types/enums";

export default function ServicesPage() {
  const permissionValues = useAuthStore((state) => state.user?.permissions);
  const permissions = useMemo(() => new Set(permissionValues ?? []), [permissionValues]);
  const canCreate = permissions.has("services.create");
  const canDelete = permissions.has("services.delete");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [pendingDeactivation, setPendingDeactivation] = useState<Service | null>(null);
  const query = useServices({ page, pageSize, search: search || undefined });
  const deactivate = useDeleteService();
  const services = query.data?.items ?? [];

  async function confirmDeactivation() {
    if (!pendingDeactivation) return;
    try {
      await deactivate.mutateAsync(pendingDeactivation.serviceId);
      toast.success("Servicio desactivado");
      setPendingDeactivation(null);
    } catch {
      toast.error("No fue posible desactivar el servicio");
    }
  }

  const columns: ColumnDef<Service>[] = useMemo(() => [
    {
      accessorKey: "serviceName",
      header: "Servicio",
      cell: ({ row }) => <div><p className="font-medium text-foreground">{row.original.serviceName}</p><p className="mt-0.5 max-w-md truncate text-xs text-muted-foreground">{row.original.description || "Sin descripción"}</p></div>,
    },
    { accessorKey: "categoryName", header: "Categoría", cell: ({ row }) => row.original.categoryName ?? row.original.category?.name ?? "Sin categoría" },
    { accessorKey: "durationMinutes", header: "Duración", cell: ({ row }) => <span className="inline-flex items-center gap-1.5"><Clock3 className="h-4 w-4 text-emerald-600" />{row.original.durationMinutes} min</span> },
    { accessorKey: "price", header: "Precio", cell: ({ row }) => <div><p className="font-semibold tabular-nums">{formatCurrency(row.original.price)}</p>{!row.original.includeInCheckoutTotal && <p className="text-xs text-muted-foreground">No suma al cobro</p>}</div> },
    { accessorKey: "serviceType", header: "Tipo", cell: ({ row }) => <Badge variant="outline">{ServiceTypeLabels[row.original.serviceType] ?? "Sin tipo"}</Badge> },
    { accessorKey: "tier", header: "Nivel", cell: ({ row }) => ServiceTierLabels[row.original.tier] ?? "—" },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge className={row.original.isActive ? "bg-emerald-100 text-emerald-800 hover:bg-emerald-100" : "bg-slate-100 text-slate-600 hover:bg-slate-100"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    {
      id: "actions",
      cell: ({ row }) => <DropdownMenu>
        <DropdownMenuTrigger asChild><Button variant="ghost" size="icon" aria-label={`Acciones de ${row.original.serviceName}`}><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem asChild><Link href={`/dashboard/services/${row.original.serviceId}`}><Eye className="mr-2 h-4 w-4" />Ver y editar</Link></DropdownMenuItem>
          {canDelete && row.original.isActive && <><DropdownMenuSeparator /><DropdownMenuItem className="text-destructive" onClick={() => setPendingDeactivation(row.original)}><PowerOff className="mr-2 h-4 w-4" />Desactivar</DropdownMenuItem></>}
        </DropdownMenuContent>
      </DropdownMenu>,
    },
  ], [canDelete]);

  if (query.isError) return <PageError onRetry={query.refetch} />;

  return <div className="space-y-6 p-6">
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div><p className="text-sm font-medium text-emerald-600">Catálogo comercial</p><h1 className="text-3xl font-bold tracking-tight">Servicios</h1><p className="mt-1 text-muted-foreground">Organiza lo que ofreces, su duración y el valor que verá el cliente.</p></div>
      {canCreate && <Button asChild className="rounded-xl"><Link href="/dashboard/services/new"><Plus className="mr-2 h-4 w-4" />Nuevo servicio</Link></Button>}
    </header>

    <div className="grid gap-3 sm:grid-cols-2">
      <Card className="border-emerald-100"><CardContent className="flex items-center gap-3 pt-6"><span className="rounded-xl bg-emerald-50 p-3 text-emerald-700"><Sparkles className="h-5 w-5" /></span><div><p className="text-sm text-muted-foreground">Servicios registrados</p><p className="text-2xl font-bold">{query.data?.totalCount ?? 0}</p></div></CardContent></Card>
      <Card><CardContent className="flex items-center gap-3 pt-6"><span className="rounded-xl bg-slate-100 p-3 text-slate-700"><Clock3 className="h-5 w-5" /></span><div><p className="text-sm text-muted-foreground">Visibles en esta página</p><p className="text-2xl font-bold">{services.length}</p></div></CardContent></Card>
    </div>

    <Card className="rounded-2xl"><CardContent className="pt-6">
      <DataTable
        columns={columns}
        data={services}
        searchKey="serviceName"
        searchPlaceholder="Buscar en todos los servicios..."
        isLoading={query.isLoading}
        page={page}
        pageSize={pageSize}
        pageCount={query.data?.totalPages ?? 0}
        totalItems={query.data?.totalCount ?? 0}
        onSearch={(value) => { setSearch(value.trim()); setPage(1); }}
        onPaginationChange={(nextPage, nextPageSize) => { setPage(nextPage); setPageSize(nextPageSize); }}
        enableRowSelection={false}
      />
    </CardContent></Card>

    <Dialog open={Boolean(pendingDeactivation)} onOpenChange={(open) => !open && setPendingDeactivation(null)}>
      <DialogContent>
        <DialogHeader><DialogTitle>Desactivar servicio</DialogTitle><DialogDescription>“{pendingDeactivation?.serviceName}” dejará de estar disponible para nuevas ventas y reservas.</DialogDescription></DialogHeader>
        <DialogFooter><Button variant="outline" onClick={() => setPendingDeactivation(null)}>Cancelar</Button><Button variant="destructive" onClick={() => void confirmDeactivation()} disabled={deactivate.isPending}>{deactivate.isPending ? "Desactivando..." : "Desactivar"}</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </div>;
}
