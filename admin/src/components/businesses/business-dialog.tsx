"use client";

import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { ChevronDown, Loader2, Pencil, Search } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { useBusiness, useBusinesses, useCreateBusiness, useUpdateBusiness } from "@/hooks/use-businesses";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import type { Business } from "@/types/entities";
import type { CreateBusinessRequest } from "@/services/api/businesses";

export type BusinessDialogMode = "create" | "view" | "edit";
type Props = { mode: BusinessDialogMode; businessId?: string; initialBusiness?: Business; canCreate: boolean; canEdit: boolean; onEdit: () => void; onClose: () => void };

export function BusinessDialog(props: Props) {
  const business = useBusiness(props.businessId ?? "", props.initialBusiness);
  const [saving, setSaving] = useState(false);
  const title = props.mode === "create" ? "Nueva sede" : props.mode === "edit" && props.canEdit ? "Editar sede" : "Ver sede";
  return <Dialog open onOpenChange={open => { if (!open && !saving) props.onClose(); }}>
    <DialogContent className="flex max-h-[92dvh] max-w-3xl flex-col overflow-hidden p-0" onEscapeKeyDown={event => { if (saving) event.preventDefault(); }} onPointerDownOutside={event => { if (saving) event.preventDefault(); }}>
      <DialogHeader className="shrink-0 border-b px-6 py-5"><DialogTitle>{title}</DialogTitle><DialogDescription>{props.mode === "view" ? "Consulta los datos y la configuración de esta sede. Pulsa Editar para cambiarlos." : "Completa los datos de la sede y guarda para aplicar los cambios."}</DialogDescription></DialogHeader>
      {props.mode === "create" && !props.canCreate ? <p role="alert" className="p-6">No tienes permiso para crear sedes.</p>
        : props.mode !== "create" && business.isPending ? <div role="status" className="p-10 text-center"><Loader2 className="mx-auto h-6 w-6 animate-spin"/>Cargando sede…</div>
        : props.mode !== "create" && (business.isError || !business.data) ? <div role="alert" className="p-6">No se pudo cargar la sede. <Button variant="link" onClick={() => void business.refetch()}>Reintentar</Button></div>
        : <BusinessForm {...props} business={business.data} onSaving={setSaving} />}
    </DialogContent>
  </Dialog>;
}

function BusinessForm({ mode, business, canEdit, onEdit, onClose, onSaving }: Props & { business?: Business; onSaving: (saving: boolean) => void }) {
  const create = useCreateBusiness();
  const update = useUpdateBusiness();
  const posModes = useReferenceOptions("point-of-sale-mode");
  const readOnly = mode === "view" || mode === "edit" && !canEdit;
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<CreateBusinessRequest>(() => ({
    name: business?.name ?? "", description: business?.description ?? "", address: business?.address ?? "",
    phone: business?.phone ?? "", email: business?.email ?? "", website: business?.website ?? "",
    timeZone: business?.timeZone ?? "America/Bogota", sharesProductPrices: business?.sharesProductPrices ?? false,
    pointOfSaleMode: business?.pointOfSaleMode ?? "DirectSale", restaurantRequireWaiterCredential: business?.restaurantRequireWaiterCredential ?? true,
    restaurantRestrictTableToWaiter: business?.restaurantRestrictTableToWaiter ?? false,
    restaurantSuggestedTipPercent: business?.restaurantSuggestedTipPercent ?? 0, priceSourceBusinessId: "",
  }));
  const pending = create.isPending || update.isPending;
  const set = <K extends keyof CreateBusinessRequest>(key: K, value: CreateBusinessRequest[K]) => setForm(current => ({ ...current, [key]: value }));
  const valid = Boolean(form.name.trim() && form.timeZone.trim() && posModes.data?.some(option => option.code === form.pointOfSaleMode)
    && (mode !== "create" || form.address.trim() && form.phone.trim() && form.email.trim() && form.priceSourceBusinessId)
    && (form.pointOfSaleMode !== "RestaurantBar" || Number.isFinite(form.restaurantSuggestedTipPercent) && form.restaurantSuggestedTipPercent >= 0 && form.restaurantSuggestedTipPercent <= 10
    && Math.abs(form.restaurantSuggestedTipPercent * 100 - Math.round(form.restaurantSuggestedTipPercent * 100)) < 0.000001));
  async function save(event: FormEvent) {
    event.preventDefault();
    if (readOnly || pending || !valid) return;
    setError(null);
    onSaving(true);
    const values = { ...form, name: form.name.trim(), address: form.address.trim(), email: form.email.trim(), phone: form.phone.trim(), website: form.website.trim(), timeZone: form.timeZone.trim(),
      restaurantSuggestedTipPercent: form.pointOfSaleMode === "RestaurantBar" ? form.restaurantSuggestedTipPercent : 0 };
    try {
      if (mode === "create") await create.mutateAsync(values);
      else if (business) {
        const { priceSourceBusinessId, ...updated } = values;
        void priceSourceBusinessId;
        await update.mutateAsync({ id: business.businessId, values: updated });
      }
      toast.success(mode === "create" ? "Sede creada." : "Sede actualizada.");
      onClose();
    } catch (failure) { setError(failure instanceof Error ? failure.message : "No se pudo guardar la sede."); }
    finally { onSaving(false); }
  }
  return <form onSubmit={save} className="flex min-h-0 flex-1 flex-col">
    <fieldset disabled={pending} className="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-5">
      <section className="grid gap-4 sm:grid-cols-2" aria-label="Datos de la sede">
        <Field label="Nombre" id="business-name"><Input id="business-name" required maxLength={200} readOnly={readOnly} value={form.name} onChange={event => set("name", event.target.value)}/></Field>
        <Field label="Correo" id="business-email"><Input id="business-email" type="email" required={mode === "create"} readOnly={readOnly} value={form.email} onChange={event => set("email", event.target.value)}/></Field>
        <Field label="Dirección" id="business-address"><Input id="business-address" required={mode === "create"} readOnly={readOnly} value={form.address} onChange={event => set("address", event.target.value)}/></Field>
        <Field label="Teléfono" id="business-phone"><Input id="business-phone" required={mode === "create"} readOnly={readOnly} value={form.phone} onChange={event => set("phone", event.target.value)}/></Field>
        <Field label="Sitio web" id="business-website"><Input id="business-website" readOnly={readOnly} value={form.website} onChange={event => set("website", event.target.value)}/></Field>
        <Field label="Zona horaria" id="business-timezone"><Input id="business-timezone" required readOnly={readOnly} value={form.timeZone} onChange={event => set("timeZone", event.target.value)}/></Field>
        <Field label="Descripción" id="business-description" className="sm:col-span-2"><Textarea id="business-description" readOnly={readOnly} value={form.description} onChange={event => set("description", event.target.value)}/></Field>
      </section>
      {business && <p className="text-sm text-muted-foreground">Estado: {business.isActive ? "Activa" : "Inactiva"}</p>}
      <Field label="Tipo de operación" id="business-pos-mode">
        <Select value={form.pointOfSaleMode} disabled={readOnly || posModes.isPending || posModes.isError} onValueChange={value => set("pointOfSaleMode", value as Business["pointOfSaleMode"])}>
          <SelectTrigger id="business-pos-mode"><SelectValue placeholder="Selecciona el tipo"/></SelectTrigger><SelectContent>{posModes.data?.map(option => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent>
        </Select>
        {posModes.isError && <p role="alert" className="text-sm text-destructive">No se pudieron cargar los tipos de operación. <Button type="button" variant="link" onClick={() => void posModes.refetch()}>Reintentar</Button></p>}
      </Field>
      {form.pointOfSaleMode === "RestaurantBar" && <section className="space-y-4 rounded-2xl border p-4" aria-label="Atención en restaurante y bar">
        <h3 className="font-semibold">Atención en restaurante y bar</h3>
        <div className="flex items-center justify-between gap-4"><Label htmlFor="business-waiter-credential">Pedir clave al seleccionar mesero</Label><Switch id="business-waiter-credential" checked={form.restaurantRequireWaiterCredential} disabled={readOnly || form.restaurantRestrictTableToWaiter} onCheckedChange={checked => set("restaurantRequireWaiterCredential", checked)}/></div>
        <div className="flex items-center justify-between gap-4"><Label htmlFor="business-restrict-waiter">Restringir mesa al mesero responsable</Label><Switch id="business-restrict-waiter" checked={form.restaurantRestrictTableToWaiter} disabled={readOnly} onCheckedChange={checked => setForm(current => ({ ...current, restaurantRestrictTableToWaiter: checked, restaurantRequireWaiterCredential: checked || current.restaurantRequireWaiterCredential }))}/></div>
        <Field label="Propina sugerida en la precuenta (%)" id="business-tip"><Input id="business-tip" type="number" min={0} max={10} step="0.01" readOnly={readOnly} value={form.restaurantSuggestedTipPercent} onChange={event => set("restaurantSuggestedTipPercent", Number(event.target.value))}/></Field>
        <p className="text-xs text-muted-foreground">La propina es opcional para el cliente. La factura registra solo el consumo.</p>
      </section>}
      {mode === "create" && <BusinessPriceSource value={form.priceSourceBusinessId} onChange={value => set("priceSourceBusinessId", value)}/>}
      <section className="flex items-center justify-between gap-4 rounded-2xl border p-4">
        <div><Label htmlFor="business-shared-prices">Compartir precios y costos</Label><p className="mt-1 text-xs text-muted-foreground">Integra la sede al grupo que prepara, publica precios y calcula costo promedio en conjunto.</p></div>
        <Switch id="business-shared-prices" disabled={readOnly} checked={form.sharesProductPrices} onCheckedChange={checked => set("sharesProductPrices", checked)}/>
      </section>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    </fieldset>
    <DialogFooter className="shrink-0 border-t px-6 py-4">
      <Button type="button" variant="outline" disabled={pending} onClick={onClose}>Cancelar</Button>
      {readOnly ? canEdit && <Button key="edit" type="button" onClick={event => { event.preventDefault(); onEdit(); }}><Pencil className="mr-2 h-4 w-4"/>Editar</Button>
        : <Button key="save" type="submit" disabled={pending || !valid}>{pending && <Loader2 className="mr-2 h-4 w-4 animate-spin"/>}{pending ? mode === "create" ? "Creando…" : "Guardando…" : mode === "create" ? "Crear" : "Guardar"}</Button>}
    </DialogFooter>
  </form>;
}

function BusinessPriceSource({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  const trigger = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [page, setPage] = useState(1);
  const [label, setLabel] = useState("");
  useEffect(() => { const timer = setTimeout(() => { setTerm(search.trim()); setPage(1); }, 250); return () => clearTimeout(timer); }, [search]);
  const options = useBusinesses({ page, pageSize: 20, search: term }, open);
  return <div className="space-y-2 rounded-2xl border bg-muted/20 p-4">
    <Label htmlFor="business-price-source">Copiar precios desde</Label>
    <p className="text-xs text-muted-foreground">Copia los precios activos de la sede origen. El inventario nuevo comienza en cero.</p>
    <Popover modal open={open} onOpenChange={setOpen}><PopoverTrigger asChild><Button ref={trigger} id="business-price-source" type="button" variant="outline" role="combobox" aria-expanded={open} className="w-full justify-between">{value ? label : "Selecciona la sede origen"}<ChevronDown className="h-4 w-4"/></Button></PopoverTrigger>
      <PopoverContent portalContainer={trigger.current?.closest<HTMLElement>("[role='dialog']") ?? undefined} className="w-[min(28rem,90vw)] space-y-3" align="start">
        <div className="relative"><Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground"/><Input className="pl-9" aria-label="Buscar sede origen" value={search} onChange={event => setSearch(event.target.value)}/></div>
        {options.isLoading ? <p role="status">Cargando sedes…</p> : options.isError ? <div role="alert">No se pudieron cargar las sedes. <Button type="button" variant="link" onClick={() => void options.refetch()}>Reintentar</Button></div> : <div role="listbox" aria-label="Sedes origen" className="max-h-56 overflow-y-auto">{options.data?.items.map(business => <button type="button" role="option" aria-selected={value === business.businessId} key={business.businessId} disabled={!business.isActive} className="block w-full rounded-lg p-3 text-left text-sm hover:bg-muted disabled:opacity-50" onClick={() => { onChange(business.businessId); setLabel(business.name); setOpen(false); }}>{business.name}{!business.isActive && " · Inactiva"}</button>)}{options.data?.items.length === 0 && <p className="p-3 text-sm text-muted-foreground">No hay sedes para esta búsqueda.</p>}</div>}
        {(options.data?.totalPages ?? 0) > 1 && <div className="flex justify-between gap-2"><Button type="button" variant="outline" size="sm" disabled={page <= 1 || options.isFetching} onClick={() => setPage(current => current - 1)}>Anterior</Button><span className="text-sm">Página {page}</span><Button type="button" variant="outline" size="sm" disabled={page >= (options.data?.totalPages ?? 0) || options.isFetching} onClick={() => setPage(current => current + 1)}>Siguiente</Button></div>}
      </PopoverContent>
    </Popover>
  </div>;
}

function Field({ label, id, children, className = "" }: { label: string; id: string; children: ReactNode; className?: string }) {
  return <div className={`space-y-2 ${className}`}><Label htmlFor={id}>{label}</Label>{children}</div>;
}
