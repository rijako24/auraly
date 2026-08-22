"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CalendarClock, Check, Pencil, Plus, Radio, Search, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { DateTimePicker } from "@/components/ui/date-time-picker";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ProductPicker } from "@/components/products/product-picker";
import { formatCurrency } from "@/lib/utils";
import { priceSegmentsApi, type PriceChannelStrategy, type PriceSegmentItem, type PriceSegmentSummary } from "@/services/api/price-segments";
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
  const [editingChannel, setEditingChannel] = useState(false);
  const [draft, setDraft] = useState<ItemDraft | null>(null);
  const [deleteItem, setDeleteItem] = useState<PriceSegmentItem | null>(null);
  const [name, setName] = useState("");
  const [channelStrategy, setChannelStrategy] = useState<PriceChannelStrategy>("TieredProductPrice");
  const [channelValue, setChannelValue] = useState(0);
  const [createItems, setCreateItems] = useState<ItemDraft[]>([]);
  const [createItem, setCreateItem] = useState<ItemDraft>(emptyDraft());

  const segments = useQuery({ queryKey: ["price-segments"], queryFn: priceSegmentsApi.list });
  const items = useQuery({
    queryKey: ["price-segments", selected?.id],
    queryFn: () => priceSegmentsApi.items(selected!.id),
    enabled: selected?.strategy === "TieredProductPrice",
  });

  const create = useMutation({
    mutationFn: async () => {
      return priceSegmentsApi.create({
        name: name.trim(),
        channelStrategy,
        channelValue: requiresChannelValue(channelStrategy) ? channelValue : null,
        items: channelStrategy === "TieredProductPrice" ? createItems.map((item) => ({ productId: item.productId, amount: item.amount, minimumQuantity: item.minimumQuantity, validFrom: item.validFrom || null, validUntil: item.validUntil || null })) : undefined,
      });
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["price-segments"] });
      setCreateOpen(false);
      setName("");
      setChannelStrategy("TieredProductPrice");
      setChannelValue(0);
      setCreateItems([]);
      setCreateItem(emptyDraft());
      toast.success("Canal de precios creado.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible crear el segmento."),
  });

  const saveItem = useMutation({
    mutationFn: async (value: ItemDraft) => {
      if (!selected) throw new Error("Selecciona un canal.");
      if (value.originalMinimumQuantity !== null && value.originalMinimumQuantity !== value.minimumQuantity) {
        await priceSegmentsApi.deleteItem(selected.id, value.productId, value.originalMinimumQuantity);
      }
      await priceSegmentsApi.saveItem(selected.id, value.productId, {
        amount: value.amount,
        minimumQuantity: value.minimumQuantity,
        validFrom: value.validFrom || null,
        validUntil: value.validUntil || null,
        excluded: value.excluded,
      });
    },
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ["price-segments"] }),
        client.invalidateQueries({ queryKey: ["price-segments", selected?.id] }),
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
      await priceSegmentsApi.deleteItem(selected.id, value.productId, value.minimumQuantity);
    },
    onSuccess: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: ["price-segments"] }),
        client.invalidateQueries({ queryKey: ["price-segments", selected?.id] }),
      ]);
      setDeleteItem(null);
      setDetailOpen(true);
      toast.success("Producto retirado del segmento.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible retirar el producto."),
  });

  const saveChannel = useMutation({
    mutationFn: async () => {
      if (!selected) return;
      await priceSegmentsApi.saveChannelSettings(selected.id, name.trim(), channelStrategy, requiresChannelValue(channelStrategy) ? channelValue : null);
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["price-segments"] });
      setSelected((current) => current ? { ...current, name: name.trim(), strategy: channelStrategy, value: requiresChannelValue(channelStrategy) ? channelValue : null } : current);
      setEditingChannel(false);
      toast.success("Canal de precios guardado.");
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
        <h1 className="text-3xl font-semibold tracking-tight">Canales de precios</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">Define una regla general o precios por producto y cantidad. Si no aplica un canal se usa el precio público.</p>
      </div>
      {canManage && <Button onClick={() => { setName(""); setChannelStrategy("TieredProductPrice"); setChannelValue(0); setCreateItems([]); setCreateItem(emptyDraft()); setCreateOpen(true); }}><Plus className="mr-2 h-4 w-4" />Nuevo canal</Button>}
    </header>

    <Card>
      <CardContent className="pt-6">
        <div className="relative mb-5">
          <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
          <Input className="pl-9" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar por nombre" />
        </div>
        <div className="overflow-hidden rounded-xl border">
          <table className="w-full text-sm"><thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-4 py-3 text-left">Nombre</th><th className="px-4 py-3 text-right">Modo</th><th className="px-4 py-3 text-right">Clientes</th><th className="px-4 py-3 text-right">Estado</th></tr></thead><tbody>
          {filtered.map((segment) =>
            <tr key={segment.id} className="cursor-pointer border-t transition hover:bg-muted/40" onClick={() => { setSelected(segment); setDetailOpen(true); setEditingChannel(false); setName(segment.name); setChannelStrategy(segment.strategy ?? "TieredProductPrice"); setChannelValue(segment.value ?? 0); }}><td className="px-4 py-4 font-semibold">{segment.name}</td><td className="px-4 py-4 text-right">{channelStrategyLabel(segment.strategy)}</td><td className="px-4 py-4 text-right tabular-nums">{segment.customerCount}</td><td className="px-4 py-4 text-right"><Badge variant={segment.isActive ? "secondary" : "outline"}>{segment.isActive ? "Activo" : "Inactivo"}</Badge></td></tr>)}
          {!segments.isLoading && filtered.length === 0 && <tr><td colSpan={4} className="p-12 text-center text-muted-foreground">Sin datos</td></tr>}
          </tbody></table>
        </div>
      </CardContent>
    </Card>

    <Dialog open={createOpen} onOpenChange={setCreateOpen}>
      <DialogContent className="max-h-[92dvh] max-w-4xl overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Nuevo canal de precios</DialogTitle>
          <DialogDescription>Elige el modo y configura el canal completo antes de guardarlo.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2"><Label>Nombre *</Label><Input value={name} onChange={(event) => setName(event.target.value)} placeholder="Mayoristas" maxLength={120} /></div>
          <div className="space-y-3"><Label>Modo de precio</Label><div className="grid gap-2 sm:grid-cols-2">{channelStrategies.map((mode) => <Button key={mode.value} type="button" variant={channelStrategy === mode.value ? "default" : "outline"} className="h-auto justify-start whitespace-normal py-3 text-left" onClick={() => { setChannelStrategy(mode.value); setChannelValue(0); }}>{mode.label}</Button>)}</div>{requiresChannelValue(channelStrategy) && <div className="space-y-2"><Label>{channelValueLabel(channelStrategy)}</Label><FormattedNumberInput kind="percent" allowNegative={allowsNegativeChannelValue(channelStrategy)} value={channelValue} invalid={!validChannelValue(channelStrategy, channelValue)} onValueChange={(value) => setChannelValue(value ?? 0)} /></div>}<p className="text-xs text-muted-foreground">{channelStrategyHelp(channelStrategy)}</p></div>
          {channelStrategy === "TieredProductPrice" && <div className="space-y-4 rounded-2xl border bg-muted/15 p-4"><div><h3 className="font-semibold">Productos y precios por cantidad</h3><p className="text-sm text-muted-foreground">El selector conserva su propio espacio y toma el primer resultado al pulsar Agregar o Enter. La primera escala inicia en cantidad 1.</p></div>{businessId && <ProductPicker businessId={businessId} selectedProductIds={new Set(createItems.map(item=>item.productId))} disabled={create.isPending} label="Producto" resultsMode="inline" onSelect={(product) => setCreateItem({ ...createItem, productId: product.productId, productCode: product.productCode, productName: product.productName, amount: product.saleUnitPrice ?? 0, minimumQuantity: 1 })} />}{createItem.productId && <div className="grid gap-3 rounded-xl border bg-background p-3 sm:grid-cols-[1fr_170px_150px_auto] sm:items-end"><div><Label>Producto</Label><p className="mt-2 font-medium">{createItem.productName}</p><p className="text-xs text-muted-foreground">{createItem.productCode || "Sin código"}</p></div><div className="space-y-2"><Label>Precio</Label><FormattedNumberInput kind="currency" value={createItem.amount} invalid={createItem.amount <= 0} onValueChange={(value) => setCreateItem({ ...createItem, amount: value ?? 0 })} /></div><div className="space-y-2"><Label>Desde cantidad</Label><FormattedNumberInput value={createItem.minimumQuantity} invalid={createItem.minimumQuantity <= 0} onValueChange={(value) => setCreateItem({ ...createItem, minimumQuantity: value ?? 0 })} /></div><Button type="button" variant="secondary" disabled={createItem.amount <= 0 || createItem.minimumQuantity <= 0} onClick={addCreateItem}>Agregar precio</Button></div>}{createItems.length > 0 && <div className="overflow-hidden rounded-xl border bg-background"><table className="w-full text-sm"><thead className="bg-muted/60"><tr><th className="px-3 py-2 text-left">Producto</th><th className="px-3 py-2 text-right">Desde</th><th className="px-3 py-2 text-right">Precio</th><th className="w-12" /></tr></thead><tbody>{createItems.map((item, index) => <tr key={`${item.productId}-${item.minimumQuantity}`} className="border-t"><td className="px-3 py-2"><b>{item.productName}</b><small className="block text-muted-foreground">{item.productCode}</small></td><td className="px-3 py-2 text-right">{item.minimumQuantity}</td><td className="px-3 py-2 text-right font-medium">{formatCurrency(item.amount)}</td><td><Button type="button" size="icon" variant="ghost" aria-label={`Eliminar precio de ${item.productName}`} onClick={() => setCreateItems((current) => current.filter((_, currentIndex) => currentIndex !== index))}><Trash2 className="h-4 w-4 text-destructive" /></Button></td></tr>)}</tbody></table></div>}</div>}
        </div>
        <DialogFooter><Button type="button" variant="outline" onClick={() => setCreateOpen(false)}>Cancelar</Button><Button disabled={!name.trim() || create.isPending || !validChannelValue(channelStrategy, channelValue)} onClick={() => create.mutate()}>{create.isPending ? "Guardando…" : `Crear canal${channelStrategy === "TieredProductPrice" && createItems.length ? ` · ${createItems.length} precios` : ""}`}</Button></DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog open={Boolean(selected) && detailOpen} onOpenChange={(open) => { setDetailOpen(open); if (!open) { setSelected(null); setEditingChannel(false); } }}>
      <DialogContent className="max-h-[92dvh] max-w-5xl overflow-y-auto">
        {selected && <>
          <DialogHeader>
            <div className="flex items-start justify-between gap-3">
              <div className="flex items-start gap-3"><span className="grid h-11 w-11 place-items-center rounded-xl bg-primary/10 text-primary"><Radio className="h-5 w-5" /></span><div><DialogTitle>{editingChannel ? "Editar canal de precios" : selected.name}</DialogTitle><DialogDescription>{selected.code} · {selected.customerCount} cliente(s) · {selected.isActive ? "Activo" : "Inactivo"}</DialogDescription></div></div>
              {!editingChannel && canManage && <Button type="button" onClick={() => setEditingChannel(true)}><Pencil className="mr-2 h-4 w-4" />Editar canal</Button>}
            </div>
          </DialogHeader>
          <section className="space-y-4 rounded-2xl border bg-muted/15 p-5">
            <div className="space-y-2"><Label>Nombre *</Label><Input value={name} disabled={!editingChannel} onChange={(event) => setName(event.target.value)} maxLength={120} /></div>
            <div className="space-y-3"><Label>Modo de precio</Label><div className="grid gap-2 sm:grid-cols-2">{channelStrategies.map((mode) => <Button key={mode.value} type="button" disabled={!editingChannel} variant={channelStrategy === mode.value ? "default" : "outline"} className="h-auto justify-start whitespace-normal py-3 text-left" onClick={() => { setChannelStrategy(mode.value); setChannelValue(0); }}>{mode.label}</Button>)}</div>{requiresChannelValue(channelStrategy) && <div className="space-y-2"><Label>{channelValueLabel(channelStrategy)}</Label><FormattedNumberInput disabled={!editingChannel} kind="percent" allowNegative={allowsNegativeChannelValue(channelStrategy)} value={channelValue} invalid={!validChannelValue(channelStrategy, channelValue)} onValueChange={(value) => setChannelValue(value ?? 0)} /></div>}<p className="text-xs text-muted-foreground">{channelStrategyHelp(channelStrategy)}</p></div>
          </section>
          {channelStrategy === "TieredProductPrice" && <section className="space-y-4 rounded-2xl border bg-muted/15 p-4">
            <div className="flex items-center justify-between gap-3"><div><h3 className="font-semibold">Productos y precios por cantidad</h3><p className="text-sm text-muted-foreground">Un producto puede tener varias escalas por cantidad.</p></div>{editingChannel && canManage && <Button onClick={() => { setDetailOpen(false); setDraft(emptyDraft()); }}><Plus className="mr-2 h-4 w-4" />Agregar producto</Button>}</div>
            <div className="overflow-hidden rounded-xl border bg-background"><table className="w-full text-sm"><thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-4 py-3 text-left">Producto</th><th className="px-4 py-3 text-right">Desde</th><th className="px-4 py-3 text-right">Precio</th><th className="px-4 py-3 text-left">Vigencia</th>{editingChannel && <th className="w-24" />}</tr></thead><tbody>
              {(items.data ?? []).map((item) => <tr key={item.productId + "-" + item.minimumQuantity} className="border-t align-middle"><td className="px-4 py-3"><b className="block">{item.productName}</b><small className="text-muted-foreground">{item.productCode || "Sin código"}</small></td><td className="px-4 py-3 text-right tabular-nums">{item.minimumQuantity}</td><td className="px-4 py-3 text-right"><span className={item.excluded ? "text-destructive" : "font-medium"}>{item.excluded ? "Excluido" : formatCurrency(item.amount)}</span></td><td className="px-4 py-3 text-muted-foreground"><CalendarClock className="mr-1 inline h-3.5 w-3.5" />{new Date(item.validFrom).toLocaleDateString("es-CO")}{item.validUntil ? " – " + new Date(item.validUntil).toLocaleDateString("es-CO") : " – Sin vencimiento"}</td>{editingChannel && <td className="px-2 py-3"><div className="flex justify-end"><Button size="icon" variant="ghost" aria-label={"Editar " + item.productName} onClick={() => { setDetailOpen(false); setDraft(fromItem(item)); }}><Pencil className="h-4 w-4" /></Button><Button size="icon" variant="ghost" className="text-destructive" aria-label={"Retirar " + item.productName} onClick={() => { setDetailOpen(false); setDeleteItem(item); }}><Trash2 className="h-4 w-4" /></Button></div></td>}</tr>)}
              {!items.isLoading && (items.data ?? []).length === 0 && <tr><td colSpan={editingChannel ? 5 : 4} className="p-10 text-center text-muted-foreground">Sin productos configurados</td></tr>}
            </tbody></table></div>
          </section>}
          <DialogFooter>
            <Button variant="outline" onClick={() => { if (editingChannel) { setEditingChannel(false); setName(selected.name); setChannelStrategy(selected.strategy); setChannelValue(selected.value ?? 0); } else { setDetailOpen(false); setSelected(null); } }}>{editingChannel ? "Cancelar" : "Cerrar"}</Button>
            {editingChannel && <Button disabled={saveChannel.isPending || !name.trim() || !validChannelValue(channelStrategy, channelValue)} onClick={() => saveChannel.mutate()}>{saveChannel.isPending ? "Guardando…" : "Guardar canal"}</Button>}
          </DialogFooter>
        </>}
      </DialogContent>
    </Dialog>

    <PriceItemDialog segment={selected?.strategy === "TieredProductPrice" ? selected : null} draft={draft} onChange={(value) => { setDraft(value); if (!value && selected) setDetailOpen(true); }} onSave={(value) => saveItem.mutate(value)} saving={saveItem.isPending} />

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
        <DialogHeader><DialogTitle>{isNew ? "Agregar producto" : "Editar condición"}</DialogTitle><DialogDescription>Define el precio y la cantidad desde la cual aplica.</DialogDescription></DialogHeader>
        <div className="space-y-4">
          {isNew ? <div className="space-y-2">
            {businessId && <ProductPicker businessId={businessId} selectedProductIds={new Set(draft.productId ? [draft.productId] : [])} disabled={saving} label="Producto *" onSelect={(product) => onChange({ ...draft, productId: product.productId, productCode: product.productCode, productName: product.productName, amount: product.saleUnitPrice ?? 0 })} />}
            {draft.productId && <p className="flex items-center gap-2 text-sm text-primary"><Check className="h-4 w-4" />{draft.productName}</p>}
          </div> : <div className="rounded-xl border bg-muted/20 p-3"><b>{draft.productName}</b><p className="text-xs text-muted-foreground">{draft.productCode}</p></div>}
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2"><Label>Precio *</Label><FormattedNumberInput kind="currency" value={draft.amount} invalid={draft.amount <= 0} onValueChange={(value) => onChange({ ...draft, amount: value ?? 0 })} /></div>
            <div className="space-y-2"><Label>Cantidad mínima *</Label><FormattedNumberInput value={draft.minimumQuantity} invalid={draft.minimumQuantity <= 0} onValueChange={(value) => onChange({ ...draft, minimumQuantity: value ?? 0 })} /></div>
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
const channelStrategies: Array<{ value: PriceChannelStrategy; label: string }> = [
  { value: "TieredProductPrice", label: "Precios por producto y cantidad" },
  { value: "PercentageOverBasePrice", label: "% sobre precio público" },
  { value: "PercentageBelowBasePrice", label: "Descuento sobre precio público" },
  { value: "PercentageOverAverageCost", label: "% sobre costo promedio" },
  { value: "FixedMarginOverAverageCost", label: "Margen sobre costo promedio" },
  { value: "SellAtAverageCost", label: "Vender al costo promedio" },
];
function channelStrategyLabel(value: PriceChannelStrategy | null) { return channelStrategies.find((item) => item.value === value)?.label ?? "Sin configurar"; }
function requiresChannelValue(value: PriceChannelStrategy) { return value === "PercentageOverBasePrice" || value === "PercentageBelowBasePrice" || value === "PercentageOverAverageCost" || value === "FixedMarginOverAverageCost"; }
function allowsNegativeChannelValue(value: PriceChannelStrategy) { return value === "PercentageOverBasePrice"; }
function validChannelValue(strategy: PriceChannelStrategy, value: number) { return !requiresChannelValue(strategy) || (strategy === "FixedMarginOverAverageCost" ? value >= 0 && value < 100 : strategy === "PercentageBelowBasePrice" ? value >= 0 && value <= 100 : strategy === "PercentageOverAverageCost" ? value >= 0 && value <= 1000 : value >= -100 && value <= 1000); }
function channelValueLabel(strategy: PriceChannelStrategy) { return strategy === "FixedMarginOverAverageCost" ? "Margen objetivo (%)" : strategy === "PercentageBelowBasePrice" ? "Descuento (%)" : "Variación (%) — usa negativo para descuento"; }
function channelStrategyHelp(strategy: PriceChannelStrategy) { return ({ TieredProductPrice: "Define precios por producto y escalas desde cualquier cantidad; si no hay una condición aplicable se usa el precio público.", PercentageOverBasePrice: "Aumenta o reduce el precio público vigente; acepta valores negativos.", PercentageBelowBasePrice: "Descuenta el porcentaje indicado del precio público vigente.", PercentageOverAverageCost: "Aumenta el costo promedio vigente; no permite valores negativos.", FixedMarginOverAverageCost: "Calcula el precio necesario para conservar el margen indicado sobre el costo promedio.", SellAtAverageCost: "Vende al costo promedio vigente del inventario." } satisfies Record<PriceChannelStrategy,string>)[strategy]; }
