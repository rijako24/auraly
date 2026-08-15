"use client";

import { useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Copy, Eye, MoreHorizontal, Pencil, Plus, ShieldCheck, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { RolePermissionWorkspace } from "@/components/roles/role-permission-workspace";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useRoles } from "@/hooks/use-roles";
import { formatDate } from "@/lib/utils";
import { rolesApi } from "@/services/api/roles";
import type { AppRole } from "@/types/entities";

type Workspace = { mode: "create" | "view" | "edit" | "clone"; role?: AppRole };

export default function RolesPage() {
  const { data, isLoading, isError, refetch } = useRoles({ page: 1, pageSize: 500 });
  const queryClient = useQueryClient();
  const [workspace, setWorkspace] = useState<Workspace>();
  const [deleteTarget, setDeleteTarget] = useState<AppRole>();
  const roles = data?.items ?? [];
  const remove = useMutation({
    mutationFn: (roleId: string) => rolesApi.delete(roleId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["roles"] });
      setDeleteTarget(undefined);
      toast.success("Rol eliminado.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible eliminar el rol."),
  });

  const open = (mode: Workspace["mode"], role?: AppRole) => setWorkspace({ mode, role });
  const columns: ColumnDef<AppRole>[] = useMemo(() => [
    { accessorKey: "name", header: "NOMBRE", cell: ({ row }) => <div><p className="font-medium">{row.original.name}</p><p className="text-xs text-muted-foreground">{row.original.description ?? "Sin descripción"}</p></div> },
    { accessorKey: "isSystemRole", header: "TIPO", cell: ({ row }) => <Badge variant={row.original.isSystemRole ? "secondary" : "outline"}>{row.original.isSystemRole ? "Predefinido" : "Personalizado"}</Badge> },
    { accessorKey: "isActive", header: "ESTADO", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { accessorKey: "createdAt", header: "CREADO", cell: ({ row }) => formatDate(row.original.createdAt) },
    { id: "actions", header: "", cell: ({ row }) => {
      const role = row.original;
      return <DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8" onClick={(event) => event.stopPropagation()}><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end">
        <DropdownMenuItem onClick={() => open("view", role)}><Eye className="mr-2 h-4 w-4" />Ver permisos</DropdownMenuItem>
        {!role.isSystemRole && <DropdownMenuItem onClick={() => open("edit", role)}><Pencil className="mr-2 h-4 w-4" />Editar</DropdownMenuItem>}
        <DropdownMenuItem onClick={() => open("clone", role)}><Copy className="mr-2 h-4 w-4" />Duplicar</DropdownMenuItem>
        {!role.isSystemRole && <DropdownMenuItem className="text-destructive" onClick={() => setDeleteTarget(role)}><Trash2 className="mr-2 h-4 w-4" />Eliminar</DropdownMenuItem>}
      </DropdownMenuContent></DropdownMenu>;
    } },
  ], []);

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return <div className="space-y-6">
    <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between"><div><p className="text-sm font-medium text-primary">Seguridad por funciones</p><h1 className="text-3xl font-semibold tracking-tight">Roles y permisos</h1><p className="mt-1 max-w-3xl text-muted-foreground">Controla qué menús ve cada rol y exactamente qué acciones puede ejecutar dentro de cada pantalla.</p></div><Button onClick={() => open("create")}><Plus className="mr-2 h-4 w-4" />Nuevo rol</Button></header>
    <div className="rounded-2xl border bg-gradient-to-r from-slate-950 to-teal-950 p-5 text-white"><div className="flex items-start gap-4"><span className="rounded-2xl bg-white/10 p-3 text-teal-200"><ShieldCheck className="h-6 w-6" /></span><div><h2 className="font-semibold">Permisos organizados como trabaja Auraly</h2><p className="mt-1 text-sm text-slate-300">Abre un rol para ver sus vistas. Cada vista despliega sus acciones reales: consultar, crear, editar, confirmar, anular, autorizar y las demás capacidades disponibles.</p></div></div></div>
    <DataTable columns={columns} data={roles} searchKey="name" searchPlaceholder="Buscar rol" enableRowSelection={false} onRowClick={(role) => open("view", role)} />

    <Dialog open={Boolean(workspace)} onOpenChange={(value) => !value && setWorkspace(undefined)}><DialogContent className="max-h-[94vh] max-w-[min(96vw,1400px)] overflow-y-auto p-6">
      {workspace && <RolePermissionWorkspace key={`${workspace.mode}-${workspace.role?.roleId ?? "new"}`} roleId={workspace.mode === "view" || workspace.mode === "edit" ? workspace.role?.roleId : undefined} cloneFromId={workspace.mode === "clone" ? workspace.role?.roleId : undefined} embedded readOnly={workspace.mode === "view"} onClose={() => setWorkspace(undefined)} onSaved={() => setWorkspace(undefined)} />}
    </DialogContent></Dialog>

    <Dialog open={Boolean(deleteTarget)} onOpenChange={(value) => !value && setDeleteTarget(undefined)}><DialogContent><DialogHeader><DialogTitle>¿Eliminar este rol?</DialogTitle><DialogDescription>Se eliminará “{deleteTarget?.name}”. Los roles predefinidos del sistema siempre están protegidos.</DialogDescription></DialogHeader><DialogFooter><Button variant="outline" onClick={() => setDeleteTarget(undefined)}>Cancelar</Button><Button variant="destructive" disabled={!deleteTarget || remove.isPending} onClick={() => deleteTarget && remove.mutate(deleteTarget.roleId)}>Eliminar rol</Button></DialogFooter></DialogContent></Dialog>
  </div>;
}