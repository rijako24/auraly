"use client";

import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChefHat, Pencil, Plus } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { restaurantMasterApi, type RestaurantCommandConfiguration, type RestaurantMasterPage, type RestaurantStation } from "@/services/api/restaurant-master";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { RestaurantProductRoutingSettings } from "@/components/restaurant/restaurant-product-routing-settings";
import { PosEdgeClient, readEdgeTokenFromLaunch, readEdgeUserSession,
  type RestaurantCommandPrinterProfile } from "@/services/pos/pos-edge-client";

type Draft = Omit<RestaurantStation, "businessId" | "targetMinutes"> & { targetMinutes: string };
const empty = (): Draft => {
  const stationId = crypto.randomUUID();
  return { stationId, code: stationId.replaceAll("-", ""), name: "", sortOrder: 0,
    kind: "Preparation", targetMinutes: "", isActive: true, rowVersion: "" };
};
type CommandDraft = Pick<RestaurantCommandConfiguration, "name" | "mode" | "printerDeviceId" | "printerProfileKey">;

export function RestaurantStationsSettings({ businessId, canConfigure }: { businessId: string; canConfigure: boolean }) {
  const [page, setPage] = useState(1);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [edgeClient, setEdgeClient] = useState<PosEdgeClient | null>(null);
  useEffect(() => {
    const token = readEdgeTokenFromLaunch();
    if (token) setEdgeClient(new PosEdgeClient(token, readEdgeUserSession()));
  }, []);
  const queryClient = useQueryClient();
  const stations = useQuery({ queryKey: ["restaurant", businessId, "stations", page],
    queryFn: () => restaurantMasterApi.stations(page) });
  const kinds = useReferenceOptions("restaurant-station-kind");
  const modes = useReferenceOptions("restaurant-command-mode", Boolean(draft));
  function stationSaved(value: RestaurantStation, created: boolean) {
    const key = ["restaurant", businessId, "stations", page];
    const current = queryClient.getQueryData<RestaurantMasterPage<RestaurantStation>>(key);
    if (created || current?.items.some(item => item.stationId === value.stationId && item.name !== value.name)) {
      // Creation or renaming can change membership in the SQL page ordered by
      // SortOrder,Name,StationId. Only the server can resolve that page boundary.
      void stations.refetch();
    } else {
      queryClient.setQueryData<RestaurantMasterPage<RestaurantStation>>(
        key, current => current && {
          ...current, items: current.items.map(item => item.stationId === value.stationId ? value : item),
        });
    }
  }
  return <section className="space-y-3" aria-label="Estaciones">
    <div className="flex flex-wrap items-center justify-between gap-3"><div><h2 className="text-lg font-semibold">Estaciones</h2>
      <p className="text-sm text-muted-foreground">Organiza cocina, barra y puntos de retiro de esta sede.</p></div>
      {canConfigure && <Button variant="outline" onClick={() => setDraft(empty())}><Plus className="mr-2 h-4 w-4"/>Nueva estación</Button>}</div>
    {kinds.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar los tipos de estación. <Button variant="link" onClick={() => void kinds.refetch()}>Reintentar</Button></p>}
    {stations.isError ? <div role="alert" className="rounded-2xl border p-4">No se pudieron cargar las estaciones. <Button variant="link" onClick={() => void stations.refetch()}>Reintentar</Button></div> :
      stations.isLoading ? <div className="h-20 animate-pulse rounded-2xl bg-muted"/> :
      stations.data?.items.length ? <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">{stations.data.items.map((station) =>
        <div key={station.stationId} className="flex min-h-24 items-center gap-4 rounded-3xl border bg-card p-5 shadow-sm">
          <div className="grid h-12 w-12 shrink-0 place-items-center rounded-2xl bg-primary/10 text-primary"><ChefHat className="h-6 w-6"/></div>
          <div className="min-w-0 flex-1"><p className="truncate font-semibold">{station.name}</p><p className="text-xs text-muted-foreground">{kinds.data?.find(option => option.code === station.kind)?.label}{station.targetMinutes ? ` · ${station.targetMinutes} min` : ""}</p>{!station.isActive && <p className="text-xs text-muted-foreground">Inactiva</p>}</div>
          {canConfigure && <Button variant="ghost" size="icon" aria-label={`Editar estación ${station.name}`} onClick={() => setDraft({ ...station, targetMinutes: station.targetMinutes?.toString() ?? "" })}><Pencil className="h-4 w-4"/></Button>}
        </div>)}</div> : <div className="rounded-3xl border border-dashed p-6 text-center text-sm text-muted-foreground">Crea la primera estación para organizar dónde se preparan o retiran los productos.</div>}
    {(stations.data?.totalCount ?? 0) > 100 && <div className="flex justify-end gap-2"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>Anterior</Button><Button variant="outline" size="sm" disabled={page * 100 >= (stations.data?.totalCount ?? 0)} onClick={() => setPage(page + 1)}>Siguiente</Button></div>}
    {canConfigure && <RestaurantProductRoutingSettings businessId={businessId} stations={stations.data?.items ?? []} canConfigure />}
    {draft && <StationDialog key={draft.stationId} businessId={businessId} initialStation={draft} client={edgeClient}
      kinds={kinds} modes={modes} onStationSaved={stationSaved} onClose={() => setDraft(null)} />}
  </section>;
}

function StationDialog({ businessId, initialStation, client, kinds, modes, onStationSaved, onClose }: {
  businessId: string; initialStation: Draft; client: PosEdgeClient | null;
  kinds: ReturnType<typeof useReferenceOptions>; modes: ReturnType<typeof useReferenceOptions>;
  onStationSaved: (station: RestaurantStation, created: boolean) => void; onClose: () => void;
}) {
  const [station, setStation] = useState(initialStation);
  const [stationDraft, setStationDraft] = useState(initialStation);
  const [draft, setDraft] = useState<CommandDraft | null>(null);
  const [printerDraft, setPrinterDraft] = useState<RestaurantCommandPrinterProfile | null>(null);
  const [saveStage, setSaveStage] = useState<"station" | "printer" | "command" | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const queryClient = useQueryClient();
  const configurationKey = ["restaurant", businessId, "command-configuration", station.stationId];
  const configuration = useQuery({ queryKey: ["restaurant", businessId, "command-configuration", station.stationId],
    queryFn: () => restaurantMasterApi.commandConfiguration(station.stationId), enabled: Boolean(initialStation.rowVersion) });
  const current = draft ?? configuration.data ?? (!initialStation.rowVersion ? {
    name: stationDraft.name, mode: "Virtual" as const, printerDeviceId: null, printerProfileKey: null,
  } : null);
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
    mutationFn: async (value: CommandDraft) => {
      setSaveError(null);
      let savedPrinter = false;
      let savedStation = false;
      let savedCommand = false;
      let savedKey = value.printerProfileKey;
      try {
        const fields = { code: stationDraft.code, name: stationDraft.name.trim(), sortOrder: stationDraft.sortOrder,
          kind: stationDraft.kind, targetMinutes: stationDraft.targetMinutes ? Number(stationDraft.targetMinutes) : null };
        const created = !station.rowVersion;
        const deactivating = station.isActive && !stationDraft.isActive;
        const persistStation = async () => {
          if (!created && fields.name === station.name && fields.kind === station.kind &&
              stationDraft.targetMinutes === station.targetMinutes && stationDraft.isActive === station.isActive) return;
          setSaveStage("station");
          const saved = created
            ? await restaurantMasterApi.createStation({ ...fields, stationId: station.stationId, businessId })
            : await restaurantMasterApi.updateStation(station.stationId, { ...fields, isActive: stationDraft.isActive, rowVersion: station.rowVersion });
          const accepted = { ...saved, targetMinutes: saved.targetMinutes?.toString() ?? "" };
          setStation(accepted);
          setStationDraft(accepted);
          savedStation = true;
          onStationSaved(saved, created);
        };
        // Commands can only be configured while the station is active. Apply
        // deactivation last, without weakening the server's operational guard.
        if (!deactivating) await persistStation();
        if (value.mode !== "Virtual" && isThisDevice) {
          if (!client || !printerReady) throw new Error("Selecciona una impresora válida para esta estación.");
          if (!existingProfile || printer.printerName !== existingProfile.printerName ||
              printer.paperWidthMillimeters !== existingProfile.paperWidthMillimeters ||
              printer.copies !== existingProfile.copies || printer.cut !== existingProfile.cut) {
            setSaveStage("printer");
            const saved = await client.saveRestaurantCommandPrinterProfile({ ...printer, key: profileKey });
            savedPrinter = true;
            savedKey = saved.key;
            setPrinterDraft(saved);
            queryClient.setQueryData(localPrinterKey, localPrinters.data && {
              ...localPrinters.data,
              profiles: [...localPrinters.data.profiles.filter(profile => profile.key.toLowerCase() !== saved.key.toLowerCase()), saved],
            });
          } else savedKey = existingProfile.key;
        }
        const commandFields = {
          name: value.name.trim(), mode: value.mode,
          printerDeviceId: value.mode === "Virtual" ? null : deviceId ?? null,
          printerProfileKey: value.mode === "Virtual" ? null : savedKey,
        };
        // Creation already accepts a virtual command with the station's name.
        // Its version is needed only when that default must be changed.
        if (created && commandFields.mode === "Virtual" && commandFields.name === fields.name) return;
        setSaveStage("command");
        const authoritative = configuration.data ?? await restaurantMasterApi.commandConfiguration(station.stationId);
        if (!configuration.data) queryClient.setQueryData(configurationKey, authoritative);
        if (commandFields.name !== authoritative.name || commandFields.mode !== authoritative.mode ||
            commandFields.printerDeviceId !== authoritative.printerDeviceId || commandFields.printerProfileKey !== authoritative.printerProfileKey) {
          const saved = await restaurantMasterApi.updateCommandConfiguration(station.stationId, {
            ...commandFields, rowVersion: authoritative.rowVersion,
          });
          queryClient.setQueryData(configurationKey, saved);
          setDraft(saved);
          savedCommand = true;
        }
        if (deactivating) await persistStation();
      } catch (error) {
        const detail = error instanceof Error ? error.message : "No se pudo guardar la estación.";
        if (savedCommand) throw new Error(`La comanda quedó guardada. Falta guardar la estación: ${detail}`);
        throw new Error(savedStation || savedPrinter
          ? `La ${savedPrinter ? "impresora" : "estación"} quedó guardada. Falta completar la comanda: ${detail}` : detail);
      } finally { setSaveStage(null); }
    },
    onSuccess: () => { toast.success("Estación y comanda guardadas."); onClose(); },
    onError: error => { setSaveError(error.message); toast.error(error.message); },
  });
  const change = (update: Partial<CommandDraft>) => {
    if (current) setDraft({ ...current, ...update });
  };
  const validTarget = stationDraft.targetMinutes === "" || (Number.isInteger(Number(stationDraft.targetMinutes)) &&
    Number(stationDraft.targetMinutes) >= 1 && Number(stationDraft.targetMinutes) <= 32767);
  const catalogsReady = Boolean(kinds.data?.some(option => option.code === stationDraft.kind) &&
    modes.data?.some(option => option.code === current?.mode));
  return <Dialog open onOpenChange={(open) => { if (!open && !save.isPending) onClose(); }}><DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-xl">
    <DialogHeader><DialogTitle>{initialStation.rowVersion ? `Editar estación ${initialStation.name}` : "Nueva estación"}</DialogTitle></DialogHeader>
    {(kinds.isError || modes.isError) && <p role="alert" className="text-sm text-destructive">No se pudieron cargar las opciones de estación y comanda. <Button variant="link" onClick={() => { void kinds.refetch(); void modes.refetch(); }}>Reintentar</Button></p>}
    {initialStation.rowVersion && configuration.isPending ? <div className="h-28 animate-pulse rounded-xl bg-muted"/> : configuration.isError || !current ?
      <p role="alert">No se pudo cargar la comanda de esta estación. <Button variant="link" onClick={() => void configuration.refetch()}>Reintentar</Button></p> :
      <fieldset className="grid min-w-0 gap-4" disabled={save.isPending}>
        <div className="grid gap-2"><Label htmlFor="station-name">Nombre</Label><Input id="station-name" maxLength={100} value={stationDraft.name} onChange={event => {
          const name = event.target.value;
          if (draft?.name === stationDraft.name) setDraft({ ...draft, name });
          setStationDraft({ ...stationDraft, name });
        }} placeholder="Cocina principal"/></div>
        <div className="grid grid-cols-2 gap-3"><div className="grid gap-2"><Label htmlFor="station-kind">Tipo</Label><Select value={stationDraft.kind} onValueChange={(kind: RestaurantStation["kind"]) => setStationDraft({ ...stationDraft, kind })}><SelectTrigger id="station-kind" aria-label="Tipo de estación"><SelectValue/></SelectTrigger><SelectContent>{kinds.data?.map(option => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></div>
          <div className="grid gap-2"><Label htmlFor="station-target">Meta (min)</Label><Input id="station-target" type="number" min={1} max={32767} value={stationDraft.targetMinutes} onChange={event => setStationDraft({ ...stationDraft, targetMinutes: event.target.value })}/></div></div>
        {station.rowVersion && <div className="flex items-center justify-between rounded-xl border p-3"><Label htmlFor="station-active">Estación activa</Label><Switch id="station-active" checked={stationDraft.isActive} onCheckedChange={isActive => setStationDraft({ ...stationDraft, isActive })}/></div>}
        <fieldset className="grid min-w-0 gap-4 border-t pt-4" disabled={!station.isActive && !stationDraft.isActive}>
        <legend className="text-sm font-semibold">Comanda</legend>
        {!station.isActive && !stationDraft.isActive && <p className="text-sm text-muted-foreground">Activa la estación para cambiar su comanda.</p>}
        <div className="grid gap-2"><Label htmlFor="command-name">Nombre de la comanda</Label><Input id="command-name" maxLength={100} value={current.name} onChange={(event) => change({ name: event.target.value })}/></div>
        <div className="grid gap-2"><Label>Salida</Label><Select value={current.mode} onValueChange={(mode: RestaurantCommandConfiguration["mode"]) => change({ mode })}><SelectTrigger aria-label="Salida de comanda"><SelectValue/></SelectTrigger><SelectContent>{modes.data?.map(option => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></div>
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
        </fieldset>
        {saveError && <p role="alert" className="text-sm text-destructive">{saveError}</p>}
        <Button disabled={save.isPending || !stationDraft.name.trim() || !current.name.trim() || !validTarget || !catalogsReady || (physical && (!deviceId || !printerReady))} onClick={() => save.mutate(current)}>{saveStage === "station" ? "Guardando estación…" : saveStage === "printer" ? "Guardando impresora…" : save.isPending ? "Guardando comanda…" : "Guardar estación"}</Button>
      </fieldset>}
  </DialogContent></Dialog>;
}
