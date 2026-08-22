"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BadgePercent, CalendarClock, Check, CircleDollarSign, Layers3, Pencil, Plus, Radio, Search, Trash2, Users } from "lucide-react";
import { toast } from "sonner";

import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { InventoryProductPicker } from "@/components/inventory/inventory-operation-workspace";
import { formatCurrency } from "@/lib/utils";
import { priceSegmentsApi, type PriceChannelStrategy, type PriceSegmentItem, type PriceSegmentKind, type PriceSegmentSummary } from "@/services/api/price-segments";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

type ItemDraft = {
  originalMinimumQuantity: number | null;
  productId: string;
  productCode: string;
  productName: string;
  amount: number;
  minimumQuantity: number;
  validFrom: string;
  validUntil: string;
  excluded: boolean;
};

export function PriceSegmentsManager() {
  const client = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => state.user?.permissions ?? []);
  const canManage = permissions.includes("pricing.segments.manage");
  const [search, setSearch] = useState("");
  const [createOpen, setCreateOpen] = useState(false);
  const [selected, setSelected] = useState<PriceSegmentSummary | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [draft, setDraft] = useState<ItemDraft | null>(null);
  const [deleteItem, setDeleteItem] = useState<PriceSegmentItem | null>(null);
  const [kind, setKind] = useState<PriceSegmentKind>("PriceList");
  const [name, setName] = useState("");
  const [channelStrategy, setChannelStrategy] = useState<PriceChannelStrategy>("PercentageOverBasePrice");
  const [channelValue, setChannelValue] = useState(0);
  const [createItems, setCreateItems] = useState<ItemDraft[]>([]);
  const [createItem, setCreateItem] = useState<ItemDraft>(emptyDraft());

  const segments = useQuery({ queryKey: ["price-segments"], queryFn: priceSegmentsApi.list });
  const items = useQuery({
    queryKey: ["price-segments", selected?.kind, selected?.id],
    queryFn: () => priceSegmentsApi.items(selected!.kind, selected!.id),
    enabled: selected?.kind === "PriceList" || selected?.strategy === "FixedSpecialPrice",
  });

  const create = useMutation({
    mutationFn: async () => {
      return priceSegmentsApi.create({
        kind,
        name: name.trim(),
        channelStrategy: kind === "PriceChannel" ? channelStrategy : undefined,
        channelValue: kind === "PriceChannel" && requiresChannelValue(channelStrategy) ? channelValue : null,
        items: kind === "PriceList" || channelStrategy === "FixedSpecialPrice" ? createItems.map((item) => ({ productId: item.productId, amount: item.amount, minimumQuantity: kind === "PriceList" ? item.minimumQuantity : 1, validFrom: item.validFrom || null, validUntil: item.validUntil || null })) : undefined,
      });
    },
    onSuccess: async (created) => {
      await client.invalidateQueries({ queryKey: ["price-segments"] });
      setCreateOpen(false);
      setName("");
      setChannelStrategy("PercentageOverBasePrice");
      setChannelValue(0);
      setCreateItems([]);
      setCreateItem(emptyDraft());
      setSelected(created);
      setChannelStrategy(created.strategy ?? "PercentageOverBasePrice");
      setChannelValue(created.value ?? 0);
      setDetailOpen(true);
      toast.success(kind === "PriceList" ? "Lista de precios creada." : "Canal comercial creado.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible crear el segmento."),
  });

  const saveItem = useMutation({
    mutationFn: async (value: ItemDraft) => {
      if (!selected) throw new Error("Selecciona una lista o canal.");
      if (value.originalMinimumQuantity !== null && value.originalMinimumQuantity !== value.minimumQuantity) {
        await priceSegmentsApi.deleteItem(selected.kind, selected.id, value.productId, value.originalMinimumQuantity);
      }
      await priceSegmentsApi.saveItem(selected.kind, selected.id, value.productId, {
        amount: value.amount,
        minimumQuantity: selected.kind === "PriceList" ? value.minimumQuantity : 1,
        validFrom: value.validFrom || null,
        validUntil: value.validUntil || null,
        excluded: selected.kind === "PriceChannel" && value.excluded,
      });
    },
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ["price-segments"] }),
        client.invalidateQueries({ queryKey: ["price-segments", selected?.kind, selected?.id] }),
      ]);
      setDraft(null);
      setDetailOpen(true);
      toast.success("Condición de precio guardada.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible guardar la condición."),
  });

  const removeItem = useMutation({
    mutationFn: async (value: PriceSegmentItem) => {
      if (!selected) return;
      await priceSegmentsApi.deleteItem(selected.kind, selected.id, value.productId, value.minimumQuantity);
    },
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ["price-segments"] }),
        client.invalidateQueries({ queryKey: ["price-segments", selected?.kind, selected?.id] }),
      ]);
      setDeleteItem(null);
      setDetailOpen(true);
      toast.success("Producto retirado del segmento.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible retirar el producto."),
  });

  const saveChannel = useMutation({
    mutationFn: async () => {
      if (!selected || selected.kind !== "PriceChannel") return;
      await priceSegmentsApi.saveChannelSettings(selected.id, channelStrategy, requiresChannelValue(channelStrategy) ? channelValue : null);
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["price-segments"] });
      setSelected((current) => current ? { ...current, strategy: channelStrategy, value: requiresChannelValue(channelStrategy) ? channelValue : null } : current);
      toast.success("Regla general del canal guardada.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible guardar la regla del canal."),
  });

  const filtered = useMemo(() => {
    const term = search.trim().toLocaleLowerCase("es-CO");
    return (segments.data ?? []).filter((segment) =>
      !term || segment.name.toLocaleLowerCase("es-CO").includes(term));
  }, [search, segments.data]);

  function addCreateItem() {
    if (!createItem.productId || createItem.amount <= 0 || createItem.minimumQuantity <= 0) return;
    setCreateItems((current) => {
      const duplicate = current.findIndex((item) => item.productId === createItem.productId && item.minimumQuantity === createItem.minimumQuantity);
      return duplicate < 0 ? [...current, createItem] : current.map((item, index) => index === duplicate ? createItem : item);
    });
    setCreateItem(emptyDraft());
  }

  return <div className="space-y-6">
    <header className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
      <div>
        <p className="text-sm font-medium text-primary">Precios por segmento</p>
        <h1 className="text-3xl font-semibold tracking-tight">Listas y canales comerciales</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">Configura precios por volumen en listas y precios o exclusiones por canal. El precio público del producto permanece independiente.</p>
      </div>
      {canManage && <Button onClick={() => setCreateOpen(true)}><Plus className="mr-2 h-4 w-4" />Nueva lista o canal</Button>}
    </header>

    <Card>
      <CardContent className="pt-6">
        <div className="relative mb-5">
          <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
          <Input className="pl-9" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar por nombre" />
        </div>
        <Tabs defaultValue="PriceList">
          <TabsList><TabsTrigger value="PriceList">Listas</TabsTrigger><TabsTrigger value="PriceChannel">Canales</TabsTrigger></TabsList>
          {(["PriceList", "PriceChannel"] as PriceSegmentKind[]).map((tab) =>
            <TabsContent key={tab} value={tab} className="mt-5">
              <div className="overflow-hidden rounded-xl border">
                <table className="w-full text-sm"><thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-4 py-3 text-left">Nombre</th><th className="px-4 py-3 text-right">{tab === "PriceList" ? "Productos" : "Regla"}</th><th className="px-4 py-3 text-right">Clientes</th><th className="px-4 py-3 text-right">Estado</th></tr></thead><tbody>
                {filtered.filter((segment) => segment.kind === tab).map((segment) =>
                  <tr key={segment.id} className="cursor-pointer border-t transition hover:bg-muted/40" onClick={() => { setSelected(segment); setDetailOpen(true); setChannelStrategy(segment.strategy ?? "PercentageOverBasePrice"); setChannelValue(segment.value ?? 0); }}><td className="px-4 py-4 font-semibold">{segment.name}</td><td className="px-4 py-4 text-right">{segment.kind === "PriceList" ? segment.productCount : channelStrategyLabel(segment.strategy)}</td><td className="px-4 py-4 text-right tabular-nums">{segment.customerCount}</td><td className="px-4 py-4 text-right"><Badge variant={segment.isActive ? "secondary" : "outline"}>{segment.isActive ? "Activo" : "Inactivo"}</Badge></td></tr>)}
                {!segments.isLoading && filtered.filter((segment) => segment.kind === tab).length === 0 &&
                  <tr><td colSpan={4} className="p-12 text-center text-muted-foreground">Sin datos</td></tr>}
                </tbody></table>
              </div>
            </TabsContent>)}
        </Tabs>
      </CardContent>
    </Card>

    <Dialog open={createOpen} onOpenChange={setCreateOpen}>
      <DialogContent className="max-h-[92dvh] max-w-4xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Nueva lista o canal</DialogTitle>
          <DialogDescription>Crea el segmento y configúralo de una vez. El identificador interno se genera automáticamente.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-2">
            <Button type="button" variant={kind === "PriceList" ? "default" : "outline"} onClick={() => setKind("PriceList")}><Layers3 className="mr-2 h-4 w-4" />Lista</Button>
            <Button type="button" variant={kind === "PriceChannel" ? "default" : "outline"} onClick={() => setKind("PriceChannel")}><Radio className="mr-2 h-4 w-4" />Canal</Button>
          </div>
          <div className="space-y-2"><Label>Nombre *</Label><Input value={name} onChange={(event) => setName(event.target.value)} placeholder="Mayoristas" maxLength={120} /></div>
          {kind === "PriceChannel" && <div className="space-y-3"><Label>Modo de precio</Label><div className="grid gap-2 sm:grid-cols-2">{channelStrategies.map((mode) => <Button key={mode.value} type="button" variant={channelStrategy === mode.value ? "default" : "outline"} className="h-auto justify-start whitespace-normal py-3 text-left" onClick={() => { setChannelStrategy(mode.value); setChannelValue(0); setCreateItems([]); }}>{mode.label}</Button>)}</div>{requiresChannelValue(channelStrategy) && <div className="space-y-2"><Label>{channelValueLabel(channelStrategy)}</Label><FormattedNumberInput kind="percent" value={channelValue} invalid={!validChannelValue(channelStrategy, channelValue)} onValueChange={(value) => setChannelValue(value ?? 0)} /></div>}<p className="text-xs text-muted-foreground">{channelStrategyHelp(channelStrategy)}</p></div>}
          {(kind === "PriceList" || channelStrategy === "FixedSpecialPrice") && <div className="space-y-4 rounded-2xl border bg-muted/15 p-4"><div><h3 className="font-semibold">{kind === "PriceList" ? "Productos y precios por cantidad" : "Precios especiales por producto"}</h3><p className="text-sm text-muted-foreground">{kind === "PriceList" ? "Agrega el mismo producto varias veces para definir precios desde 1, 3, 5 o cualquier cantidad." : "El precio público se carga como punto de partida y puedes cambiarlo antes de crear el canal."}</p></div>{businessId && <InventoryProductPicker businessId={businessId} selectedProductIds={new Set()} disabled={create.isPending} label="Producto" onSelect={(product) => setCreateItem({ ...createItem, productId: product.productId, productCode: product.productCode, productName: product.productName, amount: product.saleUnitPrice ?? 0 })} />}{createItem.productId && <div className={`grid gap-3 rounded-xl border bg-background p-3 sm:items-end ${kind === "PriceList" ? "sm:grid-cols-[1fr_170px_150px_auto]" : "sm:grid-cols-[1fr_190px_auto]"}`}><div><Label>Producto</Label><p className="mt-2 font-medium">{createItem.productName}</p><p className="text-xs text-muted-foreground">{createItem.productCode || "Sin código"}</p></div><div className="space-y-2"><Label>Precio</Label><FormattedNumberInput kind="currency" value={createItem.amount} invalid={createItem.amount <= 0} onValueChange={(value) => setCreateItem({ ...createItem, amount: value ?? 0 })} /></div>{kind === "PriceList" && <div className="space-y-2"><Label>Desde cantidad</Label><FormattedNumberInput value={createItem.minimumQuantity} invalid={createItem.minimumQuantity <= 0} onValueChange={(value) => setCreateItem({ ...createItem, minimumQuantity: value ?? 0 })} /></div>}<Button type="button" variant="secondary" disabled={createItem.amount <= 0 || createItem.minimumQuantity <= 0} onClick={addCreateItem}>Agregar precio</Button></div>}{createItems.length > 0 && <div className="overflow-hidden rounded-xl border bg-background"><table className="w-full text-sm"><thead className="bg-muted/60"><tr><th className="px-3 py-2 text-left">Producto</th>{kind === "PriceList" && <th className="px-3 py-2 text-right">Desde</th>}<th className="px-3 py-2 text-right">Precio</th><th className="w-12" /></tr></thead><tbody>{createItems.map((item, index) => <tr key={`${item.productId}-${item.minimumQuantity}`} className="border-t"><td className="px-3 py-2"><b>{item.productName}</b><small className="block text-muted-foreground">{item.productCode}</small></td>{kind === "PriceList" && <td className="px-3 py-2 text-right">{item.minimumQuantity}</td>}<td className="px-3 py-2 text-right font-medium">{formatCurrency(item.amount)}</td><td><Button type="button" size="icon" variant="ghost" aria-label={`Eliminar precio de ${item.productName}`} onClick={() => setCreateItems((current) => current.filter((_, currentIndex) => currentIndex !== index))}><Trash2 className="h-4 w-4 text-destructive" /></Button></td></tr>)}</tbody></table></div>}</div>}
        </div>
        <DialogFooter><Button type="button" variant="outline" onClick={() => setCreateOpen(false)}>Cancelar</Button><Button disabled={!name.trim() || create.isPending || (kind === "PriceChannel" && !validChannelValue(channelStrategy, channelValue)) || (kind === "PriceChannel" && channelStrategy === "FixedSpecialPrice" && createItems.length === 0)} onClick={() => create.mutate()}>{create.isPending ? "Guardando…" : kind === "PriceList" ? `Crear lista${createItems.length ? ` · ${createItems.length} precios` : ""}` : "Crear canal"}</Button></DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog open={Boolean(selected) && detailOpen} onOpenChange={(open) => { setDetailOpen(open); if (!open) setSelected(null); }}>
      <DialogContent className="max-h-[92dvh] max-w-5xl overflow-y-auto">
        {selected && <>
          <DialogHeader>
            <div className="flex items-start gap-3">
              <span className="grid h-11 w-11 place-items-center rounded-xl bg-primary/10 text-primary">{selected.kind === "PriceList" ? <Layers3 className="h-5 w-5" /> : <Radio className="h-5 w-5" />}</span>
              <div><DialogTitle>{selected.name}</DialogTitle><DialogDescription>{selected.code} · {selected.kind === "PriceList" ? "Escalas por cantidad" : "Regla general para todo el catálogo"}</DialogDescription></div>
            </div>
          </DialogHeader>
          <div className="grid gap-3 sm:grid-cols-3">
            <Metric icon={BadgePercent} label={selected.kind === "PriceList" ? "Condiciones" : "Modo"} value={selected.kind === "PriceList" ? items.data?.length ?? 0 : channelStrategyLabel(channelStrategy)} />
            <Metric icon={Users} label="Clientes asignados" value={selected.customerCount} />
            <Metric icon={CircleDollarSign} label="Tipo" value={selected.kind === "PriceList" ? "Lista" : "Canal"} />
          </div>
          {selected.kind === "PriceList" || channelStrategy === "FixedSpecialPrice" ? <><div className="flex items-center justify-between gap-3 border-t pt-4">
            <div><h3 className="font-semibold">Productos y condiciones</h3><p className="text-sm text-muted-foreground">{selected.kind === "PriceList" ? "Un producto puede tener varias escalas." : "Define un precio propio o exclúyelo del canal."}</p></div>
            {canManage && <Button onClick={() => { setDetailOpen(false); setDraft(emptyDraft()); }}><Plus className="mr-2 h-4 w-4" />Agregar producto</Button>}
          </div></> : <div className="space-y-5 rounded-2xl border bg-muted/20 p-5">
            <div><h3 className="font-semibold">Regla de precio del canal</h3><p className="text-sm text-muted-foreground">{channelStrategyHelp(channelStrategy)}</p></div>
            <div className="grid gap-3 sm:grid-cols-2">{channelStrategies.filter((mode) => mode.value !== "FixedSpecialPrice").map((mode) => <Button key={mode.value} type="button" variant={channelStrategy === mode.value ? "default" : "outline"} onClick={() => { setChannelStrategy(mode.value); setChannelValue(0); }}>{mode.label}</Button>)}</div>
            {requiresChannelValue(channelStrategy) && <div className="max-w-sm space-y-2"><Label>{channelValueLabel(channelStrategy)}</Label><FormattedNumberInput kind="percent" value={channelValue} invalid={!validChannelValue(channelStrategy, channelValue)} onValueChange={(value) => setChannelValue(value ?? 0)} /></div>}
            {canManage && <Button disabled={saveChannel.isPending || !validChannelValue(channelStrategy, channelValue)} onClick={() => saveChannel.mutate()}>{saveChannel.isPending ? "Guardando…" : "Guardar regla del canal"}</Button>}
          </div>}
          <div className="overflow-hidden rounded-xl border">
            <table className="w-full text-sm">
              <thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-4 py-3 text-left">Producto</th>{selected.kind === "PriceList" && <th className="px-4 py-3 text-right">Desde</th>}<th className="px-4 py-3 text-right">Precio</th><th className="px-4 py-3 text-left">Vigencia</th><th className="w-24" /></tr></thead>
              <tbody>
                {(items.data ?? []).map((item) => <tr key={item.productId + "-" + item.minimumQuantity} className="border-t align-middle">
                  <td className="px-4 py-3"><b className="block">{item.productName}</b><small className="text-muted-foreground">{item.productCode || "Sin código"}</small></td>
                  {selected.kind === "PriceList" && <td className="px-4 py-3 text-right tabular-nums">{item.minimumQuantity}</td>}
                  <td className="px-4 py-3 text-right"><span className={item.excluded ? "text-destructive" : "font-medium"}>{item.excluded ? "Excluido" : formatCurrency(item.amount)}</span></td>
                  <td className="px-4 py-3 text-muted-foreground"><CalendarClock className="mr-1 inline h-3.5 w-3.5" />{new Date(item.validFrom).toLocaleDateString("es-CO")}{item.validUntil ? " – " + new Date(item.validUntil).toLocaleDateString("es-CO") : " – Sin vencimiento"}</td>
                  <td className="px-2 py-3"><div className="flex justify-end">{canManage && <><Button size="icon" variant="ghost" aria-label={"Editar " + item.productName} onClick={() => { setDetailOpen(false); setDraft(fromItem(item)); }}><Pencil className="h-4 w-4" /></Button><Button size="icon" variant="ghost" className="text-destructive" aria-label={"Retirar " + item.productName} onClick={() => { setDetailOpen(false); setDeleteItem(item); }}><Trash2 className="h-4 w-4" /></Button></>}</div></td>
                </tr>)}
                {!items.isLoading && (items.data ?? []).length === 0 && <tr><td colSpan={5} className="p-10 text-center text-muted-foreground">Sin datos</td></tr>}
              </tbody>
            </table>
          </div>
        </>}
      </DialogContent>
    </Dialog>

    <PriceItemDialog segment={selected?.kind === "PriceList" || selected?.strategy === "FixedSpecialPrice" ? selected : null} draft={draft} onChange={(value) => { setDraft(value); if (!value && selected) setDetailOpen(true); }} onSave={(value) => saveItem.mutate(value)} saving={saveItem.isPending} />

    <Dialog open={Boolean(deleteItem)} onOpenChange={(open) => { if (!open) { setDeleteItem(null); if (selected) setDetailOpen(true); } }}>
      <DialogContent>
        <DialogHeader><DialogTitle>Retirar producto</DialogTitle><DialogDescription>Se dejará de aplicar esta condición a {deleteItem?.productName}. El precio público del producto no cambia.</DialogDescription></DialogHeader>
        <DialogFooter><Button variant="outline" onClick={() => setDeleteItem(null)}>Cancelar</Button><Button variant="destructive" disabled={removeItem.isPending} onClick={() => deleteItem && removeItem.mutate(deleteItem)}>Retirar</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </div>;
}

function PriceItemDialog({ segment, draft, onChange, onSave, saving }: { segment: PriceSegmentSummary | null; draft: ItemDraft | null; onChange: (value: ItemDraft | null) => void; onSave: (value: ItemDraft) => void; saving: boolean }) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const isNew = draft?.originalMinimumQuantity === null;

  return <Dialog open={Boolean(draft)} onOpenChange={(open) => { if (!open) onChange(null); }}>
    <DialogContent className="max-w-2xl">
      {draft && segment && <>
        <DialogHeader><DialogTitle>{isNew ? "Agregar producto" : "Editar condición"}</DialogTitle><DialogDescription>{segment.kind === "PriceList" ? "Define el precio y la cantidad desde la cual aplica." : "Define el precio del canal o marca el producto como excluido."}</DialogDescription></DialogHeader>
        <div className="space-y-4">
          {isNew ? <div className="space-y-2">
            {businessId && <InventoryProductPicker businessId={businessId} selectedProductIds={new Set(draft.productId ? [draft.productId] : [])} disabled={saving} label="Producto *" onSelect={(product) => onChange({ ...draft, productId: product.productId, productCode: product.productCode, productName: product.productName, amount: product.saleUnitPrice ?? 0 })} />}
            {draft.productId && <p className="flex items-center gap-2 text-sm text-primary"><Check className="h-4 w-4" />{draft.productName}</p>}
          </div> : <div className="rounded-xl border bg-muted/20 p-3"><b>{draft.productName}</b><p className="text-xs text-muted-foreground">{draft.productCode}</p></div>}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2"><Label>Precio *</Label><FormattedNumberInput kind="currency" value={draft.amount} invalid={draft.amount <= 0} onValueChange={(value) => onChange({ ...draft, amount: value ?? 0 })} /></div>
            {segment.kind === "PriceList" && <div className="space-y-2"><Label>Cantidad mínima *</Label><FormattedNumberInput value={draft.minimumQuantity} invalid={draft.minimumQuantity <= 0} onValueChange={(value) => onChange({ ...draft, minimumQuantity: value ?? 0 })} /></div>}
          </div>
          <div className="grid gap-4 sm:grid-cols-2"><div className="space-y-2"><Label>Válido desde</Label><DateTimePicker value={draft.validFrom} onChange={(validFrom) => onChange({ ...draft, validFrom })} /></div><div className="space-y-2"><Label>Válido hasta</Label><DateTimePicker value={draft.validUntil} onChange={(validUntil) => onChange({ ...draft, validUntil })} /></div></div>
        </div>
        <DialogFooter><Button variant="outline" onClick={() => onChange(null)}>Cancelar</Button><Button disabled={saving || !draft.productId || draft.amount <= 0 || draft.minimumQuantity <= 0} onClick={() => onSave(draft)}>{saving ? "Guardando…" : "Guardar condición"}</Button></DialogFooter>
      </>}
    </DialogContent>
  </Dialog>;
}

function emptyDraft(): ItemDraft {
  return { originalMinimumQuantity: null, productId: "", productCode: "", productName: "", amount: 0, minimumQuantity: 1, validFrom: localDateTime(new Date()), validUntil: "", excluded: false };
}
function fromItem(item: PriceSegmentItem): ItemDraft {
  return { originalMinimumQuantity: item.minimumQuantity, productId: item.productId, productCode: item.productCode, productName: item.productName, amount: item.amount, minimumQuantity: item.minimumQuantity, validFrom: localDateTime(new Date(item.validFrom)), validUntil: item.validUntil ? localDateTime(new Date(item.validUntil)) : "", excluded: item.excluded };
}
function localDateTime(value: Date) {
  const local = new Date(value.getTime() - value.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}
function Metric({ icon: Icon, label, value }: { icon: typeof Users; label: string; value: React.ReactNode }) {
  return <div className="rounded-xl border bg-muted/10 p-4"><Icon className="mb-2 h-4 w-4 text-primary" /><b className="block text-lg">{value}</b><small className="text-muted-foreground">{label}</small></div>;
}

const channelStrategies: Array<{ value: PriceChannelStrategy; label: string }> = [
  { value: "PercentageOverBasePrice", label: "% sobre precio público" },
  { value: "PercentageOverAverageCost", label: "% sobre costo promedio" },
  { value: "FixedMarginOverAverageCost", label: "Margen sobre costo promedio" },
  { value: "SellAtAverageCost", label: "Vender al costo promedio" },
  { value: "FixedSpecialPrice", label: "Precio especial por producto" },
];
function channelStrategyLabel(value: PriceChannelStrategy | null) { return channelStrategies.find((item) => item.value === value)?.label ?? "Sin configurar"; }
function requiresChannelValue(value: PriceChannelStrategy) { return value === "PercentageOverBasePrice" || value === "PercentageOverAverageCost" || value === "FixedMarginOverAverageCost"; }
function validChannelValue(strategy: PriceChannelStrategy, value: number) { return !requiresChannelValue(strategy) || (strategy === "FixedMarginOverAverageCost" ? value >= 0 && value < 100 : value >= -100 && value <= 1000); }
function channelValueLabel(strategy: PriceChannelStrategy) { return strategy === "FixedMarginOverAverageCost" ? "Margen objetivo (%)" : "Variación (%) — usa negativo para descuento"; }
function channelStrategyHelp(strategy: PriceChannelStrategy) { return ({ PercentageOverBasePrice: "Aumenta o reduce el precio público vigente.", PercentageOverAverageCost: "Calcula el precio desde el costo promedio y aplica el porcentaje indicado.", FixedMarginOverAverageCost: "Calcula el precio necesario para conservar el margen indicado sobre el costo promedio.", SellAtAverageCost: "Vende al costo promedio vigente del inventario.", FixedSpecialPrice: "Usa un precio específico por producto; los no configurados conservan el precio público." } satisfies Record<PriceChannelStrategy,string>)[strategy]; }
