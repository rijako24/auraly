"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, FileKey2, Loader2, MonitorSmartphone, Pencil } from "lucide-react";
import { toast } from "sonner";
import { FiscalIssuerConnectionCard } from "@/components/fiscal/fiscal-issuer-connection-card";
import { FiscalResolutionForm } from "@/components/fiscal/fiscal-resolution-form";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { fiscalConfigurationApi, type FiscalDeviceSeriesAssignment, type FiscalDeviceSeriesWorkspace, type FiscalResolutionConfiguration, type SaveFiscalResolutionConfiguration } from "@/services/api/fiscal-configuration";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function FiscalSettingsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const businessName = useBusinessContextStore((state) => state.businesses.find((item) => item.businessId === state.selectedBusinessId)?.name ?? "Sede actual");
  const canManage = useAuthStore((state) => state.user?.permissions.includes("fiscal.configuration.manage") ?? false);
  const [resolution, setResolution] = useState<FiscalResolutionConfiguration | null>(null);
  const [devices, setDevices] = useState<FiscalDeviceSeriesWorkspace | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [editing, setEditing] = useState(false);

  const load = useCallback(async () => {
    if (!businessId) return;
    setLoading(true);
    try {
      const [fiscal, enrolled] = await Promise.all([fiscalConfigurationApi.get(businessId), fiscalConfigurationApi.getDevices(businessId)]);
      setResolution(fiscal); setDevices(enrolled);
    } catch (error) { toast.error(message(error, "No fue posible cargar la configuración fiscal.")); }
    finally { setLoading(false); }
  }, [businessId]);
  useEffect(() => { void load(); }, [load]);

  async function save(request: SaveFiscalResolutionConfiguration) {
    if (!businessId) return;
    setSaving(true);
    try { setResolution(await fiscalConfigurationApi.save(businessId, request)); setEditing(false); await load(); toast.success("Resolución fiscal guardada."); }
    catch (error) { toast.error(message(error, "No fue posible guardar la resolución.")); }
    finally { setSaving(false); }
  }

  if (!businessId) return <Card><CardContent className="p-8 text-center text-muted-foreground">Selecciona una sede en la barra superior.</CardContent></Card>;
  const health = fiscalHealth(resolution);
  return <div className="mx-auto max-w-7xl space-y-6">
    <header className="rounded-3xl bg-gradient-to-r from-slate-950 via-teal-950 to-slate-950 p-7 text-white"><p className="text-xs font-bold uppercase tracking-[.18em] text-teal-300">Control fiscal central</p><h1 className="mt-2 text-3xl font-black">Facturación electrónica · {businessName}</h1><p className="mt-2 max-w-3xl text-sm text-slate-300">Configura una resolución por sede y reserva bloques independientes para cada equipo enrolado. Las cajas reciben su asignación al sincronizar.</p></header>
    {loading ? <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin" />Cargando configuración…</p> : <>
      <div className="grid gap-5 lg:grid-cols-2"><FiscalIssuerConnectionCard businessId={businessId} /><Card><CardHeader><CardTitle className="flex items-center gap-2"><FileKey2 className="h-5 w-5 text-primary" />Resolución DIAN</CardTitle><CardDescription>{health.message}</CardDescription></CardHeader><CardContent className="space-y-3 text-sm"><State ok={health.ok} label={health.label} /><Detail label="Resolución" value={resolution?.authorizationNumber} /><Detail label="Rango autorizado" value={resolution?.rangeStart ? `${resolution.rangeStart.toLocaleString("es-CO")} – ${resolution.rangeEnd?.toLocaleString("es-CO")}` : null} /><Detail label="Vigencia" value={resolution?.validFrom ? `${resolution.validFrom} a ${resolution.validUntil}` : null} /><Button className="w-full" disabled={!canManage} onClick={() => setEditing(true)}><Pencil className="mr-2 h-4 w-4" />{resolution?.hasActiveAuthorization ? "Editar resolución" : "Crear resolución"}</Button></CardContent></Card></div>
      <Card><CardHeader><CardTitle className="flex items-center gap-2"><MonitorSmartphone className="h-5 w-5 text-primary" />Numeración por equipo enrolado</CardTitle><CardDescription>{devices?.availableConsecutives.toLocaleString("es-CO") ?? 0} consecutivos disponibles. Cada rango es exclusivo y no se comparte entre cajas.</CardDescription></CardHeader><CardContent className="space-y-3">{!devices?.devices.length && <p className="rounded-xl border border-dashed p-6 text-center text-sm text-muted-foreground">Enrola primero los equipos de esta sede. Después aparecerán aquí para asignarles numeración.</p>}{devices?.devices.map((device) => <DeviceRow key={device.deviceId} device={device} canManage={canManage} available={devices.availableConsecutives} onAssigned={setDevices} businessId={businessId} />)}</CardContent></Card>
    </>}
    <Dialog open={editing} onOpenChange={setEditing}><DialogContent className="max-h-[92vh] max-w-3xl overflow-auto"><DialogHeader><DialogTitle>{resolution?.hasActiveAuthorization ? "Editar" : "Crear"} resolución · {businessName}</DialogTitle></DialogHeader><FiscalResolutionForm value={resolution} saving={saving} onSave={save} /></DialogContent></Dialog>
  </div>;
}

function DeviceRow({ device, canManage, available, businessId, onAssigned }: { device: FiscalDeviceSeriesAssignment; canManage: boolean; available: number; businessId: string; onAssigned: (value: FiscalDeviceSeriesWorkspace) => void }) {
  const [count, setCount] = useState(Math.min(1000, available)); const [busy, setBusy] = useState(false);
  async function assign() { setBusy(true); try { onAssigned(await fiscalConfigurationApi.assignDeviceSeries(businessId, device.deviceId, count)); toast.success(`Numeración asignada a ${device.deviceName}. La caja la recibirá al sincronizar.`); } catch (error) { toast.error(message(error, "No fue posible asignar la numeración.")); } finally { setBusy(false); } }
  return <div className="grid gap-3 rounded-2xl border p-4 md:grid-cols-[1fr_auto] md:items-center"><div><p className="font-bold">{device.deviceName}</p><p className="text-sm text-muted-foreground">{device.isProvisioned ? `${device.prefix}${device.rangeStart?.toLocaleString("es-CO")} – ${device.prefix}${device.rangeEnd?.toLocaleString("es-CO")}` : "Sin numeración fiscal asignada"}</p>{device.lastSeenAt && <p className="mt-1 text-xs text-muted-foreground">Última conexión: {new Date(device.lastSeenAt).toLocaleString("es-CO")}</p>}</div>{!device.isProvisioned && <div className="flex gap-2"><Input className="w-36" type="number" min={1} max={available} value={count} onChange={(event) => setCount(event.currentTarget.valueAsNumber || 0)} aria-label="Cantidad de consecutivos" /><Button disabled={!canManage || busy || count < 1 || count > available} onClick={() => void assign()}>{busy && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}Asignar</Button></div>}</div>;
}

function fiscalHealth(value: FiscalResolutionConfiguration | null) { const today = new Date().toISOString().slice(0, 10); if (!value?.hasActiveAuthorization) return { ok: false, label: "Sin resolución", message: "Crea una resolución para habilitar factura electrónica." }; if (value.validFrom && today < value.validFrom) return { ok: false, label: "Aún no vigente", message: `La vigencia inicia el ${value.validFrom}.` }; if (value.validUntil && today > value.validUntil) return { ok: false, label: "Resolución vencida", message: "Debes registrar una nueva resolución antes de emitir facturas." }; if (value.rangeEnd != null && value.nextConsecutive != null && value.nextConsecutive > value.rangeEnd) return { ok: false, label: "Numeración agotada", message: "Debes registrar una nueva resolución antes de emitir facturas." }; return { ok: true, label: "Vigente", message: "La vigencia y la numeración del servidor están disponibles." }; }
function State({ ok, label }: { ok: boolean; label: string }) { return <div className={`flex items-center gap-2 rounded-xl p-3 font-semibold ${ok ? "bg-emerald-50 text-emerald-800" : "bg-amber-50 text-amber-800"}`}>{ok ? <CheckCircle2 className="h-5 w-5" /> : <AlertTriangle className="h-5 w-5" />}{label}</div>; }
function Detail({ label, value }: { label: string; value?: string | null }) { return <div className="flex justify-between gap-3 border-b pb-2"><span className="text-muted-foreground">{label}</span><b className="text-right">{value || "Sin configurar"}</b></div>; }
function message(error: unknown, fallback: string) { return error instanceof Error ? error.message : fallback; }
