"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useRole } from "@/hooks/use-roles";
import { rolesApi } from "@/services/api";

export default function RoleDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: role, isLoading, isError, refetch } = useRole(id);
  const { data: rolePermissionsData } = useQuery({
    queryKey: ["roles", id, "permissions"],
    queryFn: () => rolesApi.listRolePermissions(id, { page: 1, pageSize: 500 }),
    enabled: !!id,
  });

  if (isLoading) return <PageLoading cards={1} />;
  if (isError || !role) return <PageError onRetry={refetch} />;

  const permissions = rolePermissionsData?.items ?? role.permissions?.map((permission) => ({
    rolePermissionId: permission.permissionId,
    roleId: role.roleId,
    permissionId: permission.permissionId,
    assignedAt: permission.createdAt,
    permission,
  })) ?? [];

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/roles"><ArrowLeft className="h-4 w-4" /></Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">{role.name}</h1>
          <p className="text-muted-foreground">{role.description ?? "Sin descripcion"}</p>
        </div>
        <Badge variant={role.isActive ? "default" : "secondary"}>{role.isActive ? "Activo" : "Inactivo"}</Badge>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Permisos asignados</CardTitle>
          <p className="text-sm text-muted-foreground">Permisos retornados por la API para este rol</p>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap gap-2">
            {permissions.map((rolePermission) => (
              <Badge key={rolePermission.rolePermissionId} variant="secondary">
                {rolePermission.permission
                  ? `${rolePermission.permission.module}.${rolePermission.permission.action}`
                  : rolePermission.permissionId}
              </Badge>
            ))}
            {permissions.length === 0 && <p className="text-sm text-muted-foreground">Sin permisos asignados.</p>}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
