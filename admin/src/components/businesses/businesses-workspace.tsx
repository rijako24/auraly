"use client";
import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Plus, Eye, Pencil, Store } from "lucide-react";
import { DataTable } from "@/components/tables/data-table";
import { StatCard } from "@/components/cards/stat-card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import type { Business } from "@/types/entities";
import { formatDate, truncate, getInitials } from "@/lib/utils";
import { useBusinesses } from "@/hooks/use-businesses";
import { useAuthStore } from "@/stores/auth-store";
import { BusinessDialog, type BusinessDialogMode } from "@/components/businesses/business-dialog";

export function BusinessesWorkspace({ initialMode, initialBusinessId }: { initialMode?: BusinessDialogMode; initialBusinessId?: string }) {
  const router = useRouter();
  const permissions = useAuthStore(state => state.user?.permissions ?? []);
  const canCreate = permissions.includes("businesses.create");
  const canEdit = permissions.includes("businesses.update");
  const [selection, setSelection] = useState<{ mode: BusinessDialogMode; business?: Business; id?: string } | null>(() => initialMode ? { mode: initialMode, id: initialBusinessId } : null);
  function closeDialog() {
    setSelection(null);
    if (initialMode) router.replace("/dashboard/businesses");
  }
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const { data, isLoading, isFetching, isError, refetch } = useBusinesses({ page, pageSize, search });
  const businesses = data?.items ?? [];
  const columns: ColumnDef<Business>[] = useMemo(() => [
    { accessorKey: "name", header: "Sede", cell: ({ row }) => { const b = row.original; return (<div className="flex items-center gap-3"><Avatar className="h-9 w-9"><AvatarFallback className="text-xs">{getInitials(b.name)}</AvatarFallback></Avatar><span className="font-medium">{b.name}</span></div>); } },
    { accessorKey: "email", header: "Email" },
    { accessorKey: "phone", header: "Teléfono" },
    { accessorKey: "address", header: "Dirección", cell: ({ row }) => truncate(row.original.address, 35) },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { accessorKey: "tenant", header: "Tenant", cell: ({ row }) => row.original.tenant?.name ?? "—" },
    { accessorKey: "createdAt", header: "Creado", cell: ({ row }) => formatDate(row.original.createdAt) },
    { id: "actions", cell: ({ row }) => { const b = row.original; return (
      <DropdownMenu modal={false}><DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="h-8 w-8" aria-label={`Acciones de ${b.name}`} onClick={event => event.stopPropagation()}><MoreHorizontal className="h-4 w-4" /></Button>
      </DropdownMenuTrigger><DropdownMenuContent align="end" onClick={event => event.stopPropagation()}>
        <DropdownMenuItem onSelect={() => setSelection({ mode: "view", business: b })}><Eye className="mr-2 h-4 w-4" />Ver</DropdownMenuItem>
        {canEdit && <DropdownMenuItem onSelect={() => setSelection({ mode: "edit", business: b })}><Pencil className="mr-2 h-4 w-4" />Editar</DropdownMenuItem>}
      </DropdownMenuContent></DropdownMenu>); } },
  ], [canEdit]);

  const cardRenderer = (item: Business) => (
    <Card key={item.businessId} className="overflow-hidden">
      <CardHeader className="pb-2"><div className="flex items-start gap-3"><Avatar className="h-12 w-12"><AvatarFallback>{getInitials(item.name)}</AvatarFallback></Avatar><div className="flex-1 min-w-0"><h3 className="font-semibold truncate">{item.name}</h3><p className="text-sm text-muted-foreground line-clamp-2">{truncate(item.address, 50)}</p><div className="mt-2 flex gap-1"><Badge variant={item.isActive ? "default" : "secondary"}>{item.isActive ? "Activo" : "Inactivo"}</Badge></div></div></div></CardHeader>
      <CardContent className="pt-0"><Button variant="outline" size="sm" className="w-full" onClick={() => setSelection({ mode: "view", business: item })}>Ver detalle</Button></CardContent>
    </Card>
  );

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Sedes</h1><p className="text-muted-foreground">Gestiona las sedes y sus configuraciones</p></div>
        {canCreate && <Button onClick={() => setSelection({ mode: "create" })}><Plus className="mr-2 h-4 w-4" />Nueva sede</Button>}
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4"><StatCard title="Total sedes" value={data?.totalCount ?? 0} icon={Store} /></div>
      <DataTable columns={columns} data={businesses} onRowClick={business => setSelection({ mode: "view", business })} searchKey="name" searchPlaceholder="Buscar por nombre..." isSearching={isFetching} page={data?.page} pageSize={data?.pageSize} pageCount={data?.totalPages} totalItems={data?.totalCount} onSearch={(value) => { setSearch(value); setPage(1); }} onPaginationChange={(nextPage, nextPageSize) => { setPage(nextPage); setPageSize(nextPageSize); }} viewMode={viewMode} onViewModeChange={setViewMode} cardRenderer={cardRenderer} enableRowSelection={false} />
      {selection && <BusinessDialog key={selection.business?.businessId ?? selection.id ?? "new"} mode={selection.mode} businessId={selection.business?.businessId ?? selection.id} initialBusiness={selection.business}
        canCreate={canCreate} canEdit={canEdit} onEdit={() => setSelection(current => current && { ...current, mode: "edit" })} onClose={closeDialog} />}
    </div>
  );
}
