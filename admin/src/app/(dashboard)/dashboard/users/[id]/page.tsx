"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
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
import type { AppUser, UserRole, AppRole, AuditLog } from "@/types/entities";
import { formatDate, formatDateTime, getInitials } from "@/lib/utils";

const MOCK_USERS: Record<string, AppUser & { roles?: (UserRole & { role?: AppRole })[] }> = {
  u1: {
    userId: "u1",
    tenantId: "t1",
    username: "admin.mimos",
    email: "admin@mimosbabyspa.com",
    firstName: "María",
    lastName: "García",
    phoneNumber: "+57 300 123 4567",
    avatarUrl: null,
    isActive: true,
    emailConfirmed: true,
    lastLoginAt: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
    createdAt: "2025-01-01T08:00:00Z",
    updatedAt: null,
    roles: [
      { userRoleId: "ur1", userId: "u1", roleId: "r1", businessId: null, assignedAt: "2025-01-01", role: { roleId: "r1", name: "Admin", description: "Administrador", isSystemRole: true, isActive: true } as AppRole },
    ],
  },
  u2: {
    userId: "u2",
    tenantId: "t1",
    username: "carlos.manager",
    email: "carlos@mimosbabyspa.com",
    firstName: "Carlos",
    lastName: "Rodríguez",
    phoneNumber: "+57 310 234 5678",
    avatarUrl: null,
    isActive: true,
    emailConfirmed: true,
    lastLoginAt: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
    createdAt: "2025-01-15T10:00:00Z",
    updatedAt: null,
    roles: [
      { userRoleId: "ur2", userId: "u2", roleId: "r2", businessId: null, assignedAt: "2025-01-15", role: { roleId: "r2", name: "Manager", description: "Gerente", isSystemRole: false, isActive: true } as AppRole },
    ],
  },
};

const MOCK_ROLES: AppRole[] = [
  { roleId: "r1", tenantId: null, name: "Admin", description: "Administrador", isSystemRole: true, isActive: true, createdAt: "2025-01-01", updatedAt: null },
  { roleId: "r2", tenantId: null, name: "Manager", description: "Gerente", isSystemRole: false, isActive: true, createdAt: "2025-01-01", updatedAt: null },
  { roleId: "r3", tenantId: null, name: "Staff", description: "Personal", isSystemRole: false, isActive: true, createdAt: "2025-01-01", updatedAt: null },
  { roleId: "r4", tenantId: null, name: "ReadOnly", description: "Solo lectura", isSystemRole: false, isActive: true, createdAt: "2025-01-01", updatedAt: null },
];

const MOCK_AUDIT_LOGS: (AuditLog & { user?: AppUser })[] = [
  { auditLogId: "a1", userId: "u1", tenantId: "t1", businessId: null, action: "Login", entityType: "User", entityId: "u1", oldValues: null, newValues: null, ipAddress: "192.168.1.1", userAgent: "Chrome/120", correlationId: null, timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(), user: { firstName: "María", lastName: "García" } as AppUser },
  { auditLogId: "a2", userId: "u1", tenantId: "t1", businessId: "bus-1", action: "Update", entityType: "Reservation", entityId: "res-1", oldValues: '{"status":"Pending"}', newValues: '{"status":"Confirmed"}', ipAddress: "192.168.1.1", userAgent: "Chrome/120", correlationId: null, timestamp: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(), user: { firstName: "María", lastName: "García" } as AppUser },
  { auditLogId: "a3", userId: "u1", tenantId: "t1", businessId: null, action: "Update", entityType: "AppUser", entityId: "u2", oldValues: null, newValues: '{"phoneNumber":"+57 310 234 5678"}', ipAddress: "192.168.1.1", userAgent: "Chrome/120", correlationId: null, timestamp: new Date(Date.now() - 5 * 60 * 60 * 1000).toISOString(), user: { firstName: "María", lastName: "García" } as AppUser },
];

export default function UserDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const [user, setUser] = useState(MOCK_USERS[id] ?? Object.values(MOCK_USERS)[0]);
  const [selectedRole, setSelectedRole] = useState<string>("");

  const fullName = user ? `${user.firstName} ${user.lastName}` : "";
  const userAuditLogs = MOCK_AUDIT_LOGS.filter((log) => log.userId === id);

  const handleAssignRole = () => {
    if (!selectedRole || !user) return;
    const role = MOCK_ROLES.find((r) => r.roleId === selectedRole);
    if (role && user.roles && !user.roles.some((ur) => ur.role?.roleId === role.roleId)) {
      setUser({
        ...user,
        roles: [
          ...user.roles,
          { userRoleId: `ur-new-${Date.now()}`, userId: user.userId, roleId: role.roleId, businessId: null, assignedAt: new Date().toISOString(), role },
        ],
      });
      setSelectedRole("");
    }
  };

  if (!user) {
    return (
      <div className="space-y-6">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/users">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <p className="text-muted-foreground">Usuario no encontrado.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/users">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <div className="flex items-center gap-4">
            <Avatar className="h-16 w-16">
              <AvatarFallback className="text-xl">{getInitials(fullName)}</AvatarFallback>
            </Avatar>
            <div>
              <h1 className="text-2xl font-semibold tracking-tight">{fullName}</h1>
              <p className="text-muted-foreground">{user.email}</p>
              <Badge variant={user.isActive ? "default" : "secondary"} className="mt-2">
                {user.isActive ? "Activo" : "Inactivo"}
              </Badge>
            </div>
          </div>
        </div>
      </div>

      <Tabs defaultValue="info">
        <TabsList>
          <TabsTrigger value="info">Información</TabsTrigger>
          <TabsTrigger value="roles">Roles</TabsTrigger>
          <TabsTrigger value="activity">Actividad</TabsTrigger>
        </TabsList>
        <TabsContent value="info">
          <Card>
            <CardHeader>
              <CardTitle>Datos del usuario</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-4 sm:grid-cols-2">
              <div>
                <p className="text-sm font-medium text-muted-foreground">Nombre</p>
                <p>{fullName}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Username</p>
                <p>{user.username}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Email</p>
                <p>{user.email}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Teléfono</p>
                <p>{user.phoneNumber ?? "—"}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Email confirmado</p>
                <p>{user.emailConfirmed ? "Sí" : "No"}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Último acceso</p>
                <p>{user.lastLoginAt ? formatDateTime(user.lastLoginAt) : "—"}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Creado</p>
                <p>{formatDate(user.createdAt)}</p>
              </div>
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
                    <DialogHeader>
                      <DialogTitle>Asignar rol</DialogTitle>
                    </DialogHeader>
                    <div className="flex gap-2 pt-4">
                      <Select value={selectedRole} onValueChange={setSelectedRole}>
                        <SelectTrigger>
                          <SelectValue placeholder="Seleccionar rol" />
                        </SelectTrigger>
                        <SelectContent>
                          {MOCK_ROLES.filter((r) => !user.roles?.some((ur) => ur.role?.roleId === r.roleId)).map((r) => (
                            <SelectItem key={r.roleId} value={r.roleId}>
                              {r.name} - {r.description}
                            </SelectItem>
                          ))}
                          {MOCK_ROLES.filter((r) => !user.roles?.some((ur) => ur.role?.roleId === r.roleId)).length === 0 && (
                            <SelectItem value="_none" disabled>
                              Todos los roles ya asignados
                            </SelectItem>
                          )}
                        </SelectContent>
                      </Select>
                      <Button onClick={handleAssignRole} disabled={!selectedRole}>
                        Asignar
                      </Button>
                    </div>
                  </DialogContent>
                </Dialog>
              </div>
            </CardHeader>
            <CardContent>
              <div className="flex flex-wrap gap-2">
                {user.roles?.map((ur) => (
                  <Badge key={ur.userRoleId} variant="secondary">
                    {ur.role?.name ?? "—"}
                  </Badge>
                ))}
                {(!user.roles || user.roles.length === 0) && (
                  <p className="text-sm text-muted-foreground">Sin roles asignados</p>
                )}
              </div>
            </CardContent>
          </Card>
        </TabsContent>
        <TabsContent value="activity">
          <Card>
            <CardHeader>
              <CardTitle>Actividad reciente</CardTitle>
              <p className="text-sm text-muted-foreground">
                Registro de auditoría de este usuario
              </p>
            </CardHeader>
            <CardContent>
              <div className="space-y-4">
                {userAuditLogs.map((log) => (
                  <div
                    key={log.auditLogId}
                    className="flex flex-col gap-1 rounded-md border p-3 text-sm"
                  >
                    <div className="flex items-center justify-between">
                      <span className="font-medium">{log.action}</span>
                      <span className="text-muted-foreground">
                        {formatDateTime(log.timestamp)}
                      </span>
                    </div>
                    <p className="text-muted-foreground">
                      {log.entityType} {log.entityId ? `#${log.entityId}` : ""}
                    </p>
                    {log.ipAddress && (
                      <p className="text-xs text-muted-foreground">IP: {log.ipAddress}</p>
                    )}
                  </div>
                ))}
                {userAuditLogs.length === 0 && (
                  <p className="text-center text-muted-foreground py-8">
                    Sin actividad registrada
                  </p>
                )}
              </div>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  );
}
