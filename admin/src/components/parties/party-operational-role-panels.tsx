"use client";

import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { CalendarDays, KeyRound, Save, Scissors, ShieldCheck, X } from "lucide-react";
import { toast } from "sonner";
import { ScheduleExceptionsEditor } from "@/components/settings/schedule-exceptions-editor";
import { WorkingHoursEditor } from "@/components/settings/working-hours-editor";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { useRoles } from "@/hooks/use-roles";
import { useServices } from "@/hooks/use-services";
import { employeesApi, usersApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { WorkingHour } from "@/types/entities";

const defaultHours: WorkingHour[] = [{ dayOfWeek: 1, openTime: "08:00", closeTime: "17:00", isActive: true }];

export function PartyEmployeeRolePanel({ employeeId }: { employeeId: string }) {
  const employeeQuery = useQuery({ queryKey: ["employees", employeeId], queryFn: () => employeesApi.getById(employeeId) });
  const hoursQuery = useQuery({ queryKey: ["employees", employeeId, "working-hours"], queryFn: () => employeesApi.getWorkingHours(employeeId) });
  const services = useServices({ page: 1, pageSize: 500 });
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [serviceToAdd, setServiceToAdd] = useState("");
  const [active, setActive] = useState(true);
  const [customSchedule, setCustomSchedule] = useState(false);
  const [workingHours, setWorkingHours] = useState<WorkingHour[]>(defaultHours);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!employeeQuery.data) return;
    setSelectedIds(new Set(employeeQuery.data.serviceIds ?? []));
    setActive(employeeQuery.data.isActive);
  }, [employeeQuery.data]);
  useEffect(() => {
    if (!hoursQuery.data) return;
    setCustomSchedule(!hoursQuery.data.usesBusinessFallback);
    setWorkingHours(hoursQuery.data.workingHours.length ? hoursQuery.data.workingHours : defaultHours);
  }, [hoursQuery.data]);

  const save = async () => {
    const employee = employeeQuery.data;
    if (!employee) return;
    setSaving(true);
    try {
      await employeesApi.update(employeeId, { name: employee.name, isActive: active, serviceIds: [...selectedIds] });
      await employeesApi.updateWorkingHours(employeeId, customSchedule ? workingHours : []);
      await Promise.all([employeeQuery.refetch(), hoursQuery.refetch()]);
      toast.success("Configuración del empleado actualizada");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible actualizar el empleado.");
    } finally {
      setSaving(false);
    }
  };

  if (employeeQuery.isLoading || hoursQuery.isLoading) return <PanelLoading />;
  if (!employeeQuery.data) return <PanelError text="No fue posible cargar la configuración del empleado." />;
  const available = (services.data?.items ?? []).filter((item) => item.isActive && !selectedIds.has(item.serviceId));

  return <div className="space-y-5">
    <PanelHeader icon={Scissors} title="Configuración del empleado" description="Servicios, disponibilidad y estado en el mismo tercero.">
      <div className="flex items-center gap-3"><span className="text-sm text-muted-foreground">Activo</span><Switch checked={active} onCheckedChange={setActive} /></div>
    </PanelHeader>
    <section className="space-y-3 rounded-2xl border p-5">
      <div><h3 className="font-semibold">Servicios asignados</h3><p className="text-sm text-muted-foreground">Agrega los servicios que este empleado puede atender.</p></div>
      <Select value={serviceToAdd} onValueChange={(value) => { setServiceToAdd(""); setSelectedIds((current) => new Set(current).add(value)); }}>
        <SelectTrigger><SelectValue placeholder={services.isLoading ? "Cargando servicios..." : "Agregar servicio"} /></SelectTrigger>
        <SelectContent>{available.map((service) => <SelectItem key={service.serviceId} value={service.serviceId}>{service.serviceName}</SelectItem>)}{available.length === 0 && <SelectItem value="_none" disabled>Sin datos</SelectItem>}</SelectContent>
      </Select>
      <div className="grid gap-3 sm:grid-cols-2">{[...selectedIds].map((serviceId) => { const service = services.data?.items.find((item) => item.serviceId === serviceId); return <div key={serviceId} className="flex items-center justify-between rounded-xl border bg-card p-4"><div><p className="font-medium">{service?.serviceName ?? "Servicio"}</p><p className="text-xs text-muted-foreground">Disponible para asignaciones</p></div><Button type="button" variant="ghost" size="icon" onClick={() => setSelectedIds((current) => { const next = new Set(current); next.delete(serviceId); return next; })}><X className="h-4 w-4" /></Button></div>; })}{selectedIds.size === 0 && <EmptyState text="Sin datos. Agrega los servicios que atenderá esta persona." />}</div>
    </section>
    <section className="space-y-4 rounded-2xl border p-5">
      <div className="flex items-center justify-between gap-4"><div><h3 className="font-semibold">Calendario activo</h3><p className="text-sm text-muted-foreground">Desactivado reutiliza automáticamente el horario del negocio.</p></div><Switch checked={customSchedule} onCheckedChange={setCustomSchedule} /></div>
      {customSchedule ? <WorkingHoursEditor value={workingHours} onChange={setWorkingHours} /> : <div className="flex items-center gap-3 rounded-xl bg-muted/40 p-4 text-sm text-muted-foreground"><CalendarDays className="h-5 w-5 text-primary" />Usará el calendario activo configurado para el negocio.</div>}
    </section>
    <section className="rounded-2xl border p-5"><h3 className="font-semibold">Excepciones del calendario</h3><p className="mb-4 text-sm text-muted-foreground">Cierres o cambios puntuales para fechas específicas.</p><ScheduleExceptionsEditor employeeId={employeeId} /></section>
    <div className="flex justify-end"><Button onClick={save} disabled={saving}><Save className="mr-2 h-4 w-4" />{saving ? "Guardando..." : "Guardar empleado"}</Button></div>
  </div>;
}

export function PartyUserRolePanel({ userId }: { userId: string }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const userQuery = useQuery({ queryKey: ["users", userId], queryFn: () => usersApi.getById(userId) });
  const userRolesQuery = useQuery({ queryKey: ["users", userId, "roles"], queryFn: () => usersApi.getRoles(userId) });
  const roles = useRoles({ page: 1, pageSize: 500 });
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [roleToAdd, setRoleToAdd] = useState("");
  const [active, setActive] = useState(true);
  const [newPassword, setNewPassword] = useState("");
  const [roleError, setRoleError] = useState("");
  const [passwordError, setPasswordError] = useState("");
  const [saving, setSaving] = useState(false);
  const scopedAssignments = useMemo(() => (userRolesQuery.data ?? []).filter((item) => item.businessId === businessId), [userRolesQuery.data, businessId]);

  useEffect(() => { if (userQuery.data) setActive(userQuery.data.isActive); }, [userQuery.data]);
  useEffect(() => { setSelectedIds(new Set(scopedAssignments.map((item) => item.roleId))); }, [scopedAssignments]);

  const save = async () => {
    if (!businessId || !userQuery.data) return;
    const nextRoleError = selectedIds.size === 0 ? "Este campo es requerido" : "";
    const nextPasswordError = newPassword && newPassword.length < 10 ? "Debe tener al menos 10 caracteres" : "";
    setRoleError(nextRoleError); setPasswordError(nextPasswordError);
    if (nextRoleError || nextPasswordError) return;
    setSaving(true);
    try {
      const currentIds = new Set(scopedAssignments.map((item) => item.roleId));
      await Promise.all([
        ...[...selectedIds].filter((roleId) => !currentIds.has(roleId)).map((roleId) => usersApi.assignRole(userId, { roleId, businessId })),
        ...scopedAssignments.filter((item) => !selectedIds.has(item.roleId)).map((item) => usersApi.removeRole(userId, item.roleId, businessId)),
      ]);
      if (active !== userQuery.data.isActive) await (active ? usersApi.activate(userId) : usersApi.deactivate(userId));
      if (newPassword) await usersApi.resetPassword(userId, newPassword);
      setNewPassword("");
      await Promise.all([userQuery.refetch(), userRolesQuery.refetch()]);
      toast.success("Acceso del usuario actualizado");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible actualizar el usuario.");
    } finally {
      setSaving(false);
    }
  };

  if (userQuery.isLoading || userRolesQuery.isLoading) return <PanelLoading />;
  if (!userQuery.data) return <PanelError text="No fue posible cargar la configuración del usuario." />;
  const available = (roles.data?.items ?? []).filter((item) => item.isActive && !selectedIds.has(item.roleId));
  return <div className="space-y-5">
    <PanelHeader icon={KeyRound} title="Configuración del usuario" description="Acceso, contraseña unificada y permisos por rol.">
      <div className="flex items-center gap-3"><span className="text-sm text-muted-foreground">Activo</span><Switch checked={active} onCheckedChange={setActive} /></div>
    </PanelHeader>
    <section className={`space-y-3 rounded-2xl border p-5 ${roleError ? "border-destructive" : ""}`}><div><h3 className="font-semibold">Roles en este negocio</h3><p className="text-sm text-muted-foreground">Definen los menús visibles y las acciones habilitadas en cada vista.</p></div><Select value={roleToAdd} onValueChange={(value) => { setRoleToAdd(""); setRoleError(""); setSelectedIds((current) => new Set(current).add(value)); }}><SelectTrigger aria-invalid={Boolean(roleError)} className={roleError ? "border-destructive" : ""}><SelectValue placeholder={roles.isLoading ? "Cargando roles..." : "Agregar rol"} /></SelectTrigger><SelectContent>{available.map((role) => <SelectItem key={role.roleId} value={role.roleId}>{role.name}</SelectItem>)}{available.length === 0 && <SelectItem value="_none" disabled>Sin datos</SelectItem>}</SelectContent></Select>{roleError && <p className="text-sm text-destructive">{roleError}</p>}<div className="grid gap-3 sm:grid-cols-2">{[...selectedIds].map((roleId) => { const role = roles.data?.items.find((item) => item.roleId === roleId); return <div key={roleId} className="flex items-center justify-between rounded-xl border bg-card p-4"><div><p className="font-medium">{role?.name ?? "Rol"}</p><p className="text-xs text-muted-foreground">{role?.description ?? "Permisos asignados"}</p></div><Button type="button" variant="ghost" size="icon" onClick={() => setSelectedIds((current) => { const next = new Set(current); next.delete(roleId); return next; })}><X className="h-4 w-4" /></Button></div>; })}{selectedIds.size === 0 && <EmptyState text="Sin datos. Agrega al menos un rol para habilitar el acceso." />}</div></section>
    <section className={`space-y-3 rounded-2xl border p-5 ${passwordError ? "border-destructive" : ""}`}><div><Label htmlFor={`reset-${userId}`}>Contraseña de acceso y modo sin conexión POS</Label><p className="text-sm text-muted-foreground">Déjala vacía para conservar la actual. Al cambiarla se actualiza también el acceso sin conexión.</p></div><Input id={`reset-${userId}`} type="password" autoComplete="new-password" value={newPassword} onChange={(event) => { setNewPassword(event.target.value); setPasswordError(""); }} aria-invalid={Boolean(passwordError)} className={passwordError ? "border-destructive" : ""} placeholder="Nueva contraseña" />{passwordError && <p className="text-sm text-destructive">{passwordError}</p>}</section>
    <div className="flex justify-end"><Button onClick={save} disabled={saving}><ShieldCheck className="mr-2 h-4 w-4" />{saving ? "Guardando..." : "Guardar acceso"}</Button></div>
  </div>;
}

function PanelHeader({ icon: Icon, title, description, children }: { icon: typeof Scissors; title: string; description: string; children: React.ReactNode }) { return <div className="flex flex-col justify-between gap-4 rounded-2xl bg-gradient-to-r from-slate-950 to-teal-950 p-5 text-white sm:flex-row sm:items-center"><div className="flex items-center gap-3"><span className="rounded-xl bg-white/10 p-3 text-teal-300"><Icon className="h-5 w-5" /></span><div><h3 className="font-semibold">{title}</h3><p className="text-sm text-slate-300">{description}</p></div></div>{children}</div>; }
function EmptyState({ text }: { text: string }) { return <div className="col-span-full rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">{text}</div>; }
function PanelLoading() { return <div className="rounded-2xl border p-8 text-center text-sm text-muted-foreground">Cargando configuración...</div>; }
function PanelError({ text }: { text: string }) { return <div className="rounded-2xl border border-destructive/30 bg-destructive/5 p-5 text-sm text-destructive">{text}</div>; }