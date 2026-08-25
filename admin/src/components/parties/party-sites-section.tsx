"use client";

import { useMemo, useState } from "react";
import { Building2, MapPin, Navigation, Pencil, Phone, Plus, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { SiteLocationFields, mapsHref, type SiteLocationValue } from "@/components/parties/site-location-fields";
import { useCities, useCountries, useDivisions } from "@/hooks/use-parties";
import type { PartySiteDetail, PartySiteInput, PartyWorkspaceDetail } from "@/services/api/parties";
import { useAuthStore } from "@/stores/auth-store";

export type PartySiteDraft = PartySiteInput & { clientKey: string; partySiteId?: string; rowVersion?: string };
export const partySiteDraft = (site: PartySiteDetail): PartySiteDraft => ({
  clientKey: site.partySiteId, partySiteId: site.partySiteId, rowVersion: site.rowVersion,
  code: site.code, name: site.name, countryId: site.countryId, administrativeDivisionId: site.administrativeDivisionId,
  cityId: site.cityId, addressLine: site.addressLine, neighborhood: site.neighborhood, postalCode: site.postalCode,
  email: site.email, phone: site.phone, isPrimary: site.isPrimary, googleMapsUrl: site.googleMapsUrl,
  googlePlaceId: site.googlePlaceId, latitude: site.latitude, longitude: site.longitude,
});

export function PartySitesSection({ detail, editing = false, drafts, onChange }: { detail: PartyWorkspaceDetail; editing?: boolean; drafts?: PartySiteDraft[]; onChange?: (sites: PartySiteDraft[]) => void }) {
  const permissions = useAuthStore(state => new Set(state.user?.permissions ?? []));
  const [target, setTarget] = useState<PartySiteDraft | null>(null);
  const stored = useMemo(() => detail.sites?.length ? detail.sites : detail.primarySite ? [detail.primarySite] : [], [detail]);
  const sites = drafts ?? stored.map(partySiteDraft);
  const canManage = editing && Boolean(detail.customer) && permissions.has("parties.sites.manage") && Boolean(onChange);
  const add = () => { const base = sites.find(site => site.isPrimary) ?? sites[0]; setTarget({ clientKey: crypto.randomUUID(), code: `SEDE-${sites.length + 1}`, name: `Sede ${sites.length + 1}`, countryId: base?.countryId ?? "", administrativeDivisionId: base?.administrativeDivisionId ?? "", cityId: base?.cityId ?? "", addressLine: "", neighborhood: null, postalCode: null, email: null, phone: null, isPrimary: sites.length === 0, googleMapsUrl: null, googlePlaceId: null, latitude: null, longitude: null }); };
  const apply = (site: PartySiteDraft) => { const normalized = site.isPrimary ? sites.map(item => ({ ...item, isPrimary: false })) : sites; const next = normalized.some(item => item.clientKey === site.clientKey) ? normalized.map(item => item.clientKey === site.clientKey ? site : item) : [...normalized, site]; onChange?.(next); setTarget(null); };
  const remove = (site: PartySiteDraft) => { const next = sites.filter(item => item.clientKey !== site.clientKey); if (site.isPrimary && next.length) next[0] = { ...next[0], isPrimary: true }; onChange?.(next); };
  const targetIsNew=Boolean(target&&!sites.some(site=>site.clientKey===target.clientKey));
  return <section className="overflow-hidden rounded-2xl border bg-card shadow-sm">
    <div className="flex flex-col justify-between gap-3 border-b bg-muted/20 px-4 py-3.5 sm:flex-row sm:items-center sm:px-5"><div className="flex items-start gap-3"><span className="rounded-xl bg-primary/10 p-2 text-primary"><MapPin className="h-5 w-5"/></span><div><h3 className="font-semibold">Sedes y puntos de entrega</h3><p className="text-sm text-muted-foreground">Todas las ubicaciones se administran aquí y se guardan junto con el tercero.</p></div></div>{canManage && !target && <Button type="button" variant="outline" onClick={add}><Plus className="mr-2 h-4 w-4"/>Agregar sede</Button>}</div>
    <div className="space-y-3 p-4 sm:p-5">
      {targetIsNew&&target&&<InlineSiteEditor value={target} onApply={apply} onClose={()=>setTarget(null)}/>}
      {!sites.length && !target ? <p className="rounded-xl border border-dashed p-5 text-sm text-muted-foreground">No tiene sedes. Agrega al menos una antes de guardar.</p> : sites.length>0&&<div><div className="mb-3 flex items-center justify-between gap-3"><div><h4 className="text-sm font-semibold">Ubicaciones registradas</h4><p className="text-xs text-muted-foreground">La principal aparece primero; las demás quedan en el mismo listado.</p></div><Badge variant="outline">{sites.length} {sites.length===1?"sede":"sedes"}</Badge></div><div className="grid items-start gap-3 md:grid-cols-2">{[...sites].sort((left,right)=>Number(right.isPrimary)-Number(left.isPrimary)).map(site => target?.clientKey===site.clientKey?<InlineSiteEditor key={site.clientKey} value={target} onApply={apply} onClose={()=>setTarget(null)}/>:<SiteCard key={site.clientKey} site={site} canEdit={canManage && !target} onEdit={() => setTarget({ ...site })} onRemove={() => remove(site)}/>)}</div></div>}
    </div>
  </section>;
}

function SiteCard({ site, canEdit, onEdit, onRemove }: { site: PartySiteDraft; canEdit: boolean; onEdit: () => void; onRemove: () => void }) {
  const href = mapsHref({ googleMapsUrl: site.googleMapsUrl ?? "", latitude: site.latitude == null ? "" : String(site.latitude), longitude: site.longitude == null ? "" : String(site.longitude) });
  return <article className="relative overflow-hidden rounded-2xl border bg-card p-4 shadow-sm">{site.isPrimary && <span className="absolute right-0 top-0 rounded-bl-xl bg-teal-600 px-3 py-1 text-xs font-semibold text-white">Principal</span>}<div className="flex items-start gap-3 pr-16"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-2xl bg-teal-50 text-teal-700"><Building2 className="h-5 w-5"/></span><div className="min-w-0"><h4 className="truncate font-semibold">{site.name}</h4><p className="text-xs text-muted-foreground">{site.code}</p></div></div><div className="mt-4 min-h-12 space-y-2 text-sm"><p className="flex gap-2"><MapPin className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground"/><span>{site.addressLine}{site.neighborhood ? ` · ${site.neighborhood}` : ""}</span></p>{site.phone && <p className="flex gap-2"><Phone className="h-4 w-4 text-muted-foreground"/>{site.phone}</p>}</div><div className="mt-4 flex flex-wrap items-center justify-between gap-2"><Badge variant={site.latitude != null && site.longitude != null ? "secondary" : "outline"}>{site.latitude != null && site.longitude != null ? "Ubicación verificada" : "Sin coordenadas"}</Badge><span className="flex gap-1">{href && <Button asChild size="sm" variant="ghost"><a href={href} target="_blank" rel="noreferrer"><Navigation className="mr-1 h-4 w-4"/>Mapa</a></Button>}{canEdit && <><Button type="button" size="sm" variant="outline" onClick={onEdit}><Pencil className="mr-1 h-4 w-4"/>Editar</Button><Button type="button" size="icon" variant="ghost" className="text-destructive" onClick={onRemove} aria-label={`Quitar ${site.name}`}><Trash2 className="h-4 w-4"/></Button></>}</span></div></article>;
}

function InlineSiteEditor({ value, onApply, onClose }: { value: PartySiteDraft; onApply: (site: PartySiteDraft) => void; onClose: () => void }) {
  const countries = useCountries(); const [site, setSite] = useState(value); const divisions = useDivisions(site.countryId), cities = useCities(site.administrativeDivisionId);
  const location: SiteLocationValue = { googleMapsUrl: site.googleMapsUrl ?? "", googlePlaceId: site.googlePlaceId ?? "", latitude: site.latitude == null ? "" : String(site.latitude), longitude: site.longitude == null ? "" : String(site.longitude) };
  const set = <K extends keyof PartySiteDraft>(key: K, next: PartySiteDraft[K]) => setSite(current => ({ ...current, [key]: next }));
  const apply = () => { if (!site.countryId || !site.administrativeDivisionId || !site.cityId || !site.code.trim() || !site.name.trim() || !site.addressLine.trim()) return toast.error("Completa código, nombre, ciudad y dirección de la sede."); onApply({ ...site, code: site.code.trim().toUpperCase(), name: site.name.trim(), addressLine: site.addressLine.trim() }); };
  return <article className="rounded-2xl border-2 border-teal-200 bg-teal-50/30 p-4 shadow-sm md:col-span-2 sm:p-5"><div className="mb-3 flex items-start justify-between gap-3"><div><h4 className="font-semibold">{value.partySiteId ? `Editar ${value.name}` : "Nueva sede"}</h4><p className="text-sm text-muted-foreground">Completa la ubicación y pulsa Listo para volver al listado. Solo Guardar tercero crea o actualiza las sedes.</p></div><Button type="button" size="icon" variant="ghost" onClick={onClose} aria-label="Cerrar editor de sede"><X className="h-4 w-4"/></Button></div><div className="grid gap-x-4 gap-y-3 border-t pt-3 md:grid-cols-2">
    <Field label="Código"><Input value={site.code} onChange={event => set("code", event.target.value)}/></Field><Field label="Nombre"><Input value={site.name} onChange={event => set("name", event.target.value)}/></Field>
    <Field label="País"><Select value={site.countryId} onValueChange={countryId => setSite(current => ({ ...current, countryId, administrativeDivisionId: "", cityId: "" }))}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{countries.data?.filter(item => item.isActive).map(item => <SelectItem key={item.countryId} value={item.countryId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Departamento"><Select value={site.administrativeDivisionId} onValueChange={administrativeDivisionId => setSite(current => ({ ...current, administrativeDivisionId, cityId: "" }))}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{divisions.data?.filter(item => item.isActive).map(item => <SelectItem key={item.administrativeDivisionId} value={item.administrativeDivisionId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Ciudad"><Select value={site.cityId} onValueChange={value => set("cityId", value)}><SelectTrigger><SelectValue placeholder="Selecciona"/></SelectTrigger><SelectContent>{cities.data?.filter(item => item.isActive).map(item => <SelectItem key={item.cityId} value={item.cityId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>
    <Field label="Dirección"><Input value={site.addressLine} onChange={event => set("addressLine", event.target.value)}/></Field><Field label="Barrio"><Input value={site.neighborhood ?? ""} onChange={event => set("neighborhood", event.target.value || null)}/></Field><Field label="Código postal"><Input value={site.postalCode ?? ""} onChange={event => set("postalCode", event.target.value || null)}/></Field><Field label="Correo de la sede"><Input value={site.email ?? ""} onChange={event => set("email", event.target.value || null)}/></Field><Field label="Teléfono"><Input value={site.phone ?? ""} onChange={event => set("phone", event.target.value || null)}/></Field>
    <label className="flex items-center justify-between rounded-xl border bg-background p-4 md:col-span-2"><span><b className="block text-sm">Sede principal</b><small className="text-muted-foreground">Solo una sede puede ser principal.</small></span><Switch checked={site.isPrimary} onCheckedChange={value => set("isPrimary", value)}/></label><SiteLocationFields value={location} onChange={next => setSite(current => ({ ...current, googleMapsUrl: next.googleMapsUrl || null, googlePlaceId: next.googlePlaceId || null, latitude: next.latitude ? Number(next.latitude) : null, longitude: next.longitude ? Number(next.longitude) : null }))}/>
  </div><div className="mt-5 flex justify-end gap-2"><Button type="button" variant="outline" onClick={onClose}>Cancelar</Button><Button type="button" onClick={apply}>Listo</Button></div></article>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) { return <div className="space-y-1.5"><Label>{label}</Label>{children}</div>; }
