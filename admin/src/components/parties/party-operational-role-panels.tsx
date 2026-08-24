"use client";

import { useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarDays, CircleDollarSign, KeyRound, ReceiptText, Scissors, X } from "lucide-react";
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
import { useCities } from "@/hooks/use-parties";
import { employeesApi, usersApi } from "@/services/api";
import type { PartySiteDetail } from "@/services/api/parties";
import { taxationApi } from "@/services/api/taxation";
import { receivablesApi } from "@/services/api/receivables";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { WorkingHour } from "@/types/entities";

const defaultHours: WorkingHour[] = [{ dayOfWeek: 1, openTime: "08:00", closeTime: "17:00", isActive: true }];

type RegisterSave = (key: string, handler: () => Promise<void>) => () => void;

const creditMoney = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PartyCustomerCreditRolePanel({ customerId, editing, registerSave }: { customerId: string; editing: boolean; registerSave: RegisterSave }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canRead = permissions.has("receivables.read");
  const canManage = permissions.has("receivables.credit.manage");
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ["customer-credit", businessId, customerId], queryFn: () => receivablesApi.getCreditProfile(customerId), enabled: Boolean(businessId && customerId && canRead), retry: false });
  const [enabled, setEnabled] = useState(false);
  const [limit, setLimit] = useState("");
  const [dueDays, setDueDays] = useState("30");
  useEffect(() => {
    if (!profile.data) return;
    setEnabled(profile.data.isCreditEnabled);
    setLimit(profile.data.creditLimit == null ? "" : String(profile.data.creditLimit));
    setDueDays(String(profile.data.defaultDueDays));
  }, [profile.data]);
  const save = async () => {
    if (!businessId || !canManage) return;
    const days = Number(dueDays);
    const parsedLimit = limit.trim() ? Number(limit) : null;
    if (!Number.isInteger(days) || days < 0 || days > 3650 || (parsedLimit != null && (!Number.isFinite(parsedLimit) || parsedLimit < 0)))
      throw new Error("Revisa el cupo y el plazo de crédito.");
    await receivablesApi.updateCreditProfile(customerId, { businessId, creditLimit: parsedLimit, defaultDueDays: days, isCreditEnabled: enabled });
    await queryClient.invalidateQueries({ queryKey: ["customer-credit", businessId, customerId] });
  };
  useEffect(() => registerSave(`customer-credit-${customerId}`, save), [registerSave, customerId, businessId, canManage, enabled, limit, dueDays]);

  if (!canRead) return <PanelError text="No tienes permiso para consultar la configuración de cartera de este cliente." />;
  if (profile.isLoading) return <PanelLoading />;
  if (!profile.data) return <PanelError text="No fue posible cargar la configuración de crédito." />;
  return <div className="space-y-4">
    <PanelHeader icon={CircleDollarSign} title="Crédito y cartera" description="Define si este cliente puede dejar saldo pendiente al facturar.">
      <div className="flex items-center gap-3"><span className="text-sm">Crédito habilitado</span><Switch checked={enabled} onCheckedChange={setEnabled} disabled={!editing || !canManage}/></div>
    </PanelHeader>
    <section className="grid gap-4 rounded-2xl border p-5 md:grid-cols-2">
      <div className="space-y-2"><Label>Cupo de crédito</Label>{editing&&canManage?<Input type="number" min="0" step="1" value={limit} onChange={(event)=>setLimit(event.target.value)} placeholder="Sin límite"/>:<p className="rounded-xl border bg-muted/20 p-3 font-medium">{profile.data.creditLimit == null ? "Sin límite configurado" : creditMoney.format(profile.data.creditLimit)}</p>}<p className="text-xs text-muted-foreground">Vacío significa sin límite monetario; la habilitación sigue siendo obligatoria.</p></div>
      <div className="space-y-2"><Label>Plazo predeterminado</Label>{editing&&canManage?<Input type="number" min="0" max="3650" step="1" value={dueDays} onChange={(event)=>setDueDays(event.target.value)}/>:<p className="rounded-xl border bg-muted/20 p-3 font-medium">{profile.data.defaultDueDays} días</p>}<p className="text-xs text-muted-foreground">Se usa para calcular el vencimiento de la cuenta por cobrar.</p></div>
      <div><Label>Saldo pendiente</Label><p className="mt-2 text-lg font-semibold">{creditMoney.format(profile.data.outstandingAmount)}</p></div>
      <div><Label>Cupo disponible</Label><p className="mt-2 text-lg font-semibold">{profile.data.availableCredit == null ? "Sin límite" : creditMoney.format(profile.data.availableCredit)}</p></div>
      {!canManage&&editing&&<p className="md:col-span-2 text-sm text-amber-700">Puedes editar la ficha, pero no las condiciones de cartera porque falta el permiso correspondiente.</p>}
    </section>
  </div>;
}

export function PartySupplierTaxRolePanel({ supplierId, editing, primarySite, registerSave }: { supplierId: string; editing: boolean; primarySite: PartySiteDetail | null; registerSave: RegisterSave }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ["withholding-profile", businessId, supplierId], queryFn: () => taxationApi.getProfile(supplierId), enabled: Boolean(businessId && supplierId), retry: false });
  const rules = useQuery({ queryKey: ["withholding-rules", businessId, "supplier-profile-options"], queryFn: () => taxationApi.listRules(false), enabled: Boolean(businessId) });
  const cities = useCities(primarySite?.administrativeDivisionId ?? "");
  const [responsibilities, setResponsibilities] = useState<Set<string>>(new Set());
  const [responsibilityToAdd, setResponsibilityToAdd] = useState("");

  useEffect(() => {
    setResponsibilities(new Set(profile.data?.responsibilities ?? []));
  }, [profile.data]);

  const save = async () => {
    if (!businessId) return;
    const cityCode = cities.data?.find((city) => city.cityId === primarySite?.cityId)?.code ?? profile.data?.jurisdictionCode ?? null;
    await taxationApi.saveProfile({ businessId, counterpartyId: supplierId, appliesWithholding: true, responsibilities: [...responsibilities], jurisdictionCode: cityCode });
    await queryClient.invalidateQueries({ queryKey: ["withholding-profile", businessId, supplierId] });
  };
  useEffect(() => registerSave(`supplier-tax-${supplierId}`, save), [registerSave, supplierId, businessId, responsibilities, cities.data, primarySite?.cityId, profile.data?.jurisdictionCode]);

  const catalog = [...new Set((rules.data ?? []).flatMap((rule) => rule.requiredResponsibilities))].sort();
  const available = catalog.filter((code) => !responsibilities.has(code));
  const city = cities.data?.find((item) => item.cityId === primarySite?.cityId);

  return <div className="space-y-4">
    <PanelHeader icon={ReceiptText} title="Perfil tributario" description="Las responsabilidades se toman del catálogo configurado en Contabilidad y la jurisdicción de la ciudad principal."><Badge variant="secondary">Configuración automática</Badge></PanelHeader>
    {profile.isLoading ? <PanelLoading /> : <section className="space-y-4 rounded-2xl border p-5">
      <div className="grid gap-4 md:grid-cols-2">
        <div className={editing ? "space-y-2" : "grid content-start grid-rows-[auto_3rem_auto] gap-2"}><Label>Responsabilidades tributarias</Label>{editing&&<Select value={responsibilityToAdd} onValueChange={(value) => { setResponsibilityToAdd(""); setResponsibilities((current) => new Set(current).add(value)); }}><SelectTrigger><SelectValue placeholder={rules.isLoading ? "Cargando catálogo..." : "Agregar responsabilidad"}/></SelectTrigger><SelectContent>{available.map((code) => <SelectItem key={code} value={code}>{code}</SelectItem>)}{available.length === 0 && <SelectItem value="_none" disabled>Sin responsabilidades disponibles</SelectItem>}</SelectContent></Select>}<div className="flex min-h-12 flex-wrap items-center gap-2 rounded-xl border bg-muted/10 p-3">{[...responsibilities].map((code) => <Badge key={code} variant="secondary" className="gap-1">{code}{editing&&<button type="button" aria-label={`Quitar ${code}`} onClick={() => setResponsibilities((current) => { const next = new Set(current); next.delete(code); return next; })}><X className="h-3 w-3"/></button>}</Badge>)}{responsibilities.size===0&&<span className="text-sm text-muted-foreground">Sin responsabilidades seleccionadas</span>}</div><p className="text-xs text-muted-foreground">Las opciones se crean al configurar reglas de retención en Contabilidad.</p></div>
        <div className="grid content-start grid-rows-[auto_3rem_auto] gap-2"><Label>Ciudad o jurisdicción tributaria</Label><Input className="h-12" value={city ? `${city.name} (${city.code})` : primarySite ? "Cargando ciudad..." : "Sin sede principal"} readOnly/><p className="text-xs text-muted-foreground">Se sincroniza automáticamente con la ciudad de la sede principal.</p></div>
      </div>
    </section>}
  </div>;
}

export function PartyEmployeeRolePanel({ employeeId, editing, registerSave }: { employeeId: string; editing: boolean; registerSave: RegisterSave }) {
  const employeeQuery = useQuery({ queryKey: ["employees", employeeId], queryFn: () => employeesApi.getById(employeeId) });
  const hoursQuery = useQuery({ queryKey: ["employees", employeeId, "working-hours"], queryFn: () => employeesApi.getWorkingHours(employeeId) });
  const services = useServices({ page: 1, pageSize: 500 });
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [serviceToAdd, setServiceToAdd] = useState("");
  const [active, setActive] = useState(true);
  const [customSchedule, setCustomSchedule] = useState(false);
  const [workingHours, setWorkingHours] = useState<WorkingHour[]>(defaultHours);

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
    await employeesApi.update(employeeId, { name: employee.name, isActive: active, serviceIds: [...selectedIds] });
    await employeesApi.updateWorkingHours(employeeId, customSchedule ? workingHours : []);
    await Promise.all([employeeQuery.refetch(), hoursQuery.refetch()]);
  };
  useEffect(() => registerSave(`employee-${employeeId}`, save), [registerSave, employeeId, employeeQuery.data, active, selectedIds, customSchedule, workingHours]);

  if (employeeQuery.isLoading || hoursQuery.isLoading) return <PanelLoading />;
  if (!employeeQuery.data) return <PanelError text="No fue posible cargar la configuración del empleado." />;
  const available = (services.data?.items ?? []).filter((item) => item.isActive && !selectedIds.has(item.serviceId));

  return <div className="space-y-5">
    <PanelHeader icon={Scissors} title="Configuración del empleado" description="Servicios, disponibilidad y estado en el mismo tercero.">
      <div className="flex items-center gap-3"><span className="text-sm text-muted-foreground">Activo</span><Switch checked={active} onCheckedChange={setActive} disabled={!editing}/></div>
    </PanelHeader>
    <section className="space-y-3 rounded-2xl border p-5">
      <div><h3 className="font-semibold">Servicios asignados</h3><p className="text-sm text-muted-foreground">Agrega los servicios que este empleado puede atender.</p></div>
      <Select value={serviceToAdd} onValueChange={(value) => { setServiceToAdd(""); setSelectedIds((current) => new Set(current).add(value)); }} disabled={!editing}>
        <SelectTrigger><SelectValue placeholder={services.isLoading ? "Cargando servicios..." : "Agregar servicio"} /></SelectTrigger>
        <SelectContent>{available.map((service) => <SelectItem key={service.serviceId} value={service.serviceId}>{service.serviceName}</SelectItem>)}{available.length === 0 && <SelectItem value="_none" disabled>Sin datos</SelectItem>}</SelectContent>
      </Select>
      <div className="grid gap-3 sm:grid-cols-2">{[...selectedIds].map((serviceId) => { const service = services.data?.items.find((item) => item.serviceId === serviceId); return <div key={serviceId} className="flex items-center justify-between rounded-xl border bg-card p-4"><div><p className="font-medium">{service?.serviceName ?? "Servicio"}</p><p className="text-xs text-muted-foreground">Disponible para asignaciones</p></div>{editing&&<Button type="button" variant="ghost" size="icon" onClick={() => setSelectedIds((current) => { const next = new Set(current); next.delete(serviceId); return next; })}><X className="h-4 w-4" /></Button>}</div>; })}{selectedIds.size === 0 && <EmptyState text="Sin datos. Agrega los servicios que atenderá esta persona." />}</div>
    </section>
    <section className="space-y-4 rounded-2xl border p-5">
      <div className="flex items-center justify-between gap-4"><div><h3 className="font-semibold">Calendario activo</h3><p className="text-sm text-muted-foreground">Desactivado reutiliza automáticamente el horario del negocio.</p></div><Switch checked={customSchedule} onCheckedChange={setCustomSchedule} disabled={!editing}/></div>
      {customSchedule ? editing?<WorkingHoursEditor value={workingHours} onChange={setWorkingHours}/>:<div className="rounded-xl border bg-muted/20 p-4 text-sm text-muted-foreground">Horario personalizado configurado.</div> : <div className="flex items-center gap-3 rounded-xl bg-muted/40 p-4 text-sm text-muted-foreground"><CalendarDays className="h-5 w-5 text-primary" />Usará el calendario activo configurado para el negocio.</div>}
    </section>
    {editing&&<section className="rounded-2xl border p-5"><h3 className="font-semibold">Excepciones del calendario</h3><p className="mb-4 text-sm text-muted-foreground">Cierres o cambios puntuales para fechas específicas.</p><ScheduleExceptionsEditor employeeId={employeeId}/></section>}
  </div>;
}

export function PartyUserRolePanel({ userId, editing, registerSave }: { userId: string; editing: boolean; registerSave: RegisterSave }) {
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
  const scopedAssignments = useMemo(() => (userRolesQuery.data ?? []).filter((item) => item.businessId === businessId), [userRolesQuery.data, businessId]);

  useEffect(() => { if (userQuery.data) setActive(userQuery.data.isActive); }, [userQuery.data]);
  useEffect(() => { setSelectedIds(new Set(scopedAssignments.map((item) => item.roleId))); }, [scopedAssignments]);

  const save = async () => {
    if (!businessId || !userQuery.data) return;
    const nextRoleError = selectedIds.size === 0 ? "Este campo es requerido" : "";
    const nextPasswordError = newPassword && newPassword.length < 10 ? "Debe tener al menos 10 caracteres" : "";
    setRoleError(nextRoleError); setPasswordError(nextPasswordError);
    if (nextRoleError || nextPasswordError) throw new Error(nextRoleError || nextPasswordError);
    const currentIds = new Set(scopedAssignments.map((item) => item.roleId));
    await Promise.all([
      ...[...selectedIds].filter((roleId) => !currentIds.has(roleId)).map((roleId) => usersApi.assignRole(userId, { roleId, businessId })),
      ...scopedAssignments.filter((item) => !selectedIds.has(item.roleId)).map((item) => usersApi.removeRole(userId, item.roleId, businessId)),
    ]);
    if (active !== userQuery.data.isActive) await (active ? usersApi.activate(userId) : usersApi.deactivate(userId));
    if (newPassword) await usersApi.resetPassword(userId, newPassword);
    setNewPassword("");
    await Promise.all([userQuery.refetch(), userRolesQuery.refetch()]);
  };
  useEffect(() => registerSave(`user-${userId}`, save), [registerSave, userId, businessId, userQuery.data, scopedAssignments, selectedIds, active, newPassword]);

  if (userQuery.isLoading || userRolesQuery.isLoading) return <PanelLoading />;
  if (!userQuery.data) return <PanelError text="No fue posible cargar la configuración del usuario." />;
  const available = (roles.data?.items ?? []).filter((item) => item.isActive && !selectedIds.has(item.roleId));
  return <div className="space-y-5">
    <PanelHeader icon={KeyRound} title="Configuración del usuario" description="Acceso, contraseña unificada y permisos por rol.">
      <div className="flex items-center gap-3"><span className="text-sm text-muted-foreground">Activo</span><Switch checked={active} onCheckedChange={setActive} disabled={!editing}/></div>
    </PanelHeader>
    <section className={`space-y-3 rounded-2xl border p-5 ${roleError ? "border-destructive" : ""}`}><div><h3 className="font-semibold">Roles en este negocio</h3><p className="text-sm text-muted-foreground">Definen los menús visibles y las acciones habilitadas en cada vista.</p></div>{editing&&<Select value={roleToAdd} onValueChange={(value) => { setRoleToAdd(""); setRoleError(""); setSelectedIds((current) => new Set(current).add(value)); }}><SelectTrigger aria-invalid={Boolean(roleError)} className={roleError ? "border-destructive" : ""}><SelectValue placeholder={roles.isLoading ? "Cargando roles..." : "Agregar rol"} /></SelectTrigger><SelectContent>{available.map((role) => <SelectItem key={role.roleId} value={role.roleId}>{role.name}</SelectItem>)}{available.length === 0 && <SelectItem value="_none" disabled>Sin datos</SelectItem>}</SelectContent></Select>}{roleError && <p className="text-sm text-destructive">{roleError}</p>}<div className="grid gap-3 sm:grid-cols-2">{[...selectedIds].map((roleId) => { const role = roles.data?.items.find((item) => item.roleId === roleId); return <div key={roleId} className="flex items-center justify-between rounded-xl border bg-card p-4"><div><p className="font-medium">{role?.name ?? "Rol"}</p><p className="text-xs text-muted-foreground">{role?.description ?? "Permisos asignados"}</p></div>{editing&&<Button type="button" variant="ghost" size="icon" onClick={() => setSelectedIds((current) => { const next = new Set(current); next.delete(roleId); return next; })}><X className="h-4 w-4" /></Button>}</div>; })}{selectedIds.size === 0 && <EmptyState text="Sin datos. Agrega al menos un rol para habilitar el acceso." />}</div></section>
    {editing&&<section className={`space-y-3 rounded-2xl border p-5 ${passwordError ? "border-destructive" : ""}`}><div><Label htmlFor={`reset-${userId}`}>Contraseña de acceso y modo sin conexión POS</Label><p className="text-sm text-muted-foreground">Déjala vacía para conservar la actual. Al cambiarla se actualiza también el acceso sin conexión.</p></div><Input id={`reset-${userId}`} type="password" autoComplete="new-password" value={newPassword} onChange={(event) => { setNewPassword(event.target.value); setPasswordError(""); }} aria-invalid={Boolean(passwordError)} className={passwordError ? "border-destructive" : ""} placeholder="Nueva contraseña" />{passwordError && <p className="text-sm text-destructive">{passwordError}</p>}</section>}
  </div>;
}

function PanelHeader({ icon: Icon, title, description, children }: { icon: typeof Scissors; title: string; description: string; children: React.ReactNode }) { return <div className="flex flex-col justify-between gap-4 rounded-2xl bg-gradient-to-r from-slate-950 to-teal-950 p-5 text-white sm:flex-row sm:items-center"><div className="flex items-center gap-3"><span className="rounded-xl bg-white/10 p-3 text-teal-300"><Icon className="h-5 w-5" /></span><div><h3 className="font-semibold">{title}</h3><p className="text-sm text-slate-300">{description}</p></div></div>{children}</div>; }
function EmptyState({ text }: { text: string }) { return <div className="col-span-full rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">{text}</div>; }
function PanelLoading() { return <div className="rounded-2xl border p-8 text-center text-sm text-muted-foreground">Cargando configuración...</div>; }
function PanelError({ text }: { text: string }) { return <div className="rounded-2xl border border-destructive/30 bg-destructive/5 p-5 text-sm text-destructive">{text}</div>; }
