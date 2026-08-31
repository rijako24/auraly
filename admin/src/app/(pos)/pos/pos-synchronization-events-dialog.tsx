"use client";

import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Activity, AlertTriangle, RefreshCw, Wifi, WifiOff } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { PosEdgeClient } from "@/services/pos/pos-edge-client";
import type { PosClient, PosSynchronizationEvent } from "@/services/pos/pos-edge-client";
import { nextSynchronizationEventFeed } from "./pos-synchronization-event-feed";

const visibleForMs = 10_000;
const fadeForMs = 900;
const maxVisibleEvents = 4;
const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });

type LiveEvent = PosSynchronizationEvent & { expiresAt: number };
type Props = {
  open: boolean; client: PosClient; connected: boolean;
  inProgress: boolean; onClose: () => void;
};

export function PosSynchronizationEventsDialog({ open, client, connected, inProgress, onClose }: Props) {
  const [liveEvents, setLiveEvents] = useState<LiveEvent[]>([]);
  const [now, setNow] = useState(() => Date.now());
  const seen = useRef(new Set<number>());
  const initialized = useRef(false);
  const wasOpen = useRef(false);
  const {
    data: synchronizationEvents,
    isError: synchronizationEventsFailed,
    refetch: refetchSynchronizationEvents,
  } = useQuery({
    queryKey: ["pos-synchronization-events", client.mode],
    queryFn: () => client.synchronizationEvents(80),
    enabled: open,
  });

  useEffect(() => {
    if (open && !wasOpen.current) {
      setLiveEvents([]);
      seen.current = new Set<number>();
      initialized.current = false;
    }
    wasOpen.current = open;
  }, [open]);

  useEffect(() => {
    if (!open || !(client instanceof PosEdgeClient)) return;
    const stopWatching = client.watchLocalState(
      () => void refetchSynchronizationEvents());
    return stopWatching;
  }, [client, open, refetchSynchronizationEvents]);

  useEffect(() => {
    if (!open) return;
    const timer = window.setInterval(() => {
      const value = Date.now();
      setNow(value);
      setLiveEvents(current => current.filter(event => event.expiresAt > value));
    }, 250);
    return () => window.clearInterval(timer);
  }, [open]);

  useEffect(() => {
    if (!synchronizationEvents) return;
    const receivedAt = Date.now();
    const next = nextSynchronizationEventFeed(
      synchronizationEvents,
      seen.current,
      initialized.current,
      maxVisibleEvents,
    );
    seen.current = next.seenSequences;
    initialized.current = true;
    const additions = next.events
      .map(event => ({ ...event, expiresAt: receivedAt + visibleForMs }));
    if (additions.length === 0) return;
    setLiveEvents(current => [...additions.reverse(), ...current]
      .sort((left, right) => right.sequence - left.sequence)
      .slice(0, maxVisibleEvents));
  }, [synchronizationEvents]);

  return (
    <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
      <DialogContent className="max-w-lg overflow-hidden rounded-[2rem] border-cyan-300/15 bg-[#06171b] p-0 text-white shadow-[0_32px_100px_rgba(2,20,24,.55)]">
        <DialogHeader className="relative overflow-hidden border-b border-cyan-200/10 px-7 pb-6 pt-7 text-left">
          <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_20%_0%,rgba(45,212,191,.18),transparent_42%),linear-gradient(135deg,rgba(255,255,255,.025),transparent_55%)]" />
          <div className="pointer-events-none absolute inset-x-0 top-0 h-px animate-pulse bg-gradient-to-r from-transparent via-cyan-200 to-transparent" />
          <div className="relative flex items-center gap-5">
            <ConnectionPulse connected={connected} inProgress={inProgress} />
            <div className="min-w-0 flex-1">
              <div className={`mb-2 flex items-center gap-2 text-[0.64rem] font-black uppercase tracking-[0.24em] ${connected ? "text-cyan-200/80" : "text-amber-200"}`}>
                <span className={`h-1.5 w-1.5 rounded-full ${connected ? "bg-cyan-300 shadow-[0_0_14px_rgba(103,232,249,.9)]" : "bg-amber-300 shadow-[0_0_14px_rgba(252,211,77,.7)]"}`} />
                {connected ? "Conectado · señal en tiempo real" : "Desconectado · trabajando local"}
                <Badge className="ml-auto border-white/10 bg-white/5 text-white/70" variant="outline">Ctrl+L</Badge>
              </div>
              <DialogTitle className="text-[1.35rem] font-black tracking-tight">Sincronización</DialogTitle>
              <DialogDescription className="mt-1 text-sm text-slate-300">Cambios recibidos y aplicados por esta caja.</DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <section className="relative h-[21rem] overflow-hidden px-5 py-5">
          <div className="pointer-events-none absolute inset-x-8 top-0 h-20 bg-cyan-300/[0.035] blur-3xl" />
          {synchronizationEventsFailed && (
            <div className="grid h-full place-items-center text-center"><div className="max-w-xs">
              <AlertTriangle className="mx-auto h-7 w-7 text-amber-300" />
              <p className="mt-3 text-sm font-bold text-slate-100">No pudimos leer la actividad local</p>
              <p className="mt-1 text-xs leading-5 text-slate-400">La caja puede continuar trabajando; el monitor volverá a intentarlo con la próxima señal.</p>
            </div></div>
          )}
          {!synchronizationEventsFailed && liveEvents.length === 0 && (
            <div className="grid h-full place-items-center text-center"><div className="relative">
              <div className="relative mx-auto grid h-16 w-16 place-items-center rounded-full border border-cyan-200/15 bg-cyan-200/[0.04]">
                <Activity className="h-6 w-6 text-cyan-200/70" />
              </div>
              <p className="mt-4 text-sm font-bold text-slate-200">Escuchando cambios</p>
              <p className="mt-1 text-xs text-slate-500">Aparecerán aquí y se desvanecerán automáticamente.</p>
            </div></div>
          )}
          {!synchronizationEventsFailed && liveEvents.length > 0 && (
            <div className="relative space-y-2.5">
              {liveEvents.map((event) => {
                const fading = event.expiresAt - now <= fadeForMs;
                return (
                  <article key={event.sequence} className={`relative overflow-hidden rounded-2xl border bg-white/[0.055] px-4 py-3.5 backdrop-blur-xl ${event.level === "Error" ? "border-red-300/25" : "border-cyan-200/15"} ${fading ? "animate-out fade-out slide-out-to-right-4 duration-700 fill-mode-forwards" : "animate-in fade-in slide-in-from-top-3 duration-500"}`}>
                    <div className="absolute inset-y-0 left-0 w-px bg-gradient-to-b from-transparent via-cyan-300 to-transparent" />
                    <div className="flex items-center gap-2">
                      <span className="rounded-full bg-cyan-300/10 px-2 py-0.5 text-[0.62rem] font-black uppercase tracking-[0.16em] text-cyan-200">{event.category}</span>
                      <time className="ml-auto text-[0.65rem] font-semibold text-slate-500">{new Date(event.occurredAt).toLocaleTimeString("es-CO")}</time>
                    </div>
                    <p className="mt-2 text-sm font-bold leading-5 text-white">{event.title}</p>
                    {event.detail && <p className="mt-0.5 truncate text-xs text-slate-400">{event.detail}</p>}
                    {event.newPrice != null && (
                      <div className="mt-2 flex items-center gap-2 text-sm font-black text-cyan-100">
                        {event.previousPrice != null && <span className="text-slate-500 line-through">{money.format(event.previousPrice)}</span>}
                        {event.previousPrice != null && <span className="text-cyan-400">→</span>}
                        <span>{money.format(event.newPrice)}</span>
                      </div>
                    )}
                  </article>
                );
              })}
            </div>
          )}
        </section>
      </DialogContent>
    </Dialog>
  );
}

function ConnectionPulse({ connected, inProgress }: { connected: boolean; inProgress: boolean }) {
  const Icon = connected ? Wifi : WifiOff;
  return (
    <div className="relative grid h-20 w-20 shrink-0 place-items-center">
      <span className={`absolute inset-0 rounded-full border ${connected ? "animate-[ping_2.4s_ease-out_infinite] border-cyan-300/20" : "border-amber-300/20"}`} />
      <span className="absolute inset-2 rounded-full border border-cyan-100/10 bg-cyan-200/[0.035] shadow-[inset_0_0_28px_rgba(103,232,249,.08)]" />
      <span className="absolute inset-4 rounded-full border border-white/5 bg-[#0b282d] shadow-[0_0_24px_rgba(45,212,191,.14)]" />
      {inProgress && <RefreshCw className="absolute h-12 w-12 animate-spin text-cyan-300/15" />}
      <Icon className={`relative h-6 w-6 ${connected ? "text-cyan-200" : "text-amber-200"}`} />
    </div>
  );
}
