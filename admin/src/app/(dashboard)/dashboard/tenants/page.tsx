"use client";
import { useMemo } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Plus, Eye, Pencil } from "lucide-react";
import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import type { Tenant } from "@/types/entities";
import { formatDate } from "@/lib/utils";
import { useTenants } from "@/hooks/use-tenants";

export default function TenantsPage() {
  const { data, isLoading, isError, refetch } = useTenants();
  const tenants = data?.items ?? [];
  const columns: ColumnDef<Tenant>[] = useMemo(() => [
    { accessorKey: "name", header: "Nombre", cell: ({ row }) => <div className="font-medium">{row.original.name}</div> },
    { accessorKey: "email", header: "Email" },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { accessorKey: "businesses", header: "Negocios", cell: ({ row }) => row.original.businesses?.length ?? 0 },
    { accessorKey: "createdAt", header: "Creado", cell: ({ row }) => formatDate(row.original.createdAt) },
    { id: "actions", cell: ({ row }) => { const t = row.original; return (<DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem asChild><Link href={`/dashboard/tenants/${t.tenantId}`}><Eye className="mr-2 h-4 w-4" />Ver</Link></DropdownMenuItem><DropdownMenuItem asChild><Link href={`/dashboard/tenants/${t.tenantId}/edit`}><Pencil className="mr-2 h-4 w-4" />Editar</Link></DropdownMenuItem></DropdownMenuContent></DropdownMenu>); } },
  ], []);

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Tenants</h1><p className="text-muted-foreground">Gestiona las organizaciones que usan la plataforma</p></div>
        <Button asChild><Link href="/dashboard/tenants/new"><Plus className="mr-2 h-4 w-4" />Nuevo Tenant</Link></Button>
      </div>
      <DataTable columns={columns} data={tenants} searchKey="name" searchPlaceholder="Buscar por nombre..." enableRowSelection={false} />
    </div>
  );
}
