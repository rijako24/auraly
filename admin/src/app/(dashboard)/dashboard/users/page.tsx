"use client";
import { useMemo } from "react";
import Link from "next/link";
import type { ColumnDef } from "@tanstack/react-table";
import { MoreHorizontal, Plus, Eye, Pencil, UserX } from "lucide-react";
import { DataTable } from "@/components/tables/data-table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";
import type { AppUser } from "@/types/entities";
import { formatDateTime, getInitials } from "@/lib/utils";
import { useUsers } from "@/hooks/use-users";

export default function UsersPage() {
  const { data, isLoading, isError, refetch } = useUsers();
  const users = data?.items ?? [];
  const columns: ColumnDef<AppUser>[] = useMemo(() => [
    { accessorKey: "firstName", header: "Usuario", cell: ({ row }) => { const u = row.original; const fullName = `${u.firstName} ${u.lastName}`; return (<div className="flex items-center gap-3"><Avatar className="h-9 w-9"><AvatarFallback className="text-xs">{getInitials(fullName)}</AvatarFallback></Avatar><div><span className="font-medium">{fullName}</span><p className="text-xs text-muted-foreground">{u.username}</p></div></div>); } },
    { accessorKey: "email", header: "Email" },
    { accessorKey: "phoneNumber", header: "Teléfono", cell: ({ row }) => row.original.phoneNumber ?? "—" },
    { accessorKey: "roles", header: "Roles", cell: ({ row }) => { const roles = row.original.roles?.map((ur) => ur.role?.name).filter(Boolean) ?? []; return (<div className="flex flex-wrap gap-1">{roles.map((name) => <Badge key={name} variant="secondary" className="text-xs">{name}</Badge>)}{roles.length === 0 && "—"}</div>); } },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge variant={row.original.isActive ? "default" : "secondary"}>{row.original.isActive ? "Activo" : "Inactivo"}</Badge> },
    { accessorKey: "lastLoginAt", header: "Último acceso", cell: ({ row }) => row.original.lastLoginAt ? formatDateTime(row.original.lastLoginAt) : "—" },
    { id: "actions", cell: ({ row }) => { const u = row.original; return (<DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" className="h-8 w-8"><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem asChild><Link href={`/dashboard/users/${u.userId}`}><Eye className="mr-2 h-4 w-4" />Ver</Link></DropdownMenuItem><DropdownMenuItem asChild><Link href={`/dashboard/users/${u.userId}`}><Pencil className="mr-2 h-4 w-4" />Editar</Link></DropdownMenuItem>{u.isActive && <DropdownMenuItem><UserX className="mr-2 h-4 w-4" />Desactivar</DropdownMenuItem>}</DropdownMenuContent></DropdownMenu>); } },
  ], []);

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div><h1 className="text-2xl font-semibold tracking-tight">Usuarios</h1><p className="text-muted-foreground">Gestiona los usuarios y sus permisos</p></div>
        <Button asChild><Link href="/dashboard/users/new"><Plus className="mr-2 h-4 w-4" />Nuevo Usuario</Link></Button>
      </div>
      <DataTable columns={columns} data={users} searchKey="email" searchPlaceholder="Buscar por email o nombre..." enableRowSelection={false} />
    </div>
  );
}
