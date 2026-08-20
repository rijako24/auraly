"use client";

import { useMemo, useRef, useState } from "react";
import { Building2, Check, LocateFixed, MapPinOff, Navigation, Search, SkipForward, ZoomIn, ZoomOut } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export type RouteLocationStop = {
  routeStopId: string;
  sequence: number;
  customerName: string;
  siteName: string;
  addressLine: string;
  cityName: string;
  googleMapsUrl: string | null;
  latitude: number | null;
  longitude: number | null;
};

type StopStatus = "pending" | "visited" | "skipped";

export function RouteLocationMap<T extends RouteLocationStop>({
  stops,
  onOpen,
  statusOf = () => "pending",
  className = "",
}: {
  stops: T[];
  onOpen: (stop: T) => void;
  statusOf?: (stop: T) => StopStatus;
  className?: string;
}) {
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [zoom, setZoom] = useState(1);
  const [pan,setPan]=useState({x:0,y:0});
  const drag=useRef<{x:number;y:number;originX:number;originY:number}|null>(null);
  const normalized = search.trim().toLocaleLowerCase("es");
  const visible = useMemo(
    () => stops.filter((stop) => !normalized || `${stop.customerName} ${stop.siteName} ${stop.addressLine} ${stop.cityName}`.toLocaleLowerCase("es").includes(normalized)),
    [normalized, stops],
  );
  const located = visible.filter(hasCoordinates);
  const unlocated = visible.filter((stop) => !hasCoordinates(stop));
  const selected = visible.find((stop) => stop.routeStopId === selectedId) ?? null;
  const bounds = mapBounds(located, zoom);

  return <div className={`grid min-h-0 gap-4 xl:grid-cols-[minmax(0,1.55fr)_minmax(19rem,.75fr)] ${className}`}>
    <section className="relative min-h-[32rem] touch-none overflow-hidden rounded-[2rem] border bg-slate-100 shadow-sm active:cursor-grabbing" onPointerDown={event=>{if((event.target as HTMLElement).closest("button,input,a"))return;drag.current={x:event.clientX,y:event.clientY,originX:pan.x,originY:pan.y};event.currentTarget.setPointerCapture(event.pointerId)}} onPointerMove={event=>{if(!drag.current)return;setPan({x:Math.max(-260,Math.min(260,drag.current.originX+event.clientX-drag.current.x)),y:Math.max(-220,Math.min(220,drag.current.originY+event.clientY-drag.current.y))})}} onPointerUp={()=>{drag.current=null}} onPointerCancel={()=>{drag.current=null}}>
      <div className="absolute inset-0 transition-transform duration-75" style={{transform:`translate3d(${pan.x}px,${pan.y}px,0)`}}>
      {bounds ? <iframe
        title="Mapa de establecimientos de la ruta"
        className="pointer-events-none absolute inset-0 h-full w-full border-0 grayscale-[8%] contrast-[.96]"
        loading="lazy"
        src={`https://www.openstreetmap.org/export/embed.html?bbox=${bounds.west}%2C${bounds.south}%2C${bounds.east}%2C${bounds.north}&layer=mapnik`}
      /> : <div className="absolute inset-0 grid place-items-center bg-[radial-gradient(circle_at_20%_20%,#ccfbf1,transparent_34%),linear-gradient(135deg,#f8fafc,#e2e8f0)] p-8 text-center"><div><MapPinOff className="mx-auto h-12 w-12 text-slate-500"/><h3 className="mt-3 text-lg font-bold">Sin ubicaciones verificadas</h3><p className="mt-1 max-w-sm text-sm text-muted-foreground">Captura la ubicación de las sedes para verlas en el mapa. Nunca se dibujan coordenadas inventadas.</p></div></div>}
      {bounds && located.map((stop) => {
        const position = pointPosition(stop, bounds), status = statusOf(stop), active = selectedId === stop.routeStopId;
        return <button key={stop.routeStopId} type="button" aria-label={`${stop.sequence}. ${stop.customerName}, ${statusLabel(status)}`} onPointerDown={event=>event.stopPropagation()} onClick={()=>setSelectedId(stop.routeStopId)} className={`group absolute -translate-x-1/2 -translate-y-full transition hover:z-20 hover:scale-110 focus:z-20 focus:outline-none ${active?"z-20 scale-110":"z-10"}`} style={{left:`${position.x}%`,top:`${position.y}%`}}><span className={`relative grid h-12 w-12 place-items-center rounded-2xl border-[3px] border-white text-white shadow-xl ${statusClass(status)}`}><Building2 className="h-5 w-5"/><span className="absolute -right-2 -top-2 grid h-6 min-w-6 place-items-center rounded-full bg-slate-950 px-1 text-[11px] font-black text-white ring-2 ring-white">{stop.sequence}</span></span><span className="mx-auto block h-3 w-3 -translate-y-2 rotate-45 border-b-[3px] border-r-[3px] border-white bg-inherit shadow-sm"/></button>;
      })}</div>
      <div className="absolute left-4 right-4 top-4 flex items-start justify-between gap-3">
        <div className="relative w-full max-w-sm rounded-xl bg-white/95 shadow-lg backdrop-blur"><Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input value={search} onChange={(event) => setSearch(event.target.value)} className="h-11 border-white bg-transparent pl-9" placeholder="Buscar un cliente o sede"/></div>
        <div className="flex rounded-xl bg-white/95 p-1 shadow-lg backdrop-blur"><Button size="icon" variant="ghost" aria-label="Centrar mapa" onClick={()=>setPan({x:0,y:0})}><LocateFixed className="h-4 w-4"/></Button><Button size="icon" variant="ghost" aria-label="Alejar mapa" disabled={zoom <= .45} onClick={() => setZoom((value) => Math.max(.45, value / 1.5))}><ZoomOut className="h-4 w-4"/></Button><Button size="icon" variant="ghost" aria-label="Acercar mapa" disabled={zoom >= 4} onClick={() => setZoom((value) => Math.min(4, value * 1.5))}><ZoomIn className="h-4 w-4"/></Button></div>
      </div>
      <div className="absolute bottom-4 left-4 flex flex-wrap gap-2 rounded-xl bg-white/95 p-2 text-xs shadow-lg backdrop-blur"><Legend color="bg-slate-950" label="Pendiente"/><Legend color="bg-emerald-500" label="Visitado"/><Legend color="bg-amber-500" label="Omitido"/></div>
    </section>
    <aside className="min-h-0 space-y-3 overflow-y-auto pr-1 xl:max-h-[38rem]">
      {selected && <div className="rounded-3xl border border-teal-200 bg-gradient-to-br from-teal-50 to-white p-5 shadow-sm"><div className="flex items-start justify-between gap-3"><span className={`grid h-12 w-12 shrink-0 place-items-center rounded-2xl text-white ${statusClass(statusOf(selected))}`}><Building2 className="h-5 w-5"/></span><Badge variant="outline">Parada {selected.sequence}</Badge></div><h3 className="mt-4 text-lg font-black">{selected.customerName}</h3><p className="text-sm font-medium text-teal-800">{selected.siteName}</p><p className="mt-2 text-sm text-muted-foreground">{selected.addressLine} · {selected.cityName}</p><div className="mt-4 grid grid-cols-2 gap-2"><Button onClick={() => onOpen(selected)}>Abrir</Button><Button asChild variant="outline"><a href={directionsHref(selected)} target="_blank" rel="noreferrer"><Navigation className="mr-2 h-4 w-4"/>Llegar</a></Button></div></div>}
      <div className="space-y-2">{visible.map((stop) => { const status = statusOf(stop); return <button key={stop.routeStopId} type="button" onClick={() => hasCoordinates(stop) ? setSelectedId(stop.routeStopId) : onOpen(stop)} className={`flex w-full items-center gap-3 rounded-2xl border bg-card p-3 text-left transition hover:border-teal-300 hover:shadow-sm ${selectedId === stop.routeStopId ? "border-teal-400 ring-2 ring-teal-100" : ""}`}><span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl text-white ${statusClass(status)}`}>{status === "visited" ? <Check className="h-4 w-4"/> : status === "skipped" ? <SkipForward className="h-4 w-4"/> : <Building2 className="h-4 w-4"/>}</span><span className="min-w-0 flex-1"><strong className="block truncate">{stop.sequence}. {stop.customerName}</strong><small className="block truncate text-muted-foreground">{stop.siteName} · {stop.addressLine}</small></span>{!hasCoordinates(stop) && <Badge variant="outline" className="text-amber-700">Sin ubicación</Badge>}</button>})}</div>
      {!visible.length && <div className="rounded-2xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay clientes que coincidan con la búsqueda.</div>}
      {!!unlocated.length && <p className="flex items-center gap-2 rounded-xl bg-amber-50 p-3 text-xs text-amber-900"><LocateFixed className="h-4 w-4 shrink-0"/>{unlocated.length} sede{unlocated.length === 1 ? "" : "s"} pendiente{unlocated.length === 1 ? "" : "s"} de ubicación exacta.</p>}
    </aside>
  </div>;
}

function hasCoordinates<T extends RouteLocationStop>(stop: T): stop is T & { latitude: number; longitude: number } {
  return Number.isFinite(stop.latitude) && Number.isFinite(stop.longitude) && Math.abs(stop.latitude!) <= 90 && Math.abs(stop.longitude!) <= 180;
}

type Bounds = { west: number; east: number; south: number; north: number; mercatorNorth: number; mercatorSouth: number };
function mapBounds(stops: Array<RouteLocationStop & { latitude: number; longitude: number }>, zoom: number): Bounds | null {
  if (!stops.length) return null;
  const lats = stops.map((stop) => stop.latitude), lons = stops.map((stop) => stop.longitude);
  const centerLat = (Math.min(...lats) + Math.max(...lats)) / 2, centerLon = (Math.min(...lons) + Math.max(...lons)) / 2;
  const latSpan = Math.max(.012, (Math.max(...lats) - Math.min(...lats)) * 1.35) / zoom;
  const lonSpan = Math.max(.012, (Math.max(...lons) - Math.min(...lons)) * 1.35) / zoom;
  const south = Math.max(-85, centerLat - latSpan / 2), north = Math.min(85, centerLat + latSpan / 2);
  return { west: centerLon - lonSpan / 2, east: centerLon + lonSpan / 2, south, north, mercatorNorth: mercatorY(north), mercatorSouth: mercatorY(south) };
}
function mercatorY(latitude: number) { const radians = latitude * Math.PI / 180; return Math.log(Math.tan(Math.PI / 4 + radians / 2)); }
function pointPosition(stop: RouteLocationStop & { latitude: number; longitude: number }, bounds: Bounds) { return { x: Math.max(2, Math.min(98, ((stop.longitude - bounds.west) / (bounds.east - bounds.west)) * 100)), y: Math.max(4, Math.min(96, ((bounds.mercatorNorth - mercatorY(stop.latitude)) / (bounds.mercatorNorth - bounds.mercatorSouth)) * 100)) }; }
function statusClass(status: StopStatus) { return status === "visited" ? "bg-emerald-500" : status === "skipped" ? "bg-amber-500" : "bg-slate-950"; }
function statusLabel(status: StopStatus) { return status === "visited" ? "visitado" : status === "skipped" ? "omitido" : "pendiente"; }
function directionsHref(stop: RouteLocationStop) { return stop.googleMapsUrl || (hasCoordinates(stop) ? `https://www.google.com/maps/dir/?api=1&destination=${stop.latitude},${stop.longitude}` : `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${stop.addressLine}, ${stop.cityName}`)}`); }
function Legend({ color, label }: { color: string; label: string }) { return <span className="flex items-center gap-1.5"><i className={`h-2.5 w-2.5 rounded-sm ${color}`}/>{label}</span>; }
