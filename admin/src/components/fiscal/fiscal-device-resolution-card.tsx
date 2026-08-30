"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, Cpu, Globe2, Loader2, RefreshCw, Save, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  fiscalConfigurationApi,
  type FiscalDeviceSeriesWorkspace,
  type FiscalOnboardingConfiguration,
} from "@/services/api/fiscal-configuration";

export function FiscalDeviceResolutionCard({ businessId, canManage }: { businessId: string; canManage: boolean }) {
  const [workspace, setWorkspace] = useState<FiscalDeviceSeriesWorkspace>();
  const [onboarding, setOnboarding] = useState<FiscalOnboardingConfiguration>();
  const [selected, setSelected] = useState<Record<string, string>>({});
  const [selectedOnline, setSelectedOnline] = useState("");
  const [expirationDays, setExpirationDays] = useState("3");
  const [remainingNumbers, setRemainingNumbers] = useState("100");
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [savingDeviceId, setSavingDeviceId] = useState<string>();
  const [savingOnline, setSavingOnline] = useState(false);
  const [savingAlerts, setSavingAlerts] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [nextWorkspace, nextOnboarding] = await Promise.all([
        fiscalConfigurationApi.getDevices(businessId),
        fiscalConfigurationApi.getOnboarding(businessId),
      ]);
      setWorkspace(nextWorkspace);
      setOnboarding(nextOnboarding);
      setExpirationDays(String(nextWorkspace.expirationWarningDays));
      setRemainingNumbers(String(nextWorkspace.remainingNumberWarningThreshold));
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible cargar las resoluciones fiscales.");
    } finally {
      setLoading(false);
    }
  }, [businessId]);

  useEffect(() => { void load(); }, [load]);

  async function synchronize() {
    setSyncing(true);
    try {
      setOnboarding(await fiscalConfigurationApi.synchronizeNumberingRanges(businessId));
      setWorkspace(await fiscalConfigurationApi.getDevices(businessId));
      toast.success("Resoluciones sincronizadas directamente desde la DIAN.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible consultar las resoluciones DIAN.");
    } finally {
      setSyncing(false);
    }
  }

  async function assignOnline() {
    if (!selectedOnline) return;
    setSavingOnline(true);
    try {
      setOnboarding(await fiscalConfigurationApi.activateProduction(businessId, selectedOnline));
      setWorkspace(await fiscalConfigurationApi.getDevices(businessId));
      setSelectedOnline("");
      toast.success("Resolución online asignada. La caja web ya puede emitir facturas electrónicas.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible asignar la resolución online.");
    } finally {
      setSavingOnline(false);
    }
  }

  async function assignDevice(deviceId: string) {
    const dianNumberingRangeId = selected[deviceId];
    if (!dianNumberingRangeId) return;
    setSavingDeviceId(deviceId);
    try {
      setWorkspace(await fiscalConfigurationApi.assignDeviceSeries(businessId, deviceId, dianNumberingRangeId));
      setSelected((current) => Object.fromEntries(Object.entries(current).map(
        ([key, value]) => [key, value === dianNumberingRangeId ? "" : value],
      )));
      toast.success("Resolución DIAN asignada. El equipo la descargará al sincronizar.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible asignar la resolución.");
    } finally {
      setSavingDeviceId(undefined);
    }
  }

  async function saveAlerts() {
    const days = Number(expirationDays);
    const numbers = Number(remainingNumbers);
    if (!Number.isInteger(days) || days < 0 || days > 365 ||
        !Number.isSafeInteger(numbers) || numbers < 0 || numbers > 1_000_000_000) {
      toast.error("Usa de 0 a 365 días y de 0 a 1.000.000.000 números restantes.");
      return;
    }
    setSavingAlerts(true);
    try {
      setWorkspace(await fiscalConfigurationApi.saveResolutionAlerts(businessId, days, numbers));
      toast.success("Alertas de resolución actualizadas para las cajas online y enroladas.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible guardar las alertas.");
    } finally {
      setSavingAlerts(false);
    }
  }

  const available = workspace?.availableResolutions ?? [];
  const canAssign = onboarding?.habilitationAccepted === true;

  return <Card className="overflow-hidden rounded-3xl">
    <CardHeader className="border-b bg-slate-950 text-white">
      <div className="flex flex-col justify-between gap-4 md:flex-row md:items-start">
        <div><CardTitle className="flex items-center gap-2"><Cpu className="h-5 w-5 text-teal-300" />Resoluciones por emisor</CardTitle><CardDescription className="mt-2 text-slate-300">La caja online y cada equipo enrolado reciben una resolución completa y exclusiva. Una resolución asignada desaparece de todos los combos.</CardDescription></div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" className="border-white/20 bg-white/5 text-white hover:bg-white/10" disabled={syncing || !canManage || !canAssign} onClick={() => void synchronize()}>{syncing ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}Consultar DIAN</Button>
          <Button variant="outline" size="sm" className="border-white/20 bg-white/5 text-white hover:bg-white/10" disabled={loading} onClick={() => void load()}>{loading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}Actualizar</Button>
        </div>
      </div>
    </CardHeader>
    <CardContent className="space-y-5 p-5">
      {!canAssign && !loading && <div className="flex gap-3 rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950"><AlertTriangle className="h-5 w-5 shrink-0" /><span>Completa primero la habilitación DIAN. Después podrás consultar y asignar resoluciones a cada emisor.</span></div>}

      <section className="grid gap-4 rounded-2xl border bg-slate-50 p-4 md:grid-cols-[1fr_1fr_auto] md:items-end">
        <div className="md:col-span-3"><strong className="block">Alertas en caja</strong><p className="text-sm text-muted-foreground">Se muestran al entrar cuando la resolución esté próxima a vencer o a agotar su numeración.</p></div>
        <label className="text-sm font-medium">Días antes del vencimiento<Input className="mt-2" type="number" min={0} max={365} value={expirationDays} onChange={(event) => setExpirationDays(event.target.value)} /></label>
        <label className="text-sm font-medium">Números restantes<Input className="mt-2" type="number" min={0} max={1_000_000_000} value={remainingNumbers} onChange={(event) => setRemainingNumbers(event.target.value)} /></label>
        <Button disabled={!canManage || savingAlerts} onClick={() => void saveAlerts()}>{savingAlerts ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Save className="mr-2 h-4 w-4" />}Guardar alertas</Button>
      </section>

      <section className="grid items-center gap-4 rounded-2xl border-2 border-teal-100 p-4 lg:grid-cols-[minmax(14rem,1fr)_minmax(20rem,1.4fr)_auto]">
        <div className="min-w-0"><strong className="flex items-center gap-2"><Globe2 className="h-5 w-5 text-teal-700" />Caja online</strong><small className="text-muted-foreground">Emisión desde el navegador conectado a Auraly</small></div>
        {workspace?.onlineAssignment ? <AssignedResolution authorizationNumber={workspace.onlineAssignment.authorizationNumber} prefix={workspace.onlineAssignment.prefix} rangeStart={workspace.onlineAssignment.rangeStart} rangeEnd={workspace.onlineAssignment.rangeEnd} detail={`${workspace.onlineAssignment.remainingConsecutives} números disponibles · vence ${workspace.onlineAssignment.validUntil}`} /> : <ResolutionSelect value={selectedOnline} onChange={setSelectedOnline} available={available} disabled={!canManage || !canAssign} />}
        <Button className="justify-self-end" disabled={!canManage || !canAssign || !!workspace?.onlineAssignment || !selectedOnline || savingOnline} onClick={() => void assignOnline()}>{savingOnline && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Asignar resolución</Button>
      </section>

      {loading && !workspace ? <div className="grid min-h-32 place-items-center"><Loader2 className="h-7 w-7 animate-spin text-teal-600" /></div> : null}
      {!loading && workspace?.devices.length === 0 ? <p className="rounded-2xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay equipos enrolados en esta sede.</p> : null}
      {workspace?.devices.map((device) => <div key={device.deviceId} className="grid items-center gap-4 rounded-2xl border p-4 lg:grid-cols-[minmax(14rem,1fr)_minmax(20rem,1.4fr)_auto]">
        <div className="min-w-0"><strong className="block truncate">{device.deviceName}</strong><small className="text-muted-foreground">{device.deviceIsActive ? "Equipo activo" : "Equipo inactivo"}{device.lastSeenAt ? ` · visto ${new Date(device.lastSeenAt).toLocaleString("es-CO")}` : ""}</small></div>
        {device.isProvisioned ? <AssignedResolution authorizationNumber={device.authorizationNumber ?? ""} prefix={device.prefix ?? ""} rangeStart={device.rangeStart ?? 0} rangeEnd={device.rangeEnd ?? 0} /> : <ResolutionSelect value={selected[device.deviceId] ?? ""} onChange={(value) => setSelected((current) => ({ ...current, [device.deviceId]: value }))} available={available} disabled={!canManage || !canAssign || !device.deviceIsActive} />}
        <Button className="justify-self-end" disabled={!canManage || !canAssign || device.isProvisioned || !selected[device.deviceId] || savingDeviceId === device.deviceId} onClick={() => void assignDevice(device.deviceId)}>{savingDeviceId === device.deviceId && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Asignar resolución</Button>
      </div>)}
      {!loading && available.length === 0 && <p className="text-sm text-muted-foreground">No hay resoluciones libres y vigentes. Consulta la DIAN después de asociar una nueva numeración al software.</p>}
    </CardContent>
  </Card>;
}

function ResolutionSelect({ value, onChange, available, disabled }: { value: string; onChange: (value: string) => void; available: FiscalDeviceSeriesWorkspace["availableResolutions"]; disabled: boolean }) {
  return <Select value={value || undefined} onValueChange={onChange} disabled={disabled || available.length === 0}><SelectTrigger className="h-12"><SelectValue placeholder={available.length ? "Selecciona una resolución DIAN" : "No hay resoluciones disponibles"} /></SelectTrigger><SelectContent>{available.map((resolution) => <SelectItem key={resolution.dianNumberingRangeId} value={resolution.dianNumberingRangeId}>{resolution.authorizationNumber} · {resolution.prefix}{resolution.rangeStart}–{resolution.rangeEnd} · vence {resolution.validUntil}</SelectItem>)}</SelectContent></Select>;
}

function AssignedResolution({ authorizationNumber, prefix, rangeStart, rangeEnd, detail }: { authorizationNumber: string; prefix: string; rangeStart: number; rangeEnd: number; detail?: string }) {
  return <div className="flex items-center gap-3 rounded-xl bg-emerald-50 px-4 py-3 text-emerald-950"><ShieldCheck className="h-5 w-5 shrink-0 text-emerald-600" /><span><strong className="block">{authorizationNumber}</strong><small>{prefix}{rangeStart}–{rangeEnd}{detail ? ` · ${detail}` : ""}</small></span></div>;
}
