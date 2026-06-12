"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Plus } from "lucide-react";
import { toast } from "sonner";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useRoles } from "@/hooks/use-roles";
import { useUser } from "@/hooks/use-users";
import { formatDate, formatDateTime, getInitials } from "@/lib/utils";
import { usersApi } from "@/services/api";
import { useState } from "react";

export default function UserDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const [selectedRole, setSelectedRole] = useState("");
  const { data: user, isLoading, isError, refetch } = useUser(id);
  const { data: rolesData } = useRoles({ page: 1, pageSize: 100 });
  const {
    data: userRoles = [],
    refetch: refetchUserRoles,
  } = useQuery({
    queryKey: ["users", id, "roles"],
    queryFn: () => usersApi.getRoles(id),
    enabled: !!id,
  });

  if (isLoading) return <PageLoading cards={2} />;
  if (isError || !user) return <PageError onRetry={refetch} />;

  const fullName = `${user.firstName} ${user.lastName}`.trim();
  const availableRoles = (rolesData?.items ?? []).filter(
    (role) => !userRoles.some((userRole) => userRole.roleId === role.roleId)
  );

  const handleAssignRole = async () => {
    if (!selectedRole) return;
    try {
      await usersApi.assignRole(user.userId, { roleId: selectedRole });
      setSelectedRole("");
      await refetchUserRoles();
      toast.success("Rol asignado");
    } catch {
      toast.error("No se pudo asignar el rol");
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/users">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <Avatar className="h-16 w-16">
          <AvatarFallback className="text-xl">{getInitials(fullName)}</AvatarFallback>
        </Avatar>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">{fullName}</h1>
          <p className="text-muted-foreground">{user.email}</p>
          <Badge variant={user.isActive ? "default" : "secondary"} className="mt-2">
            {user.isActive ? "Activo" : "Inactivo"}
          </Badge>
        </div>
      </div>

      <Tabs defaultValue="info">
        <TabsList>
          <TabsTrigger value="info">Informacion</TabsTrigger>
          <TabsTrigger value="roles">Roles</TabsTrigger>
        </TabsList>
        <TabsContent value="info">
          <Card>
            <CardHeader>
              <CardTitle>Datos del usuario</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <div><p className="text-sm font-medium text-muted-foreground">Nombre</p><p>{fullName}</p></div>
              <div><p className="text-sm font-medium text-muted-foreground">Username</p><p>{user.username}</p></div>
              <div><p className="text-sm font-medium text-muted-foreground">Email</p><p>{user.email}</p></div>
              <div><p className="text-sm font-medium text-muted-foreground">Telefono</p><p>{user.phoneNumber ?? "Sin telefono"}</p></div>
              <div><p className="text-sm font-medium text-muted-foreground">Email confirmado</p><p>{user.emailConfirmed ? "Si" : "No"}</p></div>
              <div><p className="text-sm font-medium text-muted-foreground">Ultimo acceso</p><p>{user.lastLoginAt ? formatDateTime(user.lastLoginAt) : "Sin acceso registrado"}</p></div>
              <div><p className="text-sm font-medium text-muted-foreground">Creado</p><p>{formatDate(user.createdAt)}</p></div>
            </CardContent>
          </Card>
        </TabsContent>
        <TabsContent value="roles">
          <Card>
            <CardHeader>
              <div className="flex items-center justify-between">
                <CardTitle>Roles asignados</CardTitle>
                <Dialog>
                  <DialogTrigger asChild>
                    <Button size="sm">
                      <Plus className="mr-2 h-4 w-4" />
                      Asignar rol
                    </Button>
                  </DialogTrigger>
                  <DialogContent>
                    <DialogHeader><DialogTitle>Asignar rol</DialogTitle></DialogHeader>
                    <div className="flex gap-2 pt-4">
                      <Select value={selectedRole} onValueChange={setSelectedRole}>
                        <SelectTrigger><SelectValue placeholder="Seleccionar rol" /></SelectTrigger>
                        <SelectContent>
                          {availableRoles.map((role) => (
                            <SelectItem key={role.roleId} value={role.roleId}>
                              {role.name}
                            </SelectItem>
                          ))}
                          {availableRoles.length === 0 && <SelectItem value="_none" disabled>Sin roles disponibles</SelectItem>}
                        </SelectContent>
                      </Select>
                      <Button onClick={handleAssignRole} disabled={!selectedRole}>Asignar</Button>
                    </div>
                  </DialogContent>
                </Dialog>
              </div>
            </CardHeader>
            <CardContent>
              <div className="flex flex-wrap gap-2">
                {userRoles.map((userRole) => (
                  <Badge key={userRole.userRoleId} variant="secondary">
                    {userRole.role?.name ?? userRole.roleId}
                  </Badge>
                ))}
                {userRoles.length === 0 && <p className="text-sm text-muted-foreground">Sin roles asignados</p>}
              </div>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  );
}
