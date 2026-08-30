"use client";

import { useEffect, useState } from "react";
import { Cpu, Loader2, RefreshCw, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  fiscalConfigurationApi,
  type FiscalDeviceSeriesWorkspace,
} from "@/services/api/fiscal-configuration";

export function FiscalDeviceResolutionCard({
  businessId,
  canManage,
}: {
  businessId: string;
  canManage: boolean;
}) {
  const [workspace, setWorkspace] = useState<FiscalDeviceSeriesWorkspace>();
  const [selected, setSelected] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [savingDeviceId, setSavingDeviceId] = useState<string>();

  const load = async () => {
    setLoading(true);
    try {
      setWorkspace(await fiscalConfigurationApi.getDevices(businessId));
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible cargar los equipos enrolados.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, [businessId]);

  const assign = async (deviceId: string) => {
    const dianNumberingRangeId = selected[deviceId];
    if (!dianNumberingRangeId) return;
    setSavingDeviceId(deviceId);
    try {
      const updated = await fiscalConfigurationApi.assignDeviceSeries(
        businessId, deviceId, dianNumberingRangeId);
      setWorkspace(updated);
      setSelected((current) => ({ ...current, [deviceId]: "" }));
      toast.success("Resolución DIAN asignada. El equipo la descargará al sincronizar.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible asignar la resolución.");
    } finally {
      setSavingDeviceId(undefined);
    }
  };

  return <Card className="overflow-hidden rounded-3xl">
    <CardHeader className="border-b bg-slate-950 text-white">
      <div className="flex items-start justify-between gap-4">
        <div><CardTitle className="flex items-center gap-2"><Cpu className="h-5 w-5 text-teal-300" />Resoluciones por equipo</CardTitle><CardDescription className="mt-2 text-slate-300">Cada dispositivo enrolado recibe una resolución completa y exclusiva. No se dividen ni se comparten rangos.</CardDescription></div>
        <Button variant="outline" size="sm" className="border-white/20 bg-white/5 text-white hover:bg-white/10" disabled={loading} onClick={() => void load()}>{loading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <RefreshCw className="mr-2 h-4 w-4" />}Actualizar</Button>
      </div>
    </CardHeader>
    <CardContent className="space-y-3 p-5">
      {loading && !workspace ? <div className="grid min-h-32 place-items-center"><Loader2 className="h-7 w-7 animate-spin text-teal-600" /></div> : null}
      {!loading && workspace?.devices.length === 0 ? <p className="rounded-2xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay equipos enrolados en esta sede.</p> : null}
      {workspace?.devices.map((device) => <div key={device.deviceId} className="grid items-center gap-4 rounded-2xl border p-4 lg:grid-cols-[minmax(14rem,1fr)_minmax(20rem,1.4fr)_auto]">
        <div className="min-w-0"><strong className="block truncate">{device.deviceName}</strong><small className="text-muted-foreground">{device.deviceIsActive ? "Equipo activo" : "Equipo inactivo"}{device.lastSeenAt ? ` · visto ${new Date(device.lastSeenAt).toLocaleString("es-CO")}` : ""}</small></div>
        {device.isProvisioned ? <div className="flex items-center gap-3 rounded-xl bg-emerald-50 px-4 py-3 text-emerald-950"><ShieldCheck className="h-5 w-5 shrink-0 text-emerald-600" /><span><strong className="block">{device.authorizationNumber}</strong><small>{device.prefix}{device.rangeStart}–{device.rangeEnd}</small></span></div> : <Select value={selected[device.deviceId] || undefined} onValueChange={(value) => setSelected((current) => ({ ...current, [device.deviceId]: value }))} disabled={!canManage || !device.deviceIsActive || !workspace.availableResolutions.length}><SelectTrigger className="h-12"><SelectValue placeholder={workspace.availableResolutions.length ? "Selecciona una resolución DIAN" : "No hay resoluciones disponibles"} /></SelectTrigger><SelectContent>{workspace.availableResolutions.map((resolution) => <SelectItem key={resolution.dianNumberingRangeId} value={resolution.dianNumberingRangeId}>{resolution.authorizationNumber} · {resolution.prefix}{resolution.rangeStart}–{resolution.rangeEnd} · vence {resolution.validUntil}</SelectItem>)}</SelectContent></Select>}
        <Button className="justify-self-end" disabled={!canManage || device.isProvisioned || !selected[device.deviceId] || savingDeviceId === device.deviceId} onClick={() => void assign(device.deviceId)}>{savingDeviceId === device.deviceId && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Asignar resolución</Button>
      </div>)}
    </CardContent>
  </Card>;
}
