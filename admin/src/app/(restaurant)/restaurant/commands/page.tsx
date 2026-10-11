"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Ban, BellRing, CheckCheck, ChefHat, Clock3, Flame, LayoutGrid, RefreshCw, Truck, Volume2, Wine } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SearchableSelect } from "@/components/forms/searchable-select";
import { apiClient } from "@/services/api/client";
import { restaurantAccountApi, type RestaurantCancellation,
  type RestaurantCommandBoardPage, type RestaurantCommandCard } from "@/services/api/restaurant-account";
import { restaurantMasterApi } from "@/services/api/restaurant-master";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { useAuthStore } from "@/stores/auth-store";
import { realtimeReconnectDelay, wasRealtimeConnectionStable } from "@/lib/realtime-reconnect-policy";
import { hasNewRestaurantCommandArrival, isRestaurantCommandNotice } from "@/lib/restaurant-command-notice";

type ActiveCommandState = Exclude<RestaurantCommandCard["state"], "Delivered">;

const states = [
  { value: "Pending", label: "Nuevas", icon: Clock3,
    active: "border-amber-400 bg-amber-100 text-amber-950 shadow-amber-500/10 dark:border-amber-600 dark:bg-amber-950/70 dark:text-amber-100" },
  { value: "Preparing", label: "En preparación", icon: Flame,
    active: "border-sky-400 bg-sky-100 text-sky-950 shadow-sky-500/10 dark:border-sky-600 dark:bg-sky-950/70 dark:text-sky-100" },
  { value: "Ready", label: "Listas", icon: CheckCheck,
    active: "border-emerald-400 bg-emerald-100 text-emerald-950 shadow-emerald-500/10 dark:border-emerald-600 dark:bg-emerald-950/70 dark:text-emerald-100" },
  { value: "ToDeliver", label: "Por entregar", icon: Truck,
    active: "border-violet-400 bg-violet-100 text-violet-950 shadow-violet-500/10 dark:border-violet-600 dark:bg-violet-950/70 dark:text-violet-100" },
  { value: "Cancelled", label: "Anuladas", icon: Ban,
    active: "border-red-400 bg-red-100 text-red-950 shadow-red-500/10 dark:border-red-600 dark:bg-red-950/70 dark:text-red-100" },
];

const appearance: Record<ActiveCommandState, { label: string; card: string; header: string; badge: string }> = {
  Pending: { label: "Nueva", card: "border-amber-300 border-l-[6px] border-l-amber-500 dark:border-amber-800 dark:border-l-amber-500", header: "bg-amber-50 dark:bg-amber-950/40", badge: "bg-amber-200/70 text-amber-950 dark:bg-amber-800/50 dark:text-amber-100" },
  Preparing: { label: "En preparación", card: "border-sky-300 border-l-[6px] border-l-sky-500 dark:border-sky-800 dark:border-l-sky-500", header: "bg-sky-50 dark:bg-sky-950/40", badge: "bg-sky-200/70 text-sky-950 dark:bg-sky-800/50 dark:text-sky-100" },
  Ready: { label: "Lista", card: "border-emerald-300 border-l-[6px] border-l-emerald-500 dark:border-emerald-800 dark:border-l-emerald-500", header: "bg-emerald-50 dark:bg-emerald-950/40", badge: "bg-emerald-200/70 text-emerald-950 dark:bg-emerald-800/50 dark:text-emerald-100" },
  ToDeliver: { label: "Por entregar", card: "border-violet-300 border-l-[6px] border-l-violet-500 dark:border-violet-800 dark:border-l-violet-500", header: "bg-violet-50 dark:bg-violet-950/40", badge: "bg-violet-200/70 text-violet-950 dark:bg-violet-800/50 dark:text-violet-100" },
  Cancelled: { label: "Anulada", card: "border-red-300 border-l-[6px] border-l-red-500 dark:border-red-800 dark:border-l-red-500", header: "bg-red-50 dark:bg-red-950/40", badge: "bg-red-200/70 text-red-950 dark:bg-red-800/50 dark:text-red-100" },
};

function playArrival(context: AudioContext) {
  try {
    if (context.state !== "running") return;
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.type = "sine";
    oscillator.frequency.setValueAtTime(680, context.currentTime);
    oscillator.frequency.setValueAtTime(880, context.currentTime + 0.12);
    gain.gain.setValueAtTime(0.0001, context.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.13, context.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.3);
    oscillator.connect(gain).connect(context.destination);
    oscillator.start();
    oscillator.stop(context.currentTime + 0.31);
  } catch { /* Audio is optional; a failed chime must not interrupt command updates. */ }
}

export default function RestaurantCommandsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const canManage = useAuthStore((state) => state.user?.permissions.includes("restaurant.commands.manage") ?? false);
  const canApprove = useAuthStore((state) => state.user?.permissions.includes("restaurant.cancellations.approve") ?? false);
  const [stationId, setStationId] = useState<string | undefined>();
  const [state, setState] = useState<ActiveCommandState | undefined>();
  const [tableCode, setTableCode] = useState<string | undefined>();
  const [returnHref, setReturnHref] = useState("/dashboard/restaurant");
  const [page, setPage] = useState(1);
  const [soundEnabled, setSoundEnabled] = useState(false);
  const [pushConnected, setPushConnected] = useState(false);
  const [pushConnecting, setPushConnecting] = useState(false);
  const [pushIssue, setPushIssue] = useState("");
  const [pushRevision, setPushRevision] = useState(0);
  const [pendingCardIds, setPendingCardIds] = useState<Set<string>>(() => new Set());
  const [partialRequest, setPartialRequest] = useState<{
    cancellationId: string; rowVersion: string; productName: string;
  } | null>(null);
  const [partialQuantities, setPartialQuantities] = useState<Record<string, number>>({});
  const knownCards = useRef<Set<string> | null>(null);
  const latestArrival = useRef<number | null>(null);
  const audioContext = useRef<AudioContext | null>(null);
  const client = useQueryClient();
  const key = ["restaurant", businessId, "command-cards", stationId, state, tableCode, page];
  const stations = useQuery({ queryKey: ["restaurant", businessId, "stations", "command-board"],
    queryFn: () => restaurantMasterApi.stations(), enabled: !!businessId });
  const tables = useQuery({ queryKey: ["restaurant", businessId, "tables", "command-board"],
    queryFn: () => restaurantMasterApi.tables(1), enabled: !!businessId });
  const cards = useQuery({ queryKey: key, queryFn: () => restaurantAccountApi.commandCards({ stationId, state, tableCode, page }),
    enabled: !!businessId, refetchOnWindowFocus: false });
  const partialComponents = useQuery({
    queryKey: ["restaurant", businessId, "cancellation-components", partialRequest?.cancellationId],
    queryFn: () => restaurantAccountApi.cancellationComponents(partialRequest!.cancellationId),
    enabled: !!partialRequest,
  });
  const changeState = useMutation({
    mutationFn: ({ cardId, state, rowVersion }: { cardId: string; state: string; rowVersion: string }) =>
      restaurantAccountApi.changeCommandState(cardId, state, rowVersion),
    onMutate: ({ cardId }) => setPendingCardIds((current) => new Set(current).add(cardId)),
    onSuccess: () => void client.invalidateQueries({ queryKey: ["restaurant", businessId, "command-cards"] }),
    onError: () => { toast.error("La comanda cambió. Revisa su estado actualizado."); void cards.refetch(); },
    onSettled: (_data, _error, { cardId }) => setPendingCardIds((current) => {
      const next = new Set(current);
      next.delete(cardId);
      return next;
    }),
  });
  const reprint = useMutation({
    mutationFn: (cardId: string) => restaurantAccountApi.reprintCommand(cardId),
    onSuccess: () => { toast.success("Se solicitó una copia de la comanda.");
      void client.invalidateQueries({ queryKey: ["restaurant", businessId, "command-cards"] }); },
    onError: () => { toast.error("No se pudo solicitar la copia. Actualiza la comanda."); void cards.refetch(); },
  });
  const patchCancellation = (updated: RestaurantCancellation) => {
    client.setQueriesData<RestaurantCommandBoardPage>(
      { queryKey: ["restaurant", businessId, "command-cards"] }, (current) =>
        current ? { ...current, items: current.items.map((card) =>
          card.cardId !== updated.cardId ? card : { ...card,
            lines: card.lines.map((line) => line.additionId !== updated.additionId
              ? line : { ...line,
                cancellationId: updated.status === "Approved" || updated.status === "Rejected"
                  ? null : updated.cancellationId,
                cancellationQuantity: updated.status === "Approved" || updated.status === "Rejected"
                  ? null : updated.quantity,
                cancellationStatus: updated.status === "Approved" || updated.status === "Rejected"
                  ? null : updated.status,
                cancellationPreparationState: updated.preparationState,
                cancellationRowVersion: updated.rowVersion,
              }),
          }),
        } : current);
  };
  const confirmCancellation = useMutation({
    mutationFn: ({ cancellationId, preparationState, rowVersion, consumedComponents }: {
      cancellationId: string; preparationState: "NotPrepared" | "Prepared" | "Partial";
      rowVersion: string; consumedComponents?: { productId: string; quantity: number }[];
    }) => restaurantAccountApi.confirmCancellation(cancellationId, preparationState,
      rowVersion, consumedComponents),
    onSuccess: (updated) => { patchCancellation(updated); setPartialRequest(null);
      void client.invalidateQueries({ queryKey: ["restaurant", businessId, "command-cards"] }); },
    onError: () => { toast.error("La solicitud cambió. Actualiza las comandas."); void cards.refetch(); },
  });
  const resolveCancellation = useMutation({
    mutationFn: ({ cancellationId, approve, rowVersion }: {
      cancellationId: string; approve: boolean; rowVersion: string;
    }) => restaurantAccountApi.resolveCancellation(cancellationId, approve, rowVersion),
    onSuccess: (updated) => { patchCancellation(updated);
      void client.invalidateQueries({ queryKey: ["restaurant", businessId, "command-cards"] });
      toast.success(updated.status === "Approved" ? "Producto retirado de la cuenta." : "Solicitud rechazada."); },
    onError: (error) => { toast.error(error instanceof Error ? error.message :
      "No se pudo resolver la cancelación. Actualiza la comanda."); void cards.refetch(); },
  });

  useEffect(() => {
    if (new URLSearchParams(window.location.search).get("from") === "pos")
      setReturnHref("/pos?workspace=change");
  }, []);

  useEffect(() => {
    if (!cards.data) return;
    const next = new Set(cards.data.items.map((card) => card.cardId));
    const newest = cards.data.items.reduce((value, card) =>
      Math.max(value, Date.parse(card.receivedAt) || 0), 0);
    const newArrival = hasNewRestaurantCommandArrival(
      cards.data.items, knownCards.current, latestArrival.current, page);
    if (newArrival && soundEnabled && audioContext.current)
      playArrival(audioContext.current);
    knownCards.current = next;
    latestArrival.current = Math.max(latestArrival.current ?? 0, newest);
  }, [cards.data, page, soundEnabled]);

  useEffect(() => () => { void audioContext.current?.close(); }, []);

  const toggleSound = async () => {
    if (soundEnabled) {
      setSoundEnabled(false);
      return;
    }
    try {
      const context = audioContext.current ?? new AudioContext();
      audioContext.current = context;
      await context.resume();
      if (context.state !== "running") throw new Error("Audio unavailable");
      setSoundEnabled(true);
      playArrival(context);
    } catch {
      toast.error("El navegador bloqueó el sonido. Toca la pantalla y vuelve a activarlo.");
    }
  };

  const selectStation = (nextStationId?: string) => {
    knownCards.current = null;
    latestArrival.current = null;
    setPage(1);
    setStationId(nextStationId);
  };

  const selectState = (nextState?: ActiveCommandState) => {
    knownCards.current = null;
    latestArrival.current = null;
    setPage(1);
    setState(nextState);
  };

  const selectTable = (nextCode?: string) => {
    const normalized = nextCode?.trim() || undefined;
    knownCards.current = null;
    latestArrival.current = null;
    setPage(1);
    setTableCode(normalized);
  };

  useEffect(() => {
    if (!businessId) return;
    let closed = false;
    let socket: WebSocket | null = null;
    let connecting = false;
    let reconnectTimer: number | null = null;
    let joinTimer: number | null = null;
    let failedAttempts = 0;
    let openedAt: number | null = null;
    const clearJoinTimer = () => {
      if (joinTimer !== null) window.clearTimeout(joinTimer);
      joinTimer = null;
    };
    const scheduleReconnect = () => {
      if (closed || reconnectTimer !== null || !navigator.onLine ||
          document.visibilityState !== "visible") return;
      const delay = realtimeReconnectDelay(failedAttempts++);
      if (delay === null) return;
      reconnectTimer = window.setTimeout(() => {
        reconnectTimer = null;
        void connect();
      }, delay);
    };
    const connect = async () => {
      if (closed || connecting || socket || !navigator.onLine ||
          document.visibilityState !== "visible") return;
      connecting = true;
      setPushConnected(false);
      setPushConnecting(true);
      setPushIssue("");
      let retryNegotiation = true;
      try {
        const negotiation = await apiClient.post<{ clientAccessUri: string; groups?: string[] }>(
          "/commerce/v1/restaurant/command-cards/synchronization/negotiate", {});
        if (closed || !navigator.onLine || document.visibilityState !== "visible") return;
        const group = negotiation.groups?.[0];
        if (!group) throw new Error("La estación no tiene un canal de comandas disponible.");
        const current = new WebSocket(negotiation.clientAccessUri, "json.webpubsub.azure.v1");
        socket = current;
        current.addEventListener("open", () => {
          if (closed || socket !== current ||
              document.visibilityState !== "visible") {
            current.close();
            return;
          }
          openedAt = Date.now();
          current.send(JSON.stringify({ type: "joinGroup", group, ackId: 1 }));
          clearJoinTimer();
          joinTimer = window.setTimeout(() => {
            if (socket !== current || closed) return;
            setPushIssue("No se confirmó la conexión con las comandas.");
            current.close();
          }, 8_000);
        });
        current.addEventListener("message", (event) => {
          if (socket !== current) return;
          try {
            const response = JSON.parse(event.data);
            if (response.type === "ack" && response.ackId === 1) {
              clearJoinTimer();
              if (!response.success) {
                setPushIssue("La estación no pudo unirse al canal de comandas.");
                current.close();
                return;
              }
              setPushConnected(true);
              setPushConnecting(false);
              setPushIssue("");
              void client.invalidateQueries({ queryKey: ["restaurant", businessId, "command-cards"] });
              return;
            }
          } catch { /* Other messages are checked below. */ }
          if (isRestaurantCommandNotice(event.data))
            void client.invalidateQueries({ queryKey: ["restaurant", businessId, "command-cards"] });
        });
        current.addEventListener("close", () => {
          if (closed || socket !== current) return;
          clearJoinTimer();
          socket = null;
          setPushConnected(false);
          setPushConnecting(false);
          setPushIssue((issue) => issue || "Se interrumpió la conexión en vivo.");
          if (wasRealtimeConnectionStable(openedAt, Date.now())) failedAttempts = 0;
          openedAt = null;
          scheduleReconnect();
        });
        current.addEventListener("error", () => {
          setPushIssue("No fue posible abrir el canal de comandas.");
          current.close();
        });
      } catch (error) {
        const statusCode = (error as { statusCode?: number } | null)?.statusCode;
        retryNegotiation = statusCode !== 401 && statusCode !== 403;
        if (!closed) {
          setPushConnected(false);
          setPushConnecting(false);
          setPushIssue(error instanceof Error ? error.message :
            "No se pudo conectar con las comandas.");
        }
      }
      finally {
        connecting = false;
        if (!closed && !socket && retryNegotiation) scheduleReconnect();
      }
    };
    const resume = () => {
      if (document.visibilityState !== "visible" || !navigator.onLine) {
        if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
        reconnectTimer = null;
        socket?.close();
        socket = null;
        setPushConnected(false);
        setPushConnecting(false);
        setPushIssue(navigator.onLine ? "La vista está en segundo plano." :
          "El dispositivo está sin conexión a Internet.");
        return;
      }
      if (socket?.readyState === WebSocket.OPEN) {
        void client.invalidateQueries({ queryKey: ["restaurant", businessId, "command-cards"] });
        return;
      }
      failedAttempts = 0;
      if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
      reconnectTimer = null;
      void connect();
    };
    window.addEventListener("online", resume);
    window.addEventListener("offline", resume);
    document.addEventListener("visibilitychange", resume);
    void connect();
    return () => {
      closed = true;
      window.removeEventListener("online", resume);
      window.removeEventListener("offline", resume);
      document.removeEventListener("visibilitychange", resume);
      if (reconnectTimer !== null) window.clearTimeout(reconnectTimer);
      clearJoinTimer();
      socket?.close();
    };
  }, [businessId, client, pushRevision]);

  const activeStations = stations.data?.items.filter((station) => station.isActive) ?? [];
  const correctionStates = new Set(cards.data?.statesWithPendingCorrections ?? []);
  const tableOptions = (tables.data?.items ?? []).filter((table) => table.isActive)
    .map((table) => ({ value: table.code, label: table.name
      ? `${table.code} · ${table.name}` : `Mesa ${table.code}` }));
  return <main className="min-h-screen bg-background p-4 text-foreground sm:p-6 lg:p-8">
    <header className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <div><p className="text-xs font-bold uppercase tracking-[.18em] text-primary">Restaurante y Bar</p><h1 className="mt-1 text-3xl font-bold tracking-tight">Comandas</h1>
        <Button asChild variant="ghost" size="sm" className="mt-2 -ml-2 text-muted-foreground hover:text-foreground"><Link href={returnHref}><ArrowLeft className="mr-1 h-4 w-4"/>Volver</Link></Button>
      </div>
      <div className="flex min-w-0 flex-wrap items-center justify-end gap-2">
        <span role="status" title={pushIssue || undefined} className={`inline-flex rounded-full px-3 py-2 text-xs font-semibold ${pushConnected ? "bg-emerald-500/10 text-emerald-700 dark:text-emerald-300" : "bg-amber-500/10 text-amber-700 dark:text-amber-300"}`}>{pushConnected ? "En vivo" : pushConnecting ? "Conectando…" : "Sin conexión en vivo"}</span>
        <nav aria-label="Estaciones" className="flex max-w-full gap-2 overflow-x-auto py-1">
          <button type="button" aria-pressed={!stationId} onClick={() => selectStation()}
            className={`flex min-h-12 shrink-0 items-center gap-2 rounded-xl border px-3 text-sm font-bold shadow-sm transition-all active:scale-[.98] ${!stationId ? "border-primary bg-primary/10 text-primary shadow-primary/10" : "bg-card hover:border-primary/50 hover:shadow-md"}`}>
            <LayoutGrid className="h-4 w-4"/><span>Todas</span>
          </button>
          {activeStations.map((station) => {
            const Icon = /bar|barra|coctel/i.test(`${station.code} ${station.name}`) ? Wine : ChefHat;
            const active = stationId === station.stationId;
            return <button key={station.stationId} type="button" aria-pressed={active} onClick={() => selectStation(station.stationId)}
              className={`flex min-h-12 shrink-0 items-center gap-2 rounded-xl border px-3 text-sm font-bold shadow-sm transition-all active:scale-[.98] ${active ? "border-primary bg-primary/10 text-primary shadow-primary/10" : "bg-card hover:border-primary/50 hover:shadow-md"}`}>
              <Icon className="h-4 w-4"/><span>{station.name}</span>
            </button>;
          })}
        </nav>
        <Button variant={soundEnabled ? "default" : "outline"} className="h-12 rounded-2xl" onClick={() => void toggleSound()}><Volume2 className="mr-2 h-5 w-5"/>{soundEnabled ? "Sonido activo" : "Activar sonido"}</Button>
        <Button variant="outline" className="h-12 rounded-2xl" onClick={() => { void cards.refetch(); setPushRevision((revision) => revision + 1); }} aria-label="Actualizar comandas y conexión en vivo" title={pushIssue || "Actualizar comandas y conexión en vivo"}><RefreshCw className="h-5 w-5"/></Button>
      </div>
    </header>
    {businessId && !pushConnected && !pushConnecting && pushIssue &&
      <p role="alert" className="mb-4 rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-950 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100">
        {pushIssue} Las comandas se pueden actualizar con el botón de recarga.
      </p>}
    <div className="grid items-start gap-4 lg:grid-cols-[196px_minmax(0,1fr)]">
      <aside className="min-w-0 rounded-[1.5rem] border bg-card p-3 shadow-sm lg:sticky lg:top-4">
        <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">Estado</p>
        <nav aria-label="Estados de comanda" className="flex gap-2 overflow-x-auto pb-1 lg:flex-col lg:overflow-visible">
          <button type="button" aria-pressed={!state} onClick={() => selectState()}
            className={`flex min-h-12 shrink-0 items-center gap-2 rounded-xl border px-4 text-sm font-bold shadow-sm transition-all active:scale-[.98] lg:w-full ${!state ? "border-primary bg-primary/10 text-primary shadow-primary/10" : "bg-background text-muted-foreground hover:border-primary/50 hover:text-foreground"}`}>
            <LayoutGrid className="h-4 w-4"/>Todas
          </button>
          {states.map((item) => <button key={item.value} type="button" aria-pressed={state === item.value}
            className={`flex min-h-12 shrink-0 items-center gap-2 rounded-xl border px-4 text-sm font-bold shadow-sm transition-all active:scale-[.98] lg:w-full ${state === item.value ? item.active : "bg-background text-muted-foreground hover:border-primary/50 hover:text-foreground"}`}
            onClick={() => selectState(item.value as ActiveCommandState)}><item.icon className="h-4 w-4"/>{item.label}{correctionStates.has(item.value) && <span role="img" aria-label="Corrección pendiente de revisión" title="Corrección pendiente de revisión" className="ml-auto grid h-7 w-7 shrink-0 place-items-center rounded-full bg-amber-200 text-amber-900 dark:bg-amber-800 dark:text-amber-100"><BellRing className="h-4 w-4"/></span>}</button>)}
        </nav>
        <p className="mb-1 mt-4 text-xs font-bold uppercase tracking-wider text-muted-foreground">Buscar por mesa</p>
        <SearchableSelect options={[{ value: "all", label: "Todas las mesas" }, ...tableOptions]}
          value={tableCode ?? "all"} onChange={(value) => selectTable(value === "all" ? undefined : String(value))}
          placeholder="Todas las mesas" searchPlaceholder="Escribe el número o nombre de mesa"
          isLoading={tables.isLoading} className="h-11 rounded-xl" />
      </aside>
      <section className="min-w-0">
    <p className="mb-4 text-sm text-muted-foreground">Orden de llegada: primero las comandas más antiguas. {tableCode && <span className="font-semibold text-foreground">Mesa {tableCode}.</span>}</p>
    {!businessId ? <Empty text="Selecciona una sede para consultar las comandas."/> :
      cards.isError ? <Empty text="No fue posible cargar las comandas. Comprueba la conexión y vuelve a intentarlo."/> :
      cards.isLoading ? <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">{[0, 1, 2].map((item) => <div key={item} className="h-64 animate-pulse rounded-3xl bg-muted"/>)}</div> :
      cards.data?.items.length ? <div className="grid items-start gap-4 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">{cards.data.items.map((card, index) => {
        const style = appearance[card.state as ActiveCommandState];
        return <article key={card.cardId} className={`overflow-hidden rounded-[1.75rem] border bg-card shadow-sm ${style.card}`}>
          <div className={`flex items-start justify-between gap-3 border-b p-5 ${style.header}`}><div><p className="text-xs font-bold uppercase tracking-wider text-primary">{card.stationName} · llegada {String((page - 1) * 60 + index + 1).padStart(2, "0")}</p><button className="mt-1 text-left text-2xl font-bold underline-offset-4 hover:underline" onClick={() => selectTable(card.tableCode)}>Mesa {card.tableCode}</button><p className="mt-1 text-sm text-muted-foreground">{new Date(card.receivedAt).toLocaleString("es-CO", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" })}</p></div><span className={`rounded-full px-3 py-1 text-xs font-semibold shadow-sm ${style.badge}`}>{style.label}</span></div>
          {card.printState && <div className={`mx-5 mt-4 rounded-xl px-3 py-2 text-xs font-semibold ${card.printState === "Failed" || card.printState === "Uncertain" ? "bg-red-100 text-red-900 dark:bg-red-900/40 dark:text-red-100" : "bg-sky-100 text-sky-900 dark:bg-sky-900/40 dark:text-sky-100"}`} role={card.printState === "Failed" || card.printState === "Uncertain" ? "alert" : undefined}>
            {({ Pending: "Tirilla pendiente de envío", Claimed: "Tirilla aceptada por el equipo", Submitted: "Tirilla entregada a Windows", Failed: "No se pudo imprimir la tirilla", Uncertain: "Resultado de impresión incierto" } as Record<string, string>)[card.printState]}
            {card.printError && <span className="block font-normal">{card.printError}</span>}
            {canManage && (card.printState === "Failed" || card.printState === "Uncertain" || card.printState === "Submitted") && <button type="button" disabled={reprint.isPending} onClick={() => reprint.mutate(card.cardId)} className="mt-2 rounded-lg border border-current px-3 py-1.5 text-xs font-bold transition-colors hover:bg-background/70 disabled:opacity-50">Reimprimir copia</button>}
          </div>}
          <ul className="divide-y px-5">{card.lines.map((line) => <li key={line.additionId} className="flex gap-3 py-4"><span className="grid h-9 min-w-9 place-items-center rounded-xl bg-primary/10 px-1 font-bold text-primary">{line.quantity}×</span><div className="min-w-0 flex-1"><p className="font-semibold">{line.productName}</p>{line.preparationNote && <p className="mt-1 whitespace-pre-wrap rounded-lg bg-amber-100 px-2 py-1 text-sm font-semibold text-amber-950 dark:bg-amber-900/40 dark:text-amber-100">{line.preparationNote}</p>}<p className="mt-1 text-xs text-muted-foreground">{line.addedByName}</p>
            {line.cancellationId && <div className="mt-3 rounded-xl border border-red-300 bg-red-50 p-3 text-sm text-red-950 dark:border-red-800 dark:bg-red-950/50 dark:text-red-100"><p className="font-bold">Cancelación solicitada · {line.cancellationQuantity} unidad(es)</p><p className="mt-1">{line.cancellationStatus === "Requested" ? "Confirma si se preparó antes de continuar." : line.cancellationPreparationState === "NotPrepared" ? "Sin preparar · espera aprobación." : "Preparado o parcialmente preparado · registrar consumo antes de aprobar."}</p>
              {canManage && line.cancellationStatus === "Requested" && line.cancellationRowVersion && <div className="mt-3 flex flex-wrap gap-2"><Button size="sm" variant="outline" disabled={confirmCancellation.isPending} onClick={() => confirmCancellation.mutate({ cancellationId: line.cancellationId!, preparationState: "NotPrepared", rowVersion: line.cancellationRowVersion! })}>No se preparó</Button><Button size="sm" variant="outline" disabled={confirmCancellation.isPending} onClick={() => confirmCancellation.mutate({ cancellationId: line.cancellationId!, preparationState: "Prepared", rowVersion: line.cancellationRowVersion! })}>Ya se preparó</Button><Button size="sm" variant="outline" disabled={confirmCancellation.isPending} onClick={() => { setPartialQuantities({}); setPartialRequest({ cancellationId: line.cancellationId!, rowVersion: line.cancellationRowVersion!, productName: line.productName }); }}>Parcial</Button></div>}
              {canApprove && line.cancellationRowVersion && <div className="mt-3 flex flex-wrap gap-2">{line.cancellationStatus === "Confirmed" && <Button size="sm" disabled={resolveCancellation.isPending} onClick={() => resolveCancellation.mutate({ cancellationId: line.cancellationId!, approve: true, rowVersion: line.cancellationRowVersion! })}>Aprobar retiro</Button>}<Button size="sm" variant="outline" disabled={resolveCancellation.isPending} onClick={() => resolveCancellation.mutate({ cancellationId: line.cancellationId!, approve: false, rowVersion: line.cancellationRowVersion! })}>Rechazar</Button></div>}
            </div>}
          </div></li>)}</ul>
          {canManage && ({ Pending: ["Preparing", "Empezar preparación"], Preparing: ["Ready", "Marcar lista"], Ready: ["ToDeliver", "Enviar a entrega"], ToDeliver: ["Delivered", "Marcar entregada"] } as Record<string, [string, string]>)[card.state] && <div className="border-t p-4"><Button className="h-12 w-full rounded-2xl text-base" disabled={pendingCardIds.has(card.cardId) || card.lines.some((line) => !!line.cancellationId)} onClick={() => { const [state] = ({ Pending: ["Preparing"], Preparing: ["Ready"], Ready: ["ToDeliver"], ToDeliver: ["Delivered"] } as Record<string, string[]>)[card.state]; changeState.mutate({ cardId: card.cardId, state, rowVersion: card.rowVersion }); }}>{({ Pending: "Empezar preparación", Preparing: "Marcar lista", Ready: "Enviar a entrega", ToDeliver: "Marcar entregada" } as Record<string,string>)[card.state]}</Button></div>}
        </article>})}</div> : <Empty text="No hay comandas con esos filtros."/>}
    {cards.data && (page > 1 || cards.data.hasMore) && <nav className="mt-6 flex items-center justify-center gap-3" aria-label="Páginas de comandas">
      <Button variant="outline" className="h-12 rounded-2xl" disabled={page === 1} onClick={() => { knownCards.current = null; latestArrival.current = null; setPage((value) => value - 1); }}>Anterior</Button>
      <span className="text-sm font-medium">Página {page}</span>
      <Button variant="outline" className="h-12 rounded-2xl" disabled={!cards.data.hasMore} onClick={() => { knownCards.current = null; latestArrival.current = null; setPage((value) => value + 1); }}>Siguiente</Button>
    </nav>}
      </section>
    </div>
    {partialRequest && <div className="fixed inset-0 z-50 grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-label="Consumo parcial"><div className="w-full max-w-xl rounded-[2rem] border bg-card p-6 shadow-2xl"><p className="text-xs font-bold uppercase tracking-widest text-primary">Confirmación de estación</p><h2 className="mt-1 text-2xl font-bold">Consumo parcial · {partialRequest.productName}</h2><p className="mt-2 text-sm text-muted-foreground">Registra la cantidad real usada de cada ingrediente o producto terminado. Solo esa cantidad saldrá del inventario si el supervisor aprueba.</p>
      {partialComponents.isLoading && <p className="mt-5 text-sm">Cargando componentes…</p>}{partialComponents.isError && <p role="alert" className="mt-5 text-sm text-destructive">No se pudieron cargar los componentes.</p>}
      <div className="mt-5 max-h-80 space-y-3 overflow-y-auto">{partialComponents.data?.map((component) => <label key={component.productId} className="flex items-center justify-between gap-3 rounded-xl border p-3"><span className="min-w-0 text-sm font-semibold">{component.productName}<small className="block font-normal text-muted-foreground">Receta completa: {component.expectedQuantity}</small></span><Input type="number" min="0" step="0.000001" className="h-12 w-32 text-right" value={partialQuantities[component.productId] ?? 0} onChange={(event) => setPartialQuantities((current) => ({ ...current, [component.productId]: Number(event.target.value) }))}/></label>)}</div>
      {!partialComponents.isLoading && !partialComponents.data?.length && <p className="mt-4 text-sm text-muted-foreground">Este producto no tiene componentes inventariables. Confirma “Ya se preparó” en la tarjeta.</p>}
      <div className="mt-6 flex justify-end gap-2"><Button variant="outline" disabled={confirmCancellation.isPending} onClick={() => setPartialRequest(null)}>Volver</Button><Button disabled={confirmCancellation.isPending || !partialComponents.data?.some((item) => (partialQuantities[item.productId] ?? 0) > 0) || Object.values(partialQuantities).some((value) => !Number.isFinite(value) || value < 0 || Number(value.toFixed(6)) !== value)} onClick={() => confirmCancellation.mutate({ cancellationId: partialRequest.cancellationId, preparationState: "Partial", rowVersion: partialRequest.rowVersion, consumedComponents: partialComponents.data?.filter((item) => (partialQuantities[item.productId] ?? 0) > 0).map((item) => ({ productId: item.productId, quantity: partialQuantities[item.productId] })) })}>{confirmCancellation.isPending ? "Confirmando…" : "Confirmar consumo"}</Button></div>
    </div></div>}
  </main>;
}

function Empty({ text }: { text: string }) {
  return <div className="grid min-h-[45vh] place-items-center rounded-[2rem] border border-dashed bg-card/60 p-8 text-center"><div><ChefHat className="mx-auto h-12 w-12 text-primary/60"/><p className="mt-4 text-lg font-medium">{text}</p></div></div>;
}
