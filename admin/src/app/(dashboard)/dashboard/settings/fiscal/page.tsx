"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, MonitorSmartphone } from "lucide-react";
import { toast } from "sonner";
import { FiscalOnboardingCard } from "@/components/fiscal/fiscal-onboarding-card";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { fiscalConfigurationApi, type FiscalDeviceSeriesAssignment, type FiscalDeviceSeriesWorkspace } from "@/services/api/fiscal-configuration";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function FiscalSettingsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const businessName = useBusinessContextStore((state) => state.businesses.find((item) => item.businessId === state.selectedBusinessId)?.name ?? "Sede actual");
  const canManage = useAuthStore((state) => state.user?.permissions.includes("fiscal.configuration.manage") ?? false);
  const [devices, setDevices] = useState<FiscalDeviceSeriesWorkspace | null>(null);
  const [loadingDevices, setLoadingDevices] = useState(false);

  const loadDevices = useCallback(async () => {
    if (!businessId) return;
    setLoadingDevices(true);
    try { setDevices(await fiscalConfigurationApi.getDevices(businessId)); }
    catch (error) { toast.error(message(error, "No fue posible cargar los equipos fiscales.")); }
    finally { setLoadingDevices(false); }
  }, [businessId]);
  useEffect(() => { void loadDevices(); }, [loadDevices]);

  if (!businessId) return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede en la barra superior.</CardContent></Card>;
  return <div className="mx-auto max-w-7xl space-y-6">
    <header className="rounded-3xl bg-gradient-to-r from-slate-950 via-teal-950 to-slate-950 p-7 text-white"><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Control fiscal central</p><h1 className="mt-2 text-3xl font-black">Facturación electrónica · {businessName}</h1><p className="mt-2 max-w-3xl text-sm text-slate-300">Carga el certificado, completa la habilitación y activa para esta sede una resolución obtenida directamente de la DIAN.</p></header>
    <FiscalOnboardingCard businessId={businessId} canManage={canManage} />
    <Card><CardHeader><CardTitle className="flex items-center gap-2"><MonitorSmartphone className="h-5 w-5 text-primary" />Numeración por equipo enrolado</CardTitle><CardDescription>{devices?.availableConsecutives.toLocaleString("es-CO") ?? 0} consecutivos disponibles. Cada rango es exclusivo y no se comparte entre cajas.</CardDescription></CardHeader><CardContent className="space-y-3">{loadingDevices && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Cargando equipos…</p>}{!loadingDevices && !devices?.devices.length && <p className="rounded-xl border border-dashed p-6 text-center text-sm text-muted-foreground">Cuando producción esté activa, enrola los equipos de esta sede para asignarles bloques de numeración.</p>}{devices?.devices.map((device) => <DeviceRow key={device.deviceId} device={device} canManage={canManage} available={devices.availableConsecutives} onAssigned={setDevices} businessId={businessId} />)}</CardContent></Card>
  </div>;
}

function DeviceRow({ device, canManage, available, businessId, onAssigned }: { device: FiscalDeviceSeriesAssignment; canManage: boolean; available: number; businessId: string; onAssigned: (value: FiscalDeviceSeriesWorkspace) => void }) {
  const [count, setCount] = useState(Math.min(1000, available)); const [busy, setBusy] = useState(false);
  async function assign() { setBusy(true); try { onAssigned(await fiscalConfigurationApi.assignDeviceSeries(businessId, device.deviceId, count)); toast.success(`Numeración asignada a ${device.deviceName}. La caja la recibirá al sincronizar.`); } catch (error) { toast.error(message(error, "No fue posible asignar la numeración.")); } finally { setBusy(false); } }
  return <div className="grid gap-3 rounded-2xl border p-4 md:grid-cols-[1fr_auto] md:items-center"><div><p className="font-bold">{device.deviceName}</p><p className="text-sm text-muted-foreground">{device.isProvisioned ? `${device.prefix}${device.rangeStart?.toLocaleString("es-CO")} – ${device.prefix}${device.rangeEnd?.toLocaleString("es-CO")}` : "Sin numeración fiscal asignada"}</p>{device.lastSeenAt && <p className="mt-1 text-xs text-muted-foreground">Última conexión: {new Date(device.lastSeenAt).toLocaleString("es-CO")}</p>}</div>{!device.isProvisioned && <div className="flex gap-2"><Input className="w-36" type="number" min={1} max={available} value={count} onChange={(event) => setCount(event.currentTarget.valueAsNumber || 0)} aria-label="Cantidad de consecutivos" /><Button disabled={!canManage || busy || count < 1 || count > available} onClick={() => void assign()}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Asignar</Button></div>}</div>;
}

function message(error: unknown, fallback: string) { return error instanceof Error ? error.message : fallback; }
