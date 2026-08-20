"use client";

import { useState } from "react";
import { Loader2, MapPin, Search, UserPlus } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { routesApi, type RouteCandidateSite, type SalesRouteStop } from "@/services/api/routes";

export function OutsideRouteCustomerDialog({ routeId, onClose, onSelect }: { routeId: string | null; onClose: () => void; onSelect: (stop: SalesRouteStop) => void }) {
  const [query, setQuery] = useState("");
  const [items, setItems] = useState<RouteCandidateSite[]>([]);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);
  const search = async () => {
    const term = query.trim();
    if (term.length < 2) { toast.info("Escribe el nombre, documento o dirección del cliente."); return; }
    if (!navigator.onLine) { toast.info("Conéctate para buscar un cliente fuera de la ruta."); return; }
    setLoading(true); setSearched(true);
    try {
      const page = routeId
        ? await routesApi.candidates(routeId, { page: 1, pageSize: 50, search: term })
        : await routesApi.customerSites({ page: 1, pageSize: 50, search: term });
      setItems(page.items.filter((item) => !item.isAlreadyInRoute));
    } catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible buscar clientes."); }
    finally { setLoading(false); }
  };
  return <Dialog open onOpenChange={(open) => !open && onClose()}><DialogContent className="flex max-h-[92dvh] w-[calc(100%-1.5rem)] flex-col overflow-hidden rounded-[2rem] p-0 sm:max-w-xl"><DialogHeader className="border-b p-5 text-left"><DialogTitle>Pedido fuera de ruta</DialogTitle><DialogDescription>{routeId ? "Busca cualquier cliente que no esté programado en este recorrido." : "Busca cualquier cliente para tomar un pedido sin ruta asignada."} El pedido no cerrará ninguna visita.</DialogDescription></DialogHeader><div className="min-h-0 flex-1 overflow-y-auto p-4"><form className="flex gap-2" onSubmit={(event) => { event.preventDefault(); void search(); }}><div className="relative min-w-0 flex-1"><Search className="absolute left-3 top-3.5 h-5 w-5 text-teal-700"/><Input autoFocus className="h-12 rounded-2xl pl-10 text-base" value={query} onChange={(event) => { setQuery(event.target.value); setSearched(false); }} placeholder="Cliente, documento o dirección"/></div><Button className="h-12 rounded-2xl bg-slate-950" disabled={loading}>{loading ? <Loader2 className="h-5 w-5 animate-spin"/> : "Buscar"}</Button></form>{!searched && <div className="py-14 text-center"><UserPlus className="mx-auto h-10 w-10 text-teal-600"/><p className="mt-3 text-sm text-muted-foreground">El catálogo y precios se cargarán para el cliente elegido.</p></div>}{searched && !loading && !items.length && <p className="py-14 text-center text-sm text-muted-foreground">No encontramos clientes fuera de este recorrido.</p>}<div className="mt-4 space-y-2">{items.map((item) => <button type="button" key={item.partySiteId} onClick={() => onSelect(toStop(item))} className="flex w-full items-center gap-3 rounded-2xl border p-4 text-left transition hover:border-teal-300"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-teal-50 text-teal-700"><MapPin className="h-5 w-5"/></span><span className="min-w-0"><strong className="block truncate">{item.customerName}</strong><small className="block truncate text-muted-foreground">{item.siteName} · {item.addressLine}, {item.cityName}</small>{item.hasScheduleConflict && <small className="text-amber-700">Tiene otra programación activa</small>}</span></button>)}</div></div></DialogContent></Dialog>;
}

function toStop(item: RouteCandidateSite): SalesRouteStop { return { routeStopId: `outside-${item.partySiteId}`, customerId: item.customerId, partySiteId: item.partySiteId, sequence: 0, customerName: item.customerName, identification: item.identification, siteName: item.siteName, addressLine: item.addressLine, neighborhood: item.neighborhood, cityName: item.cityName, phone: item.phone, googleMapsUrl: item.googleMapsUrl, latitude: item.latitude, longitude: item.longitude, plannedVisitTime: null, visitNote: null, rowVersion: "" }; }
