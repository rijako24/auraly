"use client";

import { useMemo, useState } from "react";
import { Building2, MapPin, Navigation, Pencil, Phone, Plus, X } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { SiteLocationFields, mapsHref, type SiteLocationValue } from "@/components/parties/site-location-fields";
import { useAddPartySite, useCities, useCountries, useDivisions, useUpdatePartySite } from "@/hooks/use-parties";
import type { PartySiteDetail, PartyWorkspaceDetail } from "@/services/api/parties";
import { useAuthStore } from "@/stores/auth-store";

type EditorTarget = { kind: "new" } | { kind: "edit"; site: PartySiteDetail };

export function PartySitesSection({ detail, editing = false }: { detail: PartyWorkspaceDetail; editing?: boolean }) {
  const permissions = useAuthStore(state => new Set(state.user?.permissions ?? []));
  const [target, setTarget] = useState<EditorTarget | null>(null);
  const sites = useMemo(() => detail.sites?.length ? detail.sites : detail.primarySite ? [detail.primarySite] : [], [detail]);
  const canManage = editing && Boolean(detail.customer) && permissions.has("parties.sites.manage");

  return <section className="rounded-2xl border p-5">
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div><h3 className="font-semibold">Sedes y puntos de entrega</h3><p className="text-sm text-muted-foreground">Direcciones del cliente administradas desde esta misma ficha.</p></div>
      {canManage && !target && <Button type="button" variant="outline" onClick={() => setTarget({ kind: "new" })}><Plus className="mr-2 h-4 w-4"/>Agregar sede</Button>}
    </div>
    {!sites.length ? <p className="mt-4 rounded-xl bg-muted/30 p-4 text-sm text-muted-foreground">No tiene sedes registradas.</p> : <div className="mt-4 grid gap-4 md:grid-cols-2">{sites.map(site => <SiteCard key={site.partySiteId} site={site} canEdit={canManage && !target} onEdit={() => setTarget({ kind: "edit", site })}/>)}</div>}
    {target && detail.customer && <InlineSiteEditor key={target.kind === "edit" ? target.site.partySiteId : "new"} detail={detail} target={target} onClose={() => setTarget(null)}/>}
  </section>;
}

function SiteCard({ site, canEdit, onEdit }: { site: PartySiteDetail; canEdit: boolean; onEdit: () => void }) {
  const href = mapsHref({ googleMapsUrl: site.googleMapsUrl ?? "", latitude: site.latitude == null ? "" : String(site.latitude), longitude: site.longitude == null ? "" : String(site.longitude) });
  return <article className={`relative overflow-hidden rounded-2xl border p-4 shadow-sm ${site.isActive === false ? "opacity-60" : "bg-card"}`}>
    {site.isPrimary && <span className="absolute right-0 top-0 rounded-bl-xl bg-teal-600 px-3 py-1 text-xs font-semibold text-white">Principal</span>}
    <div className="flex items-start gap-3 pr-16"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-teal-50 text-teal-700"><Building2 className="h-5 w-5"/></span><div className="min-w-0"><h4 className="truncate font-semibold">{site.name}</h4><p className="text-xs text-muted-foreground">{site.code}</p></div></div>
    <div className="mt-4 space-y-2 text-sm"><p className="flex gap-2"><MapPin className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground"/><span>{site.addressLine}{site.neighborhood ? ` · ${site.neighborhood}` : ""}</span></p>{site.phone && <p className="flex gap-2"><Phone className="h-4 w-4 text-muted-foreground"/>{site.phone}</p>}</div>
    <div className="mt-4 flex flex-wrap items-center justify-between gap-2"><Badge variant={site.latitude != null && site.longitude != null ? "secondary" : "outline"}>{site.latitude != null && site.longitude != null ? "Ubicación verificada" : "Sin coordenadas"}</Badge><span className="flex gap-1">{href && <Button asChild size="sm" variant="ghost"><a href={href} target="_blank" rel="noreferrer"><Navigation className="mr-1 h-4 w-4"/>Mapa</a></Button>}{canEdit && <Button type="button" size="sm" variant="outline" onClick={onEdit}><Pencil className="mr-1 h-4 w-4"/>Editar</Button>}</span></div>
  </article>;
}

function InlineSiteEditor({ detail, target, onClose }: { detail: PartyWorkspaceDetail; target: EditorTarget; onClose: () => void }) {
  const site = target.kind === "edit" ? target.site : null;
  const add = useAddPartySite(detail.partyId), update = useUpdatePartySite(detail.partyId), countries = useCountries();
  const primary = site ?? detail.primarySite;
  const [country, setCountry] = useState(primary?.countryId ?? ""), [division, setDivision] = useState(primary?.administrativeDivisionId ?? ""), [city, setCity] = useState(primary?.cityId ?? "");
  const divisions = useDivisions(country), cities = useCities(division);
  const [isPrimary, setIsPrimary] = useState(site?.isPrimary ?? false);
  const [form, setForm] = useState({ code: site?.code ?? "", name: site?.name ?? "", addressLine: site?.addressLine ?? "", neighborhood: site?.neighborhood ?? "", postalCode: site?.postalCode ?? "", email: site?.email ?? "", phone: site?.phone ?? "" });
  const [location, setLocation] = useState<SiteLocationValue>({ googleMapsUrl: site?.googleMapsUrl ?? "", googlePlaceId: site?.googlePlaceId ?? "", latitude: site?.latitude == null ? "" : String(site.latitude), longitude: site?.longitude == null ? "" : String(site.longitude) });
  const pending = add.isPending || update.isPending;
  const submit = async () => {
    if (!detail.customer || !country || !division || !city || !form.code.trim() || !form.name.trim() || !form.addressLine.trim()) return toast.error("Completa código, nombre, ciudad y dirección de la sede.");
    const input = { code: form.code.trim(), name: form.name.trim(), countryId: country, administrativeDivisionId: division, cityId: city, addressLine: form.addressLine.trim(), neighborhood: form.neighborhood.trim() || null, postalCode: form.postalCode.trim() || null, email: form.email.trim() || null, phone: form.phone.trim() || null, isPrimary, googleMapsUrl: location.googleMapsUrl.trim() || null, googlePlaceId: location.googlePlaceId.trim() || null, latitude: location.latitude ? Number(location.latitude) : null, longitude: location.longitude ? Number(location.longitude) : null };
    try {
      if (site) await update.mutateAsync({ customerId: detail.customer.customerId, siteId: site.partySiteId, request: { rowVersion: site.rowVersion, site: input } });
      else await add.mutateAsync({ customerId: detail.customer.customerId, request: { operationId: crypto.randomUUID(), site: input } });
      toast.success(site ? "Sede actualizada" : "Sede agregada"); onClose();
    } catch (error) { toast.error(error instanceof Error ? error.message : "No fue posible guardar la sede."); }
  };
  return <div className="mt-5 rounded-2xl border-2 border-teal-200 bg-teal-50/30 p-5">
    <div className="mb-4 flex items-start justify-between gap-3"><div><h4 className="font-semibold">{site ? `Editar ${site.name}` : "Agregar sede"}</h4><p className="text-sm text-muted-foreground">Completa la sede sin salir del formulario del tercero.</p></div><Button type="button" size="icon" variant="ghost" onClick={onClose} aria-label="Cerrar editor de sede"><X className="h-4 w-4"/></Button></div>
    <div className="grid gap-4 md:grid-cols-2">
      <Field label="Código"><Input value={form.code} onChange={event => setForm(current => ({ ...current, code: event.target.value.toUpperCase() }))}/></Field><Field label="Nombre"><Input value={form.name} onChange={event => setForm(current => ({ ...current, name: event.target.value }))}/></Field>
      <Field label="País"><Select value={country} onValueChange={value => { setCountry(value); setDivision(""); setCity(""); }}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{countries.data?.filter(item => item.isActive).map(item => <SelectItem key={item.countryId} value={item.countryId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Departamento"><Select value={division} onValueChange={value => { setDivision(value); setCity(""); }}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{divisions.data?.filter(item => item.isActive).map(item => <SelectItem key={item.administrativeDivisionId} value={item.administrativeDivisionId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Ciudad"><Select value={city} onValueChange={setCity}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{cities.data?.filter(item => item.isActive).map(item => <SelectItem key={item.cityId} value={item.cityId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
      <Field label="Dirección"><Input value={form.addressLine} onChange={event => setForm(current => ({ ...current, addressLine: event.target.value }))}/></Field><Field label="Barrio"><Input value={form.neighborhood} onChange={event => setForm(current => ({ ...current, neighborhood: event.target.value }))}/></Field><Field label="Código postal"><Input value={form.postalCode} onChange={event => setForm(current => ({ ...current, postalCode: event.target.value }))}/></Field><Field label="Correo de la sede"><Input value={form.email} onChange={event => setForm(current => ({ ...current, email: event.target.value }))}/></Field><Field label="Teléfono"><Input value={form.phone} onChange={event => setForm(current => ({ ...current, phone: event.target.value }))}/></Field>
      <label className="flex items-center justify-between rounded-xl border bg-background p-4 md:col-span-2"><span><b className="block text-sm">Sede principal</b><small className="text-muted-foreground">La nueva dirección principal reemplaza la anterior.</small></span><Switch checked={isPrimary} onCheckedChange={setIsPrimary}/></label><SiteLocationFields value={location} onChange={setLocation}/>
    </div>
    <div className="mt-5 flex justify-end gap-2"><Button type="button" variant="outline" onClick={onClose}>Cancelar</Button><Button type="button" onClick={submit} disabled={pending}>{pending ? "Guardando…" : site ? "Guardar sede" : "Agregar sede"}</Button></div>
  </div>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) { return <div className="space-y-2"><Label>{label}</Label>{children}</div>; }
