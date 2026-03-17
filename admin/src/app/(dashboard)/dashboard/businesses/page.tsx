"use client";
import { useMemo, useState } from "react";
import Link from "next/link";
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

export default function BusinessesPage() {
  const [viewMode, setViewMode] = useState<"table" | "card" | "list">("table");
  const { data, isLoading, isError, refetch } = useBusinesses();
  const businesses = data?.items ?? [];
  const columns: ColumnDef<Business>[] = useMemo(() => [
    { accessorKey: "name", header: "Negocio", cell: ({ row }) => { const b = row.original; return (<div className="flex items-center gap-3"><Avatar className="h-9 w-9"><AvatarFallback className="text-xs">{getInitials(b.name)}</AvatarFallback></Avatar><span className="font-medium">{b.name}</span></div>); } },
    { accessorKey: "email", header: "Email" },
    { accessorKey: "phone", header: "Teléfono" },
    { accessorKey: "address", header: "Dirección", cell: ({ row }) => truncate(row.original.address, 35) },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { accessorKey: "tenant", header: "Tenant", cell: ({ row }) => row.original.tenant?.name ?? "—" },
    { accessorKey: "createdAt", header: "Creado", cell: ({ row }) => formatDate(row.original.createdAt) },
    { id: "actions", cell: ({ row }) => { const b = row.original; return (<DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem asChild><Link href={`/dashboard/businesses/${b.businessId}`}><Eye className="mr-2 h-4 w-4" />Ver</Link></DropdownMenuItem><DropdownMenuItem asChild><Link href={`/dashboard/businesses/${b.businessId}/edit`}><Pencil className="mr-2 h-4 w-4" />Editar</Link></DropdownMenuItem></DropdownMenuContent></DropdownMenu>); } },
  ], []);

  const cardRenderer = (item: Business) => (
    <Card key={item.businessId} className="overflow-hidden">
      <CardHeader className="pb-2"><div className="flex items-start gap-3"><Avatar className="h-12 w-12"><AvatarFallback>{getInitials(item.name)}</AvatarFallback></Avatar><div className="flex-1 min-w-0"><h3 className="font-semibold truncate">{item.name}</h3><p className="text-sm text-muted-foreground line-clamp-2">{truncate(item.address, 50)}</p><div className="mt-2 flex gap-1"><Badge variant={item.isActive ? "default" : "secondary"}>{item.isActive ? "Activo" : "Inactivo"}</Badge></div></div></div></CardHeader>
      <CardContent className="pt-0"><Button variant="outline" size="sm" className="w-full" asChild><Link href={`/dashboard/businesses/${item.businessId}`}>Ver detalle</Link></Button></CardContent>
    </Card>
  );

  if (isLoading) return <PageLoading />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Negocios</h1><p className="text-muted-foreground">Gestiona los negocios y sus configuraciones</p></div>
        <Button asChild><Link href="/dashboard/businesses/new"><Plus className="mr-2 h-4 w-4" />Nuevo Negocio</Link></Button>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4"><StatCard title="Total negocios" value={businesses.length} icon={Store} /></div>
      <DataTable columns={columns} data={businesses} searchKey="name" searchPlaceholder="Buscar por nombre..." viewMode={viewMode} onViewModeChange={setViewMode} cardRenderer={cardRenderer} enableRowSelection={false} />
    </div>
  );
}
