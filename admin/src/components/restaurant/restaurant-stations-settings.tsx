"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChefHat, Pencil, Plus, Printer } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { restaurantMasterApi, type RestaurantCommandConfiguration, type RestaurantStation } from "@/services/api/restaurant-master";
import { RestaurantProductRoutingSettings } from "@/components/restaurant/restaurant-product-routing-settings";
import { PosEdgeClient, readEdgeTokenFromLaunch, readEdgeUserSession,
  type RestaurantCommandPrinterProfile } from "@/services/pos/pos-edge-client";

type Draft = Omit<RestaurantStation, "businessId" | "targetMinutes"> & { targetMinutes: string };
const empty = (): Draft => ({ stationId: crypto.randomUUID(), code: "", name: "", sortOrder: 0,
  kind: "Preparation", targetMinutes: "", isActive: true, rowVersion: "" });

export function RestaurantStationsSettings({ businessId, canConfigure }: { businessId: string; canConfigure: boolean }) {
  const [page, setPage] = useState(1);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [printStation, setPrintStation] = useState<RestaurantStation | null>(null);
  const [edgeClient, setEdgeClient] = useState<PosEdgeClient | null>(null);
  useEffect(() => {
    const token = readEdgeTokenFromLaunch();
    if (token) setEdgeClient(new PosEdgeClient(token, readEdgeUserSession()));
  }, []);
  const queryClient = useQueryClient();
  const stations = useQuery({ queryKey: ["restaurant", businessId, "stations", page],
    queryFn: () => restaurantMasterApi.stations(page) });
  const save = useMutation({
    mutationFn: (value: Draft) => {
      const fields = { code: value.code.trim(), name: value.name.trim(), sortOrder: value.sortOrder,
        kind: value.kind, targetMinutes: value.targetMinutes ? Number(value.targetMinutes) : null };
      return value.rowVersion
        ? restaurantMasterApi.updateStation(value.stationId, { ...fields, isActive: value.isActive, rowVersion: value.rowVersion })
        : restaurantMasterApi.createStation({ ...fields, stationId: value.stationId, businessId });
    },
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ["restaurant", businessId, "stations"] });
      setDraft(null); toast.success("Estación guardada."); },
    onError: () => toast.error("No se pudo guardar. Comprueba si la estación cambió o tiene productos asignados."),
  });
  return <section className="space-y-3" aria-label="Estaciones">
    <div className="flex flex-wrap items-center justify-between gap-3"><div><h2 className="text-lg font-semibold">Estaciones</h2>
      <p className="text-sm text-muted-foreground">Organiza cocina, barra y puntos de retiro de esta sede.</p></div>
      {canConfigure && <Button variant="outline" onClick={() => setDraft(empty())}><Plus className="mr-2 h-4 w-4"/>Nueva estación</Button>}</div>
    {stations.isError ? <div role="alert" className="rounded-2xl border p-4">No se pudieron cargar las estaciones. <Button variant="link" onClick={() => void stations.refetch()}>Reintentar</Button></div> :
      stations.isLoading ? <div className="h-20 animate-pulse rounded-2xl bg-muted"/> :
      stations.data?.items.length ? <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">{stations.data.items.map((station) =>
        <div key={station.stationId} className="flex min-h-24 items-center gap-4 rounded-3xl border bg-card p-5 shadow-sm">
          <div className="grid h-12 w-12 shrink-0 place-items-center rounded-2xl bg-primary/10 text-primary"><ChefHat className="h-6 w-6"/></div>
          <div className="min-w-0 flex-1"><p className="truncate font-semibold">{station.name}</p><p className="text-xs text-muted-foreground">{station.code} · {station.kind === "Preparation" ? "Preparación" : "Retiro"}{station.targetMinutes ? ` · ${station.targetMinutes} min` : ""}</p>{!station.isActive && <p className="text-xs text-muted-foreground">Inactiva</p>}</div>
          {canConfigure && <div className="flex gap-1"><Button variant="ghost" size="icon" aria-label={`Configurar comanda ${station.name}`} onClick={() => setPrintStation(station)}><Printer className="h-4 w-4"/></Button><Button variant="ghost" size="icon" aria-label={`Editar estación ${station.name}`} onClick={() => setDraft({ ...station, targetMinutes: station.targetMinutes?.toString() ?? "" })}><Pencil className="h-4 w-4"/></Button></div>}
        </div>)}</div> : <div className="rounded-3xl border border-dashed p-6 text-center text-sm text-muted-foreground">Crea la primera estación para organizar dónde se preparan o retiran los productos.</div>}
    {(stations.data?.totalCount ?? 0) > 100 && <div className="flex justify-end gap-2"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>Anterior</Button><Button variant="outline" size="sm" disabled={page * 100 >= (stations.data?.totalCount ?? 0)} onClick={() => setPage(page + 1)}>Siguiente</Button></div>}
    {canConfigure && <RestaurantProductRoutingSettings businessId={businessId} stations={stations.data?.items ?? []} canConfigure />}
    {printStation && <CommandConfigurationDialog businessId={businessId} station={printStation} client={edgeClient} onClose={() => setPrintStation(null)} />}
    <Dialog open={!!draft} onOpenChange={(open) => { if (!open && !save.isPending) setDraft(null); }}><DialogContent><DialogHeader><DialogTitle>{draft?.rowVersion ? "Editar estación" : "Nueva estación"}</DialogTitle></DialogHeader>
      <div className="grid gap-4"><div className="grid gap-2"><Label htmlFor="station-code">Código</Label><Input id="station-code" maxLength={32} value={draft?.code ?? ""} onChange={(event) => setDraft((v) => v && { ...v, code: event.target.value })} placeholder="COCINA"/></div>
        <div className="grid gap-2"><Label htmlFor="station-name">Nombre</Label><Input id="station-name" maxLength={100} value={draft?.name ?? ""} onChange={(event) => setDraft((v) => v && { ...v, name: event.target.value })} placeholder="Cocina principal"/></div>
        <div className="grid grid-cols-2 gap-3"><div className="grid gap-2"><Label>Tipo</Label><Select value={draft?.kind ?? "Preparation"} onValueChange={(kind: "Preparation" | "Pickup") => setDraft((v) => v && { ...v, kind })}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Preparation">Preparación</SelectItem><SelectItem value="Pickup">Retiro</SelectItem></SelectContent></Select></div>
          <div className="grid gap-2"><Label htmlFor="station-target">Meta (min)</Label><Input id="station-target" type="number" min={1} max={32767} value={draft?.targetMinutes ?? ""} onChange={(event) => setDraft((v) => v && { ...v, targetMinutes: event.target.value })}/></div></div>
        {draft?.rowVersion && <div className="flex items-center justify-between rounded-xl border p-3"><Label htmlFor="station-active">Estación activa</Label><Switch id="station-active" checked={draft.isActive} onCheckedChange={(isActive) => setDraft((v) => v && { ...v, isActive })}/></div>}
        <Button disabled={!draft?.code.trim() || !draft.name.trim() || save.isPending || (draft.targetMinutes !== "" && (!Number.isInteger(Number(draft.targetMinutes)) || Number(draft.targetMinutes) < 1 || Number(draft.targetMinutes) > 32767))} onClick={() => draft && save.mutate(draft)}>{save.isPending ? "Guardando…" : "Guardar estación"}</Button>
      </div></DialogContent></Dialog>
  </section>;
}

function CommandConfigurationDialog({ businessId, station, client, onClose }: {
  businessId: string; station: RestaurantStation; client: PosEdgeClient | null; onClose: () => void;
}) {
  const [draft, setDraft] = useState<RestaurantCommandConfiguration | null>(null);
  const [printerDraft, setPrinterDraft] = useState<RestaurantCommandPrinterProfile | null>(null);
  const [saveStage, setSaveStage] = useState<"printer" | "command" | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const configurationKey = ["restaurant", businessId, "command-configuration", station.stationId];
  const configuration = useQuery({ queryKey: ["restaurant", businessId, "command-configuration", station.stationId],
    queryFn: () => restaurantMasterApi.commandConfiguration(station.stationId) });
  const current = draft ?? configuration.data;
  const physical = Boolean(current && current.mode !== "Virtual");
  const devices = useQuery({ queryKey: ["restaurant", businessId, "print-devices"],
    queryFn: restaurantMasterApi.printDevices, enabled: physical });
  const localPrinterKey = ["restaurant", businessId, "station-printers", station.stationId];
  const localPrinters = useQuery({ queryKey: localPrinterKey,
    queryFn: async () => {
      if (!client) throw new Error("Abre esta configuración desde la aplicación instalada.");
      const [health, view, profiles] = await Promise.all([
        client.health(), client.printerConfiguration(), client.restaurantCommandPrinterProfiles(),
      ]);
      return { deviceId: health.deviceId, printers: view.installedPrinters, profiles };
    }, enabled: physical && Boolean(client), staleTime: Infinity, gcTime: 0 });
  const thisDeviceId = localPrinters.data?.deviceId;
  const thisDeviceAvailable = Boolean(thisDeviceId && devices.data?.some(device => device.deviceId === thisDeviceId));
  const deviceId = current?.printerDeviceId ?? (thisDeviceAvailable ? thisDeviceId : null);
  const isThisDevice = Boolean(thisDeviceAvailable && deviceId === thisDeviceId);
  const profileKey = current?.printerProfileKey || station.stationId;
  const existingProfile = localPrinters.data?.profiles.find(profile => profile.key.toLowerCase() === profileKey.toLowerCase());
  const printer = printerDraft ?? existingProfile ?? {
    key: profileKey, printerName: "", paperWidthMillimeters: 80, copies: 1, cut: true,
  } satisfies RestaurantCommandPrinterProfile;
  const remoteConfigured = Boolean(deviceId && deviceId === configuration.data?.printerDeviceId
    && configuration.data.printerProfileKey);
  const printerReady = isThisDevice
    ? Boolean(localPrinters.isSuccess && printer.printerName && Number.isInteger(printer.copies)
        && printer.copies >= 1 && printer.copies <= 3)
    : remoteConfigured;
  const save = useMutation({
    mutationFn: async (value: RestaurantCommandConfiguration) => {
      setSaveError(null);
      let savedPrinter = false;
      let savedKey = value.printerProfileKey;
      try {
        if (value.mode !== "Virtual" && isThisDevice) {
          if (!client || !printerReady) throw new Error("Selecciona una impresora válida para esta estación.");
          setSaveStage("printer");
          const saved = await client.saveRestaurantCommandPrinterProfile({ ...printer, key: profileKey });
          savedPrinter = true;
          savedKey = saved.key;
          setPrinterDraft(saved);
          queryClient.setQueryData(localPrinterKey, localPrinters.data && {
            ...localPrinters.data,
            profiles: [...localPrinters.data.profiles.filter(profile => profile.key.toLowerCase() !== saved.key.toLowerCase()), saved],
          });
        }
        setSaveStage("command");
        return await restaurantMasterApi.updateCommandConfiguration(station.stationId, {
          name: value.name.trim(), mode: value.mode,
          printerDeviceId: value.mode === "Virtual" ? null : deviceId ?? null,
          printerProfileKey: value.mode === "Virtual" ? null : savedKey,
          rowVersion: value.rowVersion,
        });
      } catch (error) {
        const detail = error instanceof Error ? error.message : "No se pudo guardar la comanda.";
        throw new Error(savedPrinter ? `La impresora quedó guardada en este equipo. Falta guardar la comanda: ${detail}` : detail);
      } finally { setSaveStage(null); }
    },
    onSuccess: value => { queryClient.setQueryData(configurationKey, value); toast.success("Comanda configurada."); onClose(); },
    onError: error => { setSaveError(error.message); toast.error(error.message); },
  });
  const change = (update: Partial<RestaurantCommandConfiguration>) => {
    if (current) setDraft({ ...current, ...update });
  };
  return <Dialog open onOpenChange={(open) => { if (!open && !save.isPending) onClose(); }}><DialogContent>
    <DialogHeader><DialogTitle>Comanda de {station.name}</DialogTitle></DialogHeader>
    {configuration.isLoading ? <div className="h-28 animate-pulse rounded-xl bg-muted"/> : configuration.isError || !current ?
      <p role="alert">No se pudo cargar la comanda de esta estación.</p> :
      <fieldset className="grid min-w-0 gap-4" disabled={save.isPending}>
        <div className="grid gap-2"><Label htmlFor="command-name">Nombre de la comanda</Label><Input id="command-name" maxLength={100} value={current.name} onChange={(event) => change({ name: event.target.value })}/></div>
        <div className="grid gap-2"><Label>Salida</Label><Select value={current.mode} onValueChange={(mode: RestaurantCommandConfiguration["mode"]) => change({ mode })}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="Virtual">Solo pantalla</SelectItem><SelectItem value="Both">Pantalla e impresora</SelectItem><SelectItem value="Physical">Solo impresora</SelectItem></SelectContent></Select></div>
        {current.mode !== "Virtual" && <>
          <div className="grid gap-2"><Label>Equipo que imprime</Label><Select value={deviceId ?? ""} onValueChange={(printerDeviceId) => change({ printerDeviceId })}><SelectTrigger aria-label="Equipo que imprime"><SelectValue placeholder="Selecciona el equipo"/></SelectTrigger><SelectContent>{devices.data?.map(device => <SelectItem key={device.deviceId} value={device.deviceId}>{device.name}{device.deviceId === thisDeviceId ? " · Este equipo" : ""}</SelectItem>)}</SelectContent></Select></div>
          {devices.isError && <p role="alert" className="text-sm text-destructive">No se pudieron consultar los equipos de impresión.</p>}
          {client && localPrinters.isPending && <p className="text-sm text-muted-foreground">Consultando las impresoras de este equipo…</p>}
          {localPrinters.isError && <p role="alert" className="text-sm text-destructive">{localPrinters.error.message}</p>}
          {isThisDevice && <div className="space-y-4 rounded-2xl border bg-muted/30 p-4">
            <div className="grid gap-2"><Label>Impresora Windows</Label><Select value={printer.printerName || undefined} onValueChange={printerName => setPrinterDraft({ ...printer, printerName })}><SelectTrigger aria-label="Impresora Windows"><SelectValue placeholder="Selecciona una impresora"/></SelectTrigger><SelectContent>{localPrinters.data?.printers.map(name => <SelectItem key={name} value={name}>{name}</SelectItem>)}</SelectContent></Select></div>
            {!localPrinters.data?.printers.length && <p className="text-sm text-amber-700">Windows no reporta impresoras instaladas. Instala su controlador y actualiza la lista.</p>}
            <div className="grid grid-cols-2 gap-3"><div className="grid gap-2"><Label>Tamaño de tirilla</Label><Select value={String(printer.paperWidthMillimeters)} onValueChange={width => setPrinterDraft({ ...printer, paperWidthMillimeters: Number(width) as 58 | 80 })}><SelectTrigger aria-label="Tamaño de tirilla"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="80">80 mm</SelectItem><SelectItem value="58">58 mm</SelectItem></SelectContent></Select></div><div className="grid gap-2"><Label htmlFor="command-copies">Copias</Label><Input id="command-copies" type="number" min={1} max={3} value={printer.copies} onChange={event => setPrinterDraft({ ...printer, copies: Number(event.target.value) })}/></div></div>
            <label className="flex items-center justify-between gap-3"><span className="text-sm font-medium">Corte de papel</span><Switch checked={printer.cut} onCheckedChange={cut => setPrinterDraft({ ...printer, cut })}/></label>
            <Button variant="outline" size="sm" disabled={localPrinters.isFetching || save.isPending} onClick={() => void localPrinters.refetch()}>Actualizar impresoras</Button>
          </div>}
          {!isThisDevice && (!client || !localPrinters.isPending) && <p className="text-sm text-muted-foreground">{deviceId
            ? "Para seleccionar o cambiar la impresora, abre esta comanda en el equipo que imprime."
            : "Abre esta configuración en el equipo de impresión registrado y selecciona su impresora."}</p>}
        </>}
        {saveError && <p role="alert" className="text-sm text-destructive">{saveError}</p>}
        <Button disabled={save.isPending || !current.name.trim() || (physical && (!deviceId || !printerReady))} onClick={() => save.mutate(current)}>{saveStage === "printer" ? "Guardando impresora…" : save.isPending ? "Guardando comanda…" : "Guardar comanda"}</Button>
      </fieldset>}
  </DialogContent></Dialog>;
}
