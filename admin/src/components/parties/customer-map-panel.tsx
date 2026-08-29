"use client";

import { useMemo, useState } from "react";
import { Building2, MapPinned, Route, UserRound } from "lucide-react";
import { RouteLocationMap, type RouteLocationStop } from "@/components/maps/route-location-map";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { Card, CardContent } from "@/components/ui/card";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useCustomerMap } from "@/hooks/use-parties";
import type { CustomerMapSite } from "@/services/api/parties";

type CustomerMarker = RouteLocationStop & { partyId: string; customerId: string; assignments: CustomerMapSite["assignments"] };

export function CustomerMapPanel({ onOpenCustomer }: { onOpenCustomer: (partyId: string) => void }) {
  const map = useCustomerMap();
  const [sellerId, setSellerId] = useState("all"), [routeId, setRouteId] = useState("all"), [assignment, setAssignment] = useState("all");
  const routes = useMemo(() => unique(map.data?.flatMap((site) => site.assignments.filter((value) => sellerId === "all" || value.sellerId === sellerId).map((value) => ({ id: value.routeId, name: value.routeName }))) ?? []), [map.data, sellerId]);
  const filtered = (map.data ?? []).filter((site) => {
    if (assignment === "assigned" && !site.assignments.length) return false;
    if (assignment === "unassigned" && site.assignments.length) return false;
    if (sellerId !== "all" && !site.assignments.some((value) => value.sellerId === sellerId)) return false;
    if (routeId !== "all" && !site.assignments.some((value) => value.routeId === routeId)) return false;
    return true;
  });
  const markers: CustomerMarker[] = filtered.map((site, index) => ({
    routeStopId: site.partySiteId, sequence: index + 1, partyId: site.partyId, customerId: site.customerId,
    customerName: site.customerName, siteName: site.siteName, addressLine: site.addressLine, cityName: site.cityName,
    googleMapsUrl: site.googleMapsUrl, latitude: site.latitude, longitude: site.longitude, assignments: site.assignments,
  }));
  const located = filtered.filter((site) => site.latitude != null && site.longitude != null).length;
  const unassigned = filtered.filter((site) => !site.assignments.length).length;

  return <div className="space-y-4">
    <div className="grid gap-3 md:grid-cols-3"><Metric icon={Building2} label="Sedes visibles" value={String(filtered.length)}/><Metric icon={MapPinned} label="Ubicadas" value={String(located)}/><Metric icon={Route} label="Sin ruta" value={String(unassigned)}/></div>
    <div className="grid gap-3 rounded-2xl border bg-card p-4 md:grid-cols-3">
      <PartyRoleSelect role="Seller" value={sellerId} leadingOptions={[{value:"all",label:"Todos los vendedores"}]} placeholder="Buscar vendedor" onChange={(value) => { setSellerId(value); setRouteId("all"); }}/>
      <Select value={routeId} onValueChange={setRouteId}><SelectTrigger><SelectValue placeholder="Ruta"/></SelectTrigger><SelectContent><SelectItem value="all">Todas las rutas</SelectItem>{routes.map((item) => <SelectItem key={item.id} value={item.id}>{item.name}</SelectItem>)}</SelectContent></Select>
      <Select value={assignment} onValueChange={setAssignment}><SelectTrigger><SelectValue placeholder="Asignación"/></SelectTrigger><SelectContent><SelectItem value="all">Con y sin ruta</SelectItem><SelectItem value="assigned">Con ruta</SelectItem><SelectItem value="unassigned">Sin ruta</SelectItem></SelectContent></Select>
    </div>
    {map.isLoading ? <div className="grid min-h-[32rem] place-items-center rounded-[2rem] border text-muted-foreground">Preparando el mapa comercial…</div> : map.isError ? <div className="rounded-2xl border border-destructive/30 bg-destructive/5 p-5 text-destructive">No fue posible cargar el mapa de clientes.</div> : <RouteLocationMap stops={markers} onOpen={(stop) => onOpenCustomer(stop.partyId)}/>}
  </div>;
}

function unique(values: Array<{ id: string; name: string }>) { return [...new Map(values.map((value) => [value.id, value])).values()].sort((a, b) => a.name.localeCompare(b.name, "es")); }
function Metric({ icon: Icon, label, value }: { icon: typeof UserRound; label: string; value: string }) { return <Card><CardContent className="flex items-center gap-3 p-4"><span className="rounded-xl bg-teal-50 p-2 text-teal-700"><Icon className="h-5 w-5"/></span><div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div></CardContent></Card>; }
