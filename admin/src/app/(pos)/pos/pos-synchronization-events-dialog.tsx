"use client";

import { useQuery } from "@tanstack/react-query";
import { Activity, AlertTriangle, CheckCircle2, RefreshCw, Server, Wifi, WifiOff } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import type { PosClient } from "@/services/pos/pos-edge-client";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

export function PosSynchronizationEventsDialog({ open, client, connected, pendingCount, inProgress, lastAt, onClose }: {
  open: boolean;
  client: PosClient;
  connected: boolean;
  pendingCount: number;
  inProgress: boolean;
  lastAt: string | null;
  onClose: () => void;
}) {
  const query = useQuery({
    queryKey: ["pos-synchronization-events", open],
    queryFn: () => client.synchronizationEvents(150),
    enabled: open,
    refetchInterval: open ? 1000 : false,
  });
  return <Dialog open={open} onOpenChange={(value) => !value && onClose()}><DialogContent className="max-h-[92dvh] max-w-4xl overflow-hidden p-0">
    <DialogHeader className="border-b bg-slate-950 px-6 py-5 text-left text-white"><DialogTitle className="flex items-center gap-2"><Activity className="h-5 w-5 text-teal-300"/> Monitor de sincronización <Badge variant="secondary">Ctrl+L</Badge></DialogTitle><DialogDescription className="text-slate-300">Eventos reales recibidos y procesados por esta caja.</DialogDescription></DialogHeader>
    <section className="grid gap-3 border-b bg-slate-50 p-4 sm:grid-cols-4"><Status icon={connected ? Wifi : WifiOff} label="Servidor" value={connected ? "Conectado" : "Sin conexión"} ok={connected}/><Status icon={inProgress ? RefreshCw : CheckCircle2} label="Sincronizador" value={inProgress ? "Procesando" : "En espera"} ok={!inProgress}/><Status icon={Server} label="Pendientes" value={String(pendingCount)} ok={pendingCount === 0}/><Status icon={Activity} label="Último éxito" value={lastAt ? new Date(lastAt).toLocaleTimeString("es-CO") : "Sin registro"} ok={Boolean(lastAt)}/></section>
    <div className="max-h-[60dvh] overflow-y-auto p-4">{query.isError && <p className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-800"><AlertTriangle className="mr-2 inline h-4 w-4"/>No fue posible leer los eventos. Verifica el permiso del usuario.</p>}{(query.data ?? []).map((event) => <article key={event.sequence} className="grid gap-2 border-b py-3 sm:grid-cols-[7rem_8rem_minmax(0,1fr)]"><time className="text-xs text-slate-500">{new Date(event.occurredAt).toLocaleTimeString("es-CO")}</time><Badge className="w-fit" variant={event.level === "Error" ? "destructive" : "outline"}>{event.category}</Badge><div><p className="font-medium text-slate-900">{event.title}</p>{event.detail && <p className="text-sm text-slate-500">{event.detail}</p>}{event.previousPrice != null && event.newPrice != null && <p className="mt-1 text-sm font-semibold text-teal-800">{money.format(event.previousPrice)} → {money.format(event.newPrice)}</p>}</div></article>)}{!query.isLoading && !query.isError && !query.data?.length && <p className="p-10 text-center text-sm text-slate-500">Aún no hay eventos en esta ejecución de la caja.</p>}</div>
  </DialogContent></Dialog>;
}

function Status({ icon: Icon, label, value, ok }: { icon: typeof Wifi; label: string; value: string; ok: boolean }) { return <div className="rounded-xl border bg-white p-3"><p className="flex items-center gap-2 text-xs text-slate-500"><Icon className={`h-4 w-4 ${ok ? "text-teal-600" : "text-amber-600"}`}/>{label}</p><strong className="mt-1 block text-sm">{value}</strong></div>; }
