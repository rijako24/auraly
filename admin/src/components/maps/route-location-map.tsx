"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Building2, Check, LocateFixed, MapPinOff, Navigation, Search, SkipForward } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export type RouteLocationStop = { routeStopId:string;sequence:number;customerName:string;siteName:string;addressLine:string;cityName:string;googleMapsUrl:string|null;latitude:number|null;longitude:number|null };
type StopStatus="pending"|"visited"|"skipped";
type LeafletMap={fitBounds:(bounds:Array<[number,number]>,options?:object)=>void;invalidateSize:()=>void;remove:()=>void};
type LeafletGroup={addTo:(map:LeafletMap)=>LeafletGroup;clearLayers:()=>void};
type LeafletApi={map:(element:HTMLElement,options:object)=>LeafletMap;tileLayer:(url:string,options:object)=>{addTo:(map:LeafletMap)=>void};layerGroup:()=>LeafletGroup;divIcon:(options:object)=>object;marker:(position:[number,number],options:object)=>{addTo:(group:LeafletGroup)=>{on:(event:string,action:()=>void)=>void}}};
declare global{interface Window{L?:LeafletApi;__auralyLeaflet?:Promise<LeafletApi>}}

function loadLeaflet(){
  if(window.L)return Promise.resolve(window.L);
  window.__auralyLeaflet??=new Promise<LeafletApi>((resolve,reject)=>{
    if(!document.querySelector('link[data-auraly-leaflet]')){const style=document.createElement("link");style.rel="stylesheet";style.href="/vendor/leaflet/leaflet.css";style.dataset.auralyLeaflet="true";document.head.appendChild(style)}
    const existing=document.querySelector<HTMLScriptElement>('script[data-auraly-leaflet]'),script=existing??document.createElement("script");
    script.addEventListener("load",()=>window.L?resolve(window.L):reject(new Error("No fue posible iniciar el mapa.")),{once:true});script.addEventListener("error",()=>reject(new Error("No fue posible cargar el mapa.")),{once:true});
    if(!existing){script.src="/vendor/leaflet/leaflet.js";script.dataset.auralyLeaflet="true";document.head.appendChild(script)}
  });return window.__auralyLeaflet;
}

export function RouteLocationMap<T extends RouteLocationStop>({stops,onOpen,statusOf=()=>"pending",className=""}:{stops:T[];onOpen:(stop:T)=>void;statusOf?:(stop:T)=>StopStatus;className?:string}){
  const [search,setSearch]=useState(""),[selectedId,setSelectedId]=useState<string|null>(null);const normalized=search.trim().toLocaleLowerCase("es");
  const visible=useMemo(()=>stops.filter(stop=>!normalized||`${stop.customerName} ${stop.siteName} ${stop.addressLine} ${stop.cityName}`.toLocaleLowerCase("es").includes(normalized)),[normalized,stops]);
  const located=useMemo(()=>visible.filter(hasCoordinates),[visible]),selected=visible.find(stop=>stop.routeStopId===selectedId)??null;
  return <div className={`grid min-h-0 gap-4 xl:grid-cols-[minmax(0,1.55fr)_minmax(19rem,.75fr)] ${className}`}><InteractiveMap stops={located} statusOf={statusOf} onSelect={setSelectedId} search={search} setSearch={setSearch}/><aside className="min-h-0 space-y-3 overflow-y-auto pr-1 xl:max-h-[38rem]">
    {selected&&<div className="rounded-3xl border border-teal-200 bg-gradient-to-br from-teal-50 to-white p-5 shadow-sm"><div className="flex items-start justify-between gap-3"><span className={`grid h-12 w-12 shrink-0 place-items-center rounded-2xl text-white ${statusClass(statusOf(selected))}`}><Building2 className="h-5 w-5"/></span><Badge variant="outline">Parada {selected.sequence}</Badge></div><h3 className="mt-4 text-lg font-black">{selected.customerName}</h3><p className="text-sm font-medium text-teal-800">{selected.siteName}</p><p className="mt-2 text-sm text-muted-foreground">{selected.addressLine} · {selected.cityName}</p><div className="mt-4 grid grid-cols-2 gap-2"><Button onClick={()=>onOpen(selected)}>Abrir</Button><Button asChild variant="outline"><a href={directionsHref(selected)} target="_blank" rel="noreferrer"><Navigation className="mr-2 h-4 w-4"/>Llegar</a></Button></div></div>}
    <div className="space-y-2">{visible.map(stop=>{const status=statusOf(stop);return <button key={stop.routeStopId} type="button" onClick={()=>hasCoordinates(stop)?setSelectedId(stop.routeStopId):onOpen(stop)} className={`flex w-full items-center gap-3 rounded-2xl border bg-card p-3 text-left transition hover:border-teal-300 hover:shadow-sm ${selectedId===stop.routeStopId?"border-teal-400 ring-2 ring-teal-100":""}`}><span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl text-white ${statusClass(status)}`}>{status==="visited"?<Check className="h-4 w-4"/>:status==="skipped"?<SkipForward className="h-4 w-4"/>:<Building2 className="h-4 w-4"/>}</span><span className="min-w-0 flex-1"><strong className="block truncate">{stop.sequence}. {stop.customerName}</strong><small className="block truncate text-muted-foreground">{stop.siteName} · {stop.addressLine}</small></span>{!hasCoordinates(stop)&&<Badge variant="outline" className="text-amber-700">Sin ubicación</Badge>}</button>})}</div>
    {!visible.length&&<div className="rounded-2xl border border-dashed p-8 text-center text-sm text-muted-foreground">No hay clientes que coincidan con la búsqueda.</div>}{visible.some(stop=>!hasCoordinates(stop))&&<p className="flex items-center gap-2 rounded-xl bg-amber-50 p-3 text-xs text-amber-900"><LocateFixed className="h-4 w-4 shrink-0"/>Las sedes sin coordenadas siguen disponibles por dirección.</p>}
  </aside></div>;
}

function InteractiveMap<T extends RouteLocationStop>({stops,statusOf,onSelect,search,setSearch}:{stops:Array<T&{latitude:number;longitude:number}>;statusOf:(stop:T)=>StopStatus;onSelect:(id:string)=>void;search:string;setSearch:(value:string)=>void}){
  const element=useRef<HTMLDivElement>(null),map=useRef<LeafletMap|null>(null),group=useRef<LeafletGroup|null>(null);const [ready,setReady]=useState(false);
  const fit=()=>{if(map.current&&stops.length)map.current.fitBounds(stops.map(stop=>[stop.latitude,stop.longitude]),{padding:[42,42],maxZoom:16})};
  useEffect(()=>{let active=true;void loadLeaflet().then(L=>{if(!active||!element.current||map.current)return;const value=L.map(element.current,{zoomControl:true,attributionControl:true});L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",{maxZoom:19,attribution:"© OpenStreetMap"}).addTo(value);map.current=value;group.current=L.layerGroup().addTo(value);setReady(true);window.setTimeout(()=>value.invalidateSize(),0)}).catch(()=>setReady(false));return()=>{active=false;map.current?.remove();map.current=null;group.current=null}},[]);
  useEffect(()=>{if(!ready||!group.current)return;let active=true;void loadLeaflet().then(L=>{if(!active||!group.current)return;group.current.clearLayers();for(const stop of stops){const status=statusOf(stop),icon=L.divIcon({className:"auraly-map-marker",html:`<span class="auraly-map-marker__pin auraly-map-marker__pin--${status}"><b>${stop.sequence}</b></span>`,iconSize:[42,48],iconAnchor:[21,46]});L.marker([stop.latitude,stop.longitude],{icon,keyboard:true,title:`${stop.sequence}. ${stop.customerName}`}).addTo(group.current).on("click",()=>onSelect(stop.routeStopId))}fit()});return()=>{active=false}},[ready,stops,statusOf,onSelect]);
  return <section className="relative min-h-[28rem] overflow-hidden rounded-[2rem] border bg-slate-100 shadow-sm"><div ref={element} className="absolute inset-0 z-0"/>{!stops.length&&<div className="absolute inset-0 z-10 grid place-items-center bg-slate-50 p-8 text-center"><div><MapPinOff className="mx-auto h-12 w-12 text-slate-500"/><h3 className="mt-3 text-lg font-bold">Sin ubicaciones verificadas</h3><p className="mt-1 max-w-sm text-sm text-muted-foreground">Configura las coordenadas de las sedes para ver el recorrido completo.</p></div></div>}<div className="absolute left-4 right-4 top-4 z-[500] flex items-start justify-between gap-2"><label className="relative min-w-0 flex-1 rounded-xl bg-white/95 shadow-lg"><Search className="pointer-events-none absolute left-3 top-3.5 h-4 w-4 text-muted-foreground"/><Input value={search} onChange={event=>setSearch(event.target.value)} className="h-11 border-white bg-transparent pl-9" placeholder="Buscar cliente o sede"/></label><Button size="icon" variant="secondary" className="h-11 w-11 shrink-0 bg-white shadow-lg" onClick={fit} aria-label="Ver todas las paradas"><LocateFixed className="h-4 w-4"/></Button></div><div className="absolute bottom-4 left-4 z-[500] flex flex-wrap gap-2 rounded-xl bg-white/95 p-2 text-xs shadow-lg"><Legend color="bg-slate-950" label="Pendiente"/><Legend color="bg-emerald-500" label="Visitado"/><Legend color="bg-amber-500" label="Omitido"/></div></section>;
}

function hasCoordinates<T extends RouteLocationStop>(stop:T):stop is T&{latitude:number;longitude:number}{return Number.isFinite(stop.latitude)&&Number.isFinite(stop.longitude)&&Math.abs(stop.latitude!)<=90&&Math.abs(stop.longitude!)<=180}
function statusClass(status:StopStatus){return status==="visited"?"bg-emerald-500":status==="skipped"?"bg-amber-500":"bg-slate-950"}
function directionsHref(stop:RouteLocationStop){return stop.googleMapsUrl||(hasCoordinates(stop)?`https://www.google.com/maps/dir/?api=1&destination=${stop.latitude},${stop.longitude}`:`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${stop.addressLine}, ${stop.cityName}`)}`)}
function Legend({color,label}:{color:string;label:string}){return <span className="flex items-center gap-1.5"><i className={`h-2.5 w-2.5 rounded-sm ${color}`}/>{label}</span>}
