"use client";

import { useMemo, useState } from "react";
import { Building2, MapPin, Navigation, Phone, Plus } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { SiteLocationFields, mapsHref, type SiteLocationValue } from "@/components/parties/site-location-fields";
import { useAddPartySite, useCities, useCountries, useDivisions } from "@/hooks/use-parties";
import type { PartySiteDetail, PartyWorkspaceDetail } from "@/services/api/parties";
import { useAuthStore } from "@/stores/auth-store";

const emptyLocation: SiteLocationValue = { googleMapsUrl: "", googlePlaceId: "", latitude: "", longitude: "" };

export function PartySitesSection({ detail }: { detail: PartyWorkspaceDetail }) {
  const permissions = useAuthStore(state => new Set(state.user?.permissions ?? []));
  const [open, setOpen] = useState(false);
  const sites = useMemo(() => detail.sites?.length ? detail.sites : detail.primarySite ? [detail.primarySite] : [], [detail]);
  return <section className="rounded-2xl border p-5">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div><h3 className="font-semibold">Sedes y puntos de entrega</h3><p className="text-sm text-muted-foreground">Cada card representa una dirección concreta del tercero.</p></div>
      {detail.customer && permissions.has("parties.sites.manage") && <Button type="button" variant="outline" onClick={() => setOpen(true)}><Plus className="mr-2 h-4 w-4"/>Agregar sede</Button>}
    </div>
    {!sites.length ? <p className="mt-4 rounded-xl bg-muted/30 p-4 text-sm text-muted-foreground">No tiene sedes registradas.</p> : <div className="mt-4 grid gap-4 md:grid-cols-2">
      {sites.map(site => <SiteCard key={site.partySiteId} site={site}/>)}</div>}
    {open && detail.customer && <AddSiteDialog detail={detail} onClose={() => setOpen(false)}/>}
  </section>;
}

function SiteCard({ site }: { site: PartySiteDetail }) {
  const href = mapsHref({ googleMapsUrl: site.googleMapsUrl ?? "", latitude: site.latitude == null ? "" : String(site.latitude), longitude: site.longitude == null ? "" : String(site.longitude) });
  return <article className={`relative overflow-hidden rounded-2xl border p-4 shadow-sm ${site.isActive === false ? "opacity-60" : "bg-card"}`}>
    {site.isPrimary && <span className="absolute right-0 top-0 rounded-bl-xl bg-teal-600 px-3 py-1 text-xs font-semibold text-white">Principal</span>}
    <div className="flex items-start gap-3 pr-16"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-teal-50 text-teal-700"><Building2 className="h-5 w-5"/></span><div className="min-w-0"><h4 className="truncate font-semibold">{site.name}</h4><p className="text-xs text-muted-foreground">{site.code}</p></div></div>
    <div className="mt-4 space-y-2 text-sm"><p className="flex gap-2"><MapPin className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground"/><span>{site.addressLine}{site.neighborhood ? ` · ${site.neighborhood}` : ""}</span></p>{site.phone && <p className="flex gap-2"><Phone className="h-4 w-4 text-muted-foreground"/>{site.phone}</p>}</div>
    <div className="mt-4 flex flex-wrap items-center justify-between gap-2"><Badge variant={site.latitude != null && site.longitude != null ? "secondary" : "outline"}>{site.latitude != null && site.longitude != null ? "Ubicación verificada" : "Sin coordenadas"}</Badge>{href && <Button asChild size="sm" variant="ghost"><a href={href} target="_blank" rel="noreferrer"><Navigation className="mr-1 h-4 w-4"/>Abrir mapa</a></Button>}</div>
  </article>;
}

function AddSiteDialog({ detail, onClose }: { detail: PartyWorkspaceDetail; onClose: () => void }) {
  const mutation = useAddPartySite(detail.partyId);
  const countries = useCountries();
  const primary = detail.primarySite;
  const [country, setCountry] = useState(primary?.countryId ?? ""), [division, setDivision] = useState(primary?.administrativeDivisionId ?? ""), [city, setCity] = useState(primary?.cityId ?? "");
  const divisions = useDivisions(country), cities = useCities(division);
  const [form, setForm] = useState({ code: "", name: "", addressLine: "", neighborhood: "", phone: "" });
  const [location, setLocation] = useState(emptyLocation);
  const submit = async () => {
    if (!detail.customer || !country || !division || !city || !form.code.trim() || !form.name.trim() || !form.addressLine.trim()) return toast.error("Completa código, nombre, ciudad y dirección de la sede.");
    try {
      await mutation.mutateAsync({ customerId: detail.customer.customerId, request: { operationId: crypto.randomUUID(), site: { code: form.code.trim(), name: form.name.trim(), countryId: country, administrativeDivisionId: division, cityId: city, addressLine: form.addressLine.trim(), neighborhood: form.neighborhood.trim() || null, postalCode: null, email: null, phone: form.phone.trim() || null, isPrimary: false, googleMapsUrl: location.googleMapsUrl.trim() || null, googlePlaceId: location.googlePlaceId.trim() || null, latitude: location.latitude ? Number(location.latitude) : null, longitude: location.longitude ? Number(location.longitude) : null } } });
      toast.success("Sede agregada"); onClose();
    } catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible agregar la sede."); }
  };
  return <Dialog open onOpenChange={value => !value && onClose()}><DialogContent className="max-h-[92vh] max-w-3xl overflow-y-auto"><DialogHeader><DialogTitle>Nueva sede</DialogTitle><DialogDescription>Agrega una dirección concreta. La sede principal actual no cambia.</DialogDescription></DialogHeader><div className="grid gap-4 md:grid-cols-2">
    <Field label="Código"><Input value={form.code} onChange={event => setForm(current => ({ ...current, code: event.target.value.toUpperCase() }))} placeholder="NORTE"/></Field><Field label="Nombre"><Input value={form.name} onChange={event => setForm(current => ({ ...current, name: event.target.value }))} placeholder="Bodega norte"/></Field>
    <Field label="País"><Select value={country} onValueChange={value => { setCountry(value); setDivision(""); setCity(""); }}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{countries.data?.filter(item => item.isActive).map(item => <SelectItem key={item.countryId} value={item.countryId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Departamento"><Select value={division} onValueChange={value => { setDivision(value); setCity(""); }}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{divisions.data?.filter(item => item.isActive).map(item => <SelectItem key={item.administrativeDivisionId} value={item.administrativeDivisionId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Ciudad"><Select value={city} onValueChange={setCity}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{cities.data?.filter(item => item.isActive).map(item => <SelectItem key={item.cityId} value={item.cityId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Dirección"><Input value={form.addressLine} onChange={event => setForm(current => ({ ...current, addressLine: event.target.value }))}/></Field><Field label="Barrio"><Input value={form.neighborhood} onChange={event => setForm(current => ({ ...current, neighborhood: event.target.value }))}/></Field><Field label="Teléfono de la sede"><Input value={form.phone} onChange={event => setForm(current => ({ ...current, phone: event.target.value }))}/></Field>
    <SiteLocationFields value={location} onChange={setLocation}/>
  </div><DialogFooter><Button variant="outline" onClick={onClose}>Cancelar</Button><Button onClick={submit} disabled={mutation.isPending}>{mutation.isPending ? "Guardando…" : "Agregar sede"}</Button></DialogFooter></DialogContent></Dialog>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }
