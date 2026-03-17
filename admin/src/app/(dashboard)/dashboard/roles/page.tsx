"use client";
import { useMemo } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Plus, Eye, Pencil, Trash2 } from "lucide-react";
import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import type { AppRole } from "@/types/entities";
import { formatDate } from "@/lib/utils";
import { useRoles } from "@/hooks/use-roles";

export default function RolesPage() {
  const { data, isLoading, isError, refetch } = useRoles();
  const roles = data?.items ?? [];
  const columns: ColumnDef<AppRole>[] = useMemo(() => [
    { accessorKey: "name", header: "Nombre", cell: ({ row }) => <div className="font-medium">{row.original.name}</div> },
    { accessorKey: "description", header: "Descripción", cell: ({ row }) => <span className="text-muted-foreground max-w-[250px] block truncate">{row.original.description ?? "—"}</span> },
    { accessorKey: "isSystemRole", header: "Rol Sistema", cell: ({ row }) => <Badge variant={row.original.isSystemRole ? "secondary" : "outline"}>{row.original.isSystemRole ? "Sistema" : "Personalizado"}</Badge> },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { accessorKey: "createdAt", header: "Creado", cell: ({ row }) => formatDate(row.original.createdAt) },
    { id: "actions", cell: ({ row }) => { const role = row.original; return (<DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem asChild><Link href={`/dashboard/roles/${role.roleId}`}><Eye className="mr-2 h-4 w-4" />Ver</Link></DropdownMenuItem><DropdownMenuItem asChild><Link href={`/dashboard/roles/${role.roleId}`}><Pencil className="mr-2 h-4 w-4" />Editar</Link></DropdownMenuItem>{!role.isSystemRole && <DropdownMenuItem className="text-destructive"><Trash2 className="mr-2 h-4 w-4" />Eliminar</DropdownMenuItem>}</DropdownMenuContent></DropdownMenu>); } },
  ], []);

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Roles y Permisos</h1><p className="text-muted-foreground">Gestiona roles y permisos del sistema</p></div>
        <Button asChild><Link href="/dashboard/roles/new"><Plus className="mr-2 h-4 w-4" />Nuevo Rol</Link></Button>
      </div>
      <DataTable columns={columns} data={roles} searchKey="name" searchPlaceholder="Buscar por nombre..." enableRowSelection={false} />
    </div>
  );
}
