"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, KeyRound, Loader2, Plus } from "lucide-react";
import { toast } from "sonner";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
import { posApprovalClient } from "@/services/pos/pos-approval-client";
import { useAuthStore } from "@/stores/auth-store";
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
          <div className="space-y-5"><Card>
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
          </Card><SupervisorCredentialCard userId={user.userId}/></div>
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

function SupervisorCredentialCard({userId}:{userId:string}){
  const canManage=useAuthStore(state=>state.user?.permissions.includes("pos.approvals.manage_credential")??false);
  const status=useQuery({queryKey:["supervisor-credential",userId],queryFn:()=>posApprovalClient.userCredentialStatus(userId),enabled:canManage});
  const [secret,setSecret]=useState(""),[confirmation,setConfirmation]=useState(""),[validity,setValidity]=useState<"once"|"8"|"168"|"always">("always"),[saving,setSaving]=useState(false);
  if(!canManage)return null;
  const save=async()=>{setSaving(true);try{await posApprovalClient.configureUserCredential(userId,secret,validity==="once"||validity==="always"?null:Number(validity) as 8|168,validity==="once");setSecret("");setConfirmation("");await status.refetch();toast.success(status.data?.isConfigured?"Credencial secundaria reiniciada":"Credencial secundaria configurada")}catch(error){toast.error(error instanceof Error?error.message:"No fue posible configurar la credencial")}finally{setSaving(false)}};
  const revoke=async()=>{if(!window.confirm("¿Revocar la credencial secundaria de este usuario?"))return;setSaving(true);try{await posApprovalClient.revokeUserCredential(userId);await status.refetch();toast.success("Credencial revocada")}catch(error){toast.error(error instanceof Error?error.message:"No fue posible revocar la credencial")}finally{setSaving(false)}};
  return <Card><CardHeader><CardTitle className="flex items-center gap-2"><KeyRound className="h-5 w-5"/>Autorización de supervisor</CardTitle><p className="text-sm text-muted-foreground">La credencial secundaria autoriza una sola acción cuando el cajero no tiene el permiso. El usuario debe tener permiso para aprobar y para la acción solicitada.</p></CardHeader><CardContent className="space-y-4">{status.data?.isConfigured&&<div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border bg-emerald-50 p-4 text-sm"><div><strong>Credencial activa</strong><p className="text-muted-foreground">{status.data.isOneTime?"Válida para una sola autorización":status.data.validUntil?`Vence ${new Date(status.data.validUntil).toLocaleString("es-CO")}`:"Sin vencimiento; permanece hasta revocarla"}</p></div><Button variant="destructive" size="sm" disabled={saving} onClick={()=>void revoke()}>Revocar</Button></div>}<div className="grid gap-4 md:grid-cols-3"><div className="space-y-2"><Label>Nueva credencial</Label><Input type="password" minLength={6} maxLength={32} value={secret} onChange={event=>setSecret(event.target.value)} autoComplete="new-password"/></div><div className="space-y-2"><Label>Confirmar</Label><Input type="password" minLength={6} maxLength={32} value={confirmation} onChange={event=>setConfirmation(event.target.value)} autoComplete="new-password"/></div><div className="space-y-2"><Label>Vigencia</Label><Select value={validity} onValueChange={value=>setValidity(value as typeof validity)}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="once">Un solo uso</SelectItem><SelectItem value="8">8 horas</SelectItem><SelectItem value="168">1 semana</SelectItem><SelectItem value="always">Siempre</SelectItem></SelectContent></Select></div></div>{confirmation&&secret!==confirmation&&<p className="text-sm text-destructive">Las credenciales no coinciden.</p>}<div className="flex justify-end"><Button disabled={saving||secret.length<6||secret!==confirmation} onClick={()=>void save()}>{saving&&<Loader2 className="mr-2 h-4 w-4 animate-spin"/>}{status.data?.isConfigured?"Reiniciar credencial":"Guardar credencial"}</Button></div></CardContent></Card>;
}
