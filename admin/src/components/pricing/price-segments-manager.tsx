"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Check, FileText, Pencil, Plus, Radio, Search, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { ReportViewer } from "@/components/reports/report-viewer";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ProductPicker } from "@/components/products/product-picker";
import { PriceChannelExclusionDraftEditor, PriceChannelExclusions, type DraftPriceChannelExclusion } from "@/components/pricing/price-channel-exclusions";
import { marginFromCostAndGrossSale, previewPriceChannelSalePrice } from "@/lib/product-pricing-calculator";
import { formatCurrency } from "@/lib/utils";
import { priceSegmentsApi, type PriceChannelItemSyncSelection, type PriceChannelProductReport, type PriceChannelStrategy, type PriceSegmentItem, type PriceSegmentSummary } from "@/services/api/price-segments";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

type ItemDraft = {
  draftId: string;
  productId: string;
  productCode: string;
  productName: string;
  amount: number;
  minimumQuantity: number;
  targetMarginPercent: number | null;
  autoUpdatePrice: boolean;
  costBasisAmount: number | null;
  salesTaxRate: number;
  inputMode: "Margin" | "SalePrice";
};

type CreateProductDraft = Pick<ItemDraft, "productId" | "productCode" | "productName">;
type ListedPriceItem = PriceSegmentItem & { draft?: ItemDraft };

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
  const [draft, setDraft] = useState<ItemDraft[] | null>(null);
  const [editedProducts, setEditedProducts] = useState<Record<string, ItemDraft[]>>({});
  const [name, setName] = useState("");
  const [channelStrategy, setChannelStrategy] = useState<PriceChannelStrategy>("TieredProductPrice");
  const [channelValue, setChannelValue] = useState(0);
  const [createSyncPrice, setCreateSyncPrice] = useState(false);
  const [itemSyncChanges, setItemSyncChanges] = useState<Record<string, PriceChannelItemSyncSelection>>({});
  const [syncPriceChange, setSyncPriceChange] = useState<boolean | null>(null);
  const [createItems, setCreateItems] = useState<ItemDraft[]>([]);
  const [createProduct, setCreateProduct] = useState<CreateProductDraft | null>(null);
  const [createProductPrices, setCreateProductPrices] = useState<ItemDraft[]>([]);
  const [createExclusions, setCreateExclusions] = useState<DraftPriceChannelExclusion[]>([]);
  const [report, setReport] = useState<PriceChannelProductReport | null>(null);
  const [loadingReportId, setLoadingReportId] = useState<string | null>(null);

  const segments = useQuery({ queryKey: ["price-segments", businessId], queryFn: priceSegmentsApi.list, enabled: !!businessId });
  const items = useQuery({
    queryKey: ["price-segments", businessId, selected?.id],
    queryFn: () => priceSegmentsApi.items(selected!.id),
    enabled: Boolean(selected && channelStrategy === "TieredProductPrice" && (detailOpen || draft)),
  });

  const storedSyncPrice = Boolean(items.data?.length && items.data.every((item) => item.autoUpdatePrice));
  const storedItems = items.data ?? [];
  const listedItems: ListedPriceItem[] = [
    ...storedItems.filter(item => !(item.productId in editedProducts)),
    ...Object.values(editedProducts).flat().map(item => ({ ...item, draft: item, currencyCode: "COP" })),
  ];
  const storedByKey = new Map(storedItems.map(item => [itemSyncKey(item), item]));
  const listedByKey = new Map(listedItems.map(item => [itemSyncKey(item), item]));
  const changedDrafts = listedItems.flatMap(item => {
    const saved = storedByKey.get(itemSyncKey(item));
    return item.draft && tierPriceChanged(item.draft, saved) ? [item.draft] : [];
  });
  const changedKeys = new Set(changedDrafts.map(itemSyncKey));
  const retiredItems = storedItems.filter(item => !listedByKey.has(itemSyncKey(item)) || changedKeys.has(itemSyncKey(item)));
  const itemSync = (item: PriceSegmentItem) => itemSyncChanges[itemSyncKey(item)]?.autoUpdatePrice
    ?? syncPriceChange ?? item.autoUpdatePrice;
  const listedItemSync = (item: ListedPriceItem) => item.draft ? item.autoUpdatePrice : itemSync(item);
  const syncPrice = listedItems.length
    ? listedItems.every(listedItemSync) ? true : listedItems.some(listedItemSync) ? "indeterminate" as const : false
    : false;
  const createSyncState = createItems.length
    ? createItems.every((item) => item.autoUpdatePrice) ? true
      : createItems.some((item) => item.autoUpdatePrice) ? "indeterminate" as const : false
    : createSyncPrice;
  const repeatedCreateQuantities = repeatedQuantities(createProductPrices);

  function changeItemSync(item: ListedPriceItem, checked: boolean) {
    if (item.draft) setEditedProducts(current => ({ ...current, [item.productId]: current[item.productId].map(
      row => row.minimumQuantity === item.minimumQuantity ? { ...row, autoUpdatePrice: checked } : row) }));
    else setItemSyncChanges(current => ({ ...current, [itemSyncKey(item)]: { productId: item.productId,
      minimumQuantity: item.minimumQuantity, autoUpdatePrice: checked } }));
  }

  function closeItemEditor() {
    setDraft(null);
    if (selected) setDetailOpen(true);
  }

  function saveItemEditor(rows: ItemDraft[]) {
    if (!draft) return;
    const productId = rows[0].productId;
    const retained = draft[0].productId ? [] : listedItems.filter(item => item.productId === productId)
      .map(item => item.draft ?? fromItem({ ...item, autoUpdatePrice: itemSync(item) }));
    setEditedProducts(current => ({ ...current, [productId]: [...retained, ...rows] }));
    closeItemEditor();
  }

  function removeScale(item: ListedPriceItem) {
    const kept = listedItems.filter(row => row.productId === item.productId && row.minimumQuantity !== item.minimumQuantity)
      .map(row => row.draft ?? fromItem({ ...row, autoUpdatePrice: itemSync(row) }));
    setEditedProducts(current => ({ ...current, [item.productId]: kept }));
  }

  const create = useMutation({
    mutationFn: async () => {
      return priceSegmentsApi.create({
        name: name.trim(),
        channelStrategy,
        channelValue: requiresChannelValue(channelStrategy) ? channelValue : null,
        items: channelStrategy === "TieredProductPrice" ? createItems.map(tierRequest) : undefined,
        exclusions: createExclusions.map((item) => ({ scopeType: item.scopeType, scopeId: item.scopeId })),
      });
    },
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["price-segments"] });
      setCreateOpen(false);
      setName("");
      setChannelStrategy("TieredProductPrice");
      setChannelValue(0);
      setCreateItems([]);
      setCreateProduct(null);
      setCreateProductPrices([]);
      setCreateExclusions([]);
      toast.success("Canal de precios creado.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible crear el segmento."),
  });

  const saveChannel = useMutation({
    mutationFn: async () => {
      if (!selected) throw new Error("Selecciona un canal.");
      const value = requiresChannelValue(channelStrategy) ? channelValue : null;
      const tiered = channelStrategy === "TieredProductPrice";
      const newItems = tiered ? changedDrafts : [];
      const retired = tiered ? retiredItems.map(({ productId, minimumQuantity }) => ({ productId, minimumQuantity })) : [];
      const retiredKeys = new Set(retired.map(itemSyncKey));
      const overrides = tiered ? storedItems.flatMap(item => {
        const key = itemSyncKey(item);
        const next = listedByKey.get(key);
        if (!next || retiredKeys.has(key)) return [];
        const autoUpdatePrice = listedItemSync(next);
        return autoUpdatePrice === (syncPriceChange ?? item.autoUpdatePrice) ? [] :
          [{ productId: item.productId, minimumQuantity: item.minimumQuantity, autoUpdatePrice }];
      }) : [];
      const result = await priceSegmentsApi.saveChannelSettings(selected.id, name.trim(), channelStrategy, value,
        tiered ? syncPriceChange : null, overrides,
        newItems.length ? newItems.map(tierRequest) : undefined,
        retired.length ? retired : undefined);
      if ((newItems.length && result?.addedItems?.length !== newItems.length) ||
          (retired.length && result?.productCount == null))
        throw new Error("No se pudieron confirmar todas las escalas. Actualiza la aplicación y vuelve a revisar el canal.");
      const draftsByKey = new Map(newItems.map(item => [itemSyncKey(item), item]));
      const addedItems = (result?.addedItems ?? []).map(item => {
        const source = draftsByKey.get(itemSyncKey(item));
        if (!source) throw new Error("La respuesta contiene una escala distinta de las seleccionadas. Revisa el canal.");
        return { ...item, productCode: source.productCode, productName: source.productName, currencyCode: "COP" };
      });
      return { autoUpdatePrice: result?.autoUpdatePrice ?? null, itemSync: result?.itemSync ?? [], addedItems, retiredKeys,
        productCount: result?.productCount, businessId, id: selected.id, name: name.trim(), strategy: channelStrategy, value };
    },
    onSuccess: (result) => {
      const update = (current: PriceSegmentSummary) => current.id === result.id
        ? { ...current, name: result.name, strategy: result.strategy, value: result.value, productCount: result.productCount ?? current.productCount } : current;
      client.setQueryData<PriceSegmentSummary[]>(["price-segments", result.businessId], (current) => current?.map(update));
      setSelected((current) => current ? update(current) : current);
      const autoUpdatePrice = result.autoUpdatePrice;
      if (autoUpdatePrice !== null || result.itemSync.length > 0 || result.addedItems.length > 0 || result.retiredKeys.size > 0) {
        const overrides = new Map(result.itemSync.map((item) => [itemSyncKey(item), item.autoUpdatePrice]));
        client.setQueryData<PriceSegmentItem[]>(["price-segments", result.businessId, result.id], (current) =>
          current && [...current.filter(item => !result.retiredKeys.has(itemSyncKey(item)))
            .map((item) => ({ ...item, autoUpdatePrice: overrides.get(itemSyncKey(item)) ?? autoUpdatePrice ?? item.autoUpdatePrice })),
            ...result.addedItems].sort((left, right) => left.productName.localeCompare(right.productName, "es-CO") || left.minimumQuantity - right.minimumQuantity));
      }
      setEditedProducts({});
      setItemSyncChanges({});
      setSyncPriceChange(null);
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

  async function editCreateProduct(product: CreateProductDraft, defaultAmount: number) {
    const existing = createItems.filter((item) => item.productId === product.productId);
    let costBasisAmount: number | null = existing[0]?.costBasisAmount ?? null;
    let salesTaxRate = existing[0]?.salesTaxRate ?? 0;
    if (costBasisAmount === null && permissions.includes("pricing.cost-basis.read")) {
      try { const cost = await priceSegmentsApi.productCost(product.productId); costBasisAmount = cost.costBasisAmount; salesTaxRate = cost.salesTaxRate; }
      catch (error) { toast.error(error instanceof Error ? error.message : "No se pudo consultar el costo del producto."); return; }
    }
    setCreateProduct(product);
    setCreateProductPrices(existing.length > 0 ? existing : [updateTierPrice({ ...emptyDraft(), ...product, autoUpdatePrice: createSyncPrice,
      costBasisAmount, salesTaxRate }, "SalePrice", defaultAmount)]);
    requestAnimationFrame(() => document.getElementById("new-channel-product-minimum-0")?.focus());
  }

  function updateCreateTier(index: number, change: Partial<ItemDraft>) {
    setCreateProductPrices((current) => current.map((item, at) =>
      at === index ? { ...item, ...change } : item));
  }

  function saveCreateProductPrices() {
    if (!createProduct || !validProductPrices(createProductPrices)) return;
    setCreateItems((current) => [...current.filter((item) => item.productId !== createProduct.productId), ...createProductPrices]);
    setCreateProduct(null);
    setCreateProductPrices([]);
    requestAnimationFrame(() => document.getElementById("new-channel-product-search")?.focus());
  }

  async function openProductPriceReport(segment: PriceSegmentSummary) {
    if (loadingReportId) return;
    setLoadingReportId(segment.id);
    try {
      const value = await priceSegmentsApi.productPriceReport(segment.id);
      setDetailOpen(false);
      setSelected(null);
      setReport(value);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible cargar la lista de precios del canal.");
    } finally {
      setLoadingReportId(null);
    }
  }

  if (report) return <div className="space-y-4">
    <Button variant="ghost" onClick={() => setReport(null)}><ArrowLeft className="mr-2 h-4 w-4" />Volver a canales</Button>
    <ReportViewer
      onClose={() => setReport(null)}
      title="Lista de precios del canal"
      description={`${report.name} · ${report.items.length.toLocaleString("es-CO")} precios efectivos${report.isActive ? "" : " · canal inactivo"}`}
      fileName={`lista-precios-${report.code.toLocaleLowerCase("es-CO")}`}
      rows={report.items.map((item) => ({ ...item, id: `${item.productId}-${item.minimumQuantity}` }))}
      columns={[
        { key: "productCode", label: "Código interno" },
        { key: "productName", label: "Producto" },
        { key: "minimumQuantity", label: "Cantidad desde", align: "right" },
        { key: "publicAmount", label: "Precio público", align: "right", format: (value, row) => formatCurrency(Number(value), String(row.currencyCode || "COP")) },
        { key: "channelAmount", label: "Precio del canal", align: "right", format: (value, row) => formatCurrency(Number(value), String(row.currencyCode || "COP")) },
        { key: "priceSource", label: "Origen" },
      ]}
    />
  </div>;

  return <div className="space-y-6">
    <header className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
      <div>
        <p className="text-sm font-medium text-primary">Precios por segmento</p>
        <h1 className="text-3xl font-semibold tracking-tight">Canales de precios</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">Define una regla general o precios por producto y cantidad. Si no aplica un canal se usa el precio público.</p>
      </div>
      {canManage && <Button onClick={() => { setName(""); setChannelStrategy("TieredProductPrice"); setChannelValue(0); setCreateSyncPrice(false); setCreateItems([]); setCreateProduct(null); setCreateProductPrices([]); setCreateExclusions([]); setCreateOpen(true); }}><Plus className="mr-2 h-4 w-4" />Nuevo canal</Button>}
    </header>

    <Card>
      <CardContent className="pt-6">
        <div className="relative mb-5">
          <Search className="absolute left-3 top-3 h-4 w-4 text-muted-foreground" />
          <Input className="pl-9" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar por nombre" />
        </div>
        <div className="overflow-hidden rounded-xl border">
          <table className="w-full text-sm"><thead className="bg-muted/60 text-xs uppercase tracking-wide text-muted-foreground"><tr><th className="px-4 py-3 text-left">Nombre</th><th className="px-4 py-3 text-right">Modo</th><th className="px-4 py-3 text-right">Clientes</th><th className="px-4 py-3 text-right">Estado</th><th className="px-4 py-3 text-right">Informe</th></tr></thead><tbody>
          {filtered.map((segment) =>
            <tr key={segment.id} className="cursor-pointer border-t transition hover:bg-muted/40" onClick={() => { setSelected(segment); setEditedProducts({}); setItemSyncChanges({}); setSyncPriceChange(null); setDetailOpen(true); setEditingChannel(false); setName(segment.name); setChannelStrategy(segment.strategy ?? "TieredProductPrice"); setChannelValue(segment.value ?? 0); }}><td className="px-4 py-4 font-semibold">{segment.name}</td><td className="px-4 py-4 text-right">{channelStrategyLabel(segment.strategy)}</td><td className="px-4 py-4 text-right tabular-nums">{segment.customerCount}</td><td className="px-4 py-4 text-right"><Badge variant={segment.isActive ? "secondary" : "outline"}>{segment.isActive ? "Activo" : "Inactivo"}</Badge></td><td className="px-4 py-3 text-right"><Button type="button" size="sm" variant="outline" disabled={Boolean(loadingReportId)} onClick={(event) => { event.stopPropagation(); void openProductPriceReport(segment); }}><FileText className="mr-2 h-4 w-4" />{loadingReportId === segment.id ? "Cargando…" : "Lista de precios"}</Button></td></tr>)}
          {!segments.isLoading && filtered.length === 0 && <tr><td colSpan={5} className="p-12 text-center text-muted-foreground">Sin datos</td></tr>}
          </tbody></table>
        </div>
      </CardContent>
    </Card>

    <Dialog open={createOpen} onOpenChange={setCreateOpen}>
      <DialogContent className="max-h-[92dvh] max-w-4xl grid-cols-[minmax(0,1fr)] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Nuevo canal de precios</DialogTitle>
          <DialogDescription>Elige el modo y configura el canal completo antes de guardarlo.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-2"><Label>Nombre *</Label><Input value={name} onChange={(event) => setName(event.target.value)} placeholder="Mayoristas" maxLength={120} /></div>
          <div className="space-y-3"><Label>Modo de precio</Label><div className="grid gap-2 sm:grid-cols-2">{channelStrategies.map((mode) => <Button key={mode.value} type="button" variant={channelStrategy === mode.value ? "default" : "outline"} className="h-auto justify-start whitespace-normal py-3 text-left" onClick={() => { setChannelStrategy(mode.value); setChannelValue(0); }}>{mode.label}</Button>)}</div>{requiresChannelValue(channelStrategy) && <div className="space-y-2"><Label>{channelValueLabel(channelStrategy)}</Label><FormattedNumberInput kind="percent" allowNegative={allowsNegativeChannelValue(channelStrategy)} value={channelValue} invalid={!validChannelValue(channelStrategy, channelValue)} onValueChange={(value) => setChannelValue(value ?? 0)} /></div>}<p className="text-xs text-muted-foreground">{channelStrategyHelp(channelStrategy)}</p></div>
          {channelStrategy === "TieredProductPrice" && <div className="space-y-4 rounded-2xl border bg-muted/15 p-4">
            <div><h3 className="font-semibold">Productos y precios por cantidad</h3><p className="text-sm text-muted-foreground">Configura el precio y el margen de cada escala.</p></div>
            <SynchronizePrices checked={createSyncState} disabled={create.isPending} onChange={(checked) => {
              setCreateSyncPrice(checked);
              setCreateItems((current) => current.map((item) => ({ ...item, autoUpdatePrice: checked })));
            }} />
            {businessId && <ProductPicker inputId="new-channel-product-search" businessId={businessId} selectedProductIds={new Set(createItems.map(item=>item.productId))} disabled={create.isPending} label="Producto" showAddButton={false} onSelect={(product) => { void editCreateProduct({ productId: product.productId, productCode: product.productCode, productName: product.productName }, product.saleUnitPrice ?? 0); }} />}
            <GroupedTiers items={createItems} emptyMessage="Selecciona un producto para configurar sus precios."
              editing disabled={create.isPending} isSync={item => item.autoUpdatePrice}
              onSync={(item, checked) => setCreateItems(current => current.map(row =>
                itemSyncKey(row) === itemSyncKey(item) ? { ...row, autoUpdatePrice: checked } : row))}
              onEdit={item => { void editCreateProduct(item, item.amount); }}
              onRemove={item => setCreateItems(current => current.filter(row => itemSyncKey(row) !== itemSyncKey(item)))}/>
          </div>}
          {businessId && <PriceChannelExclusionDraftEditor businessId={businessId} value={createExclusions} onChange={setCreateExclusions} disabled={create.isPending} />}
        </div>
        <DialogFooter><Button type="button" variant="outline" onClick={() => setCreateOpen(false)}>Cancelar</Button><Button disabled={!name.trim() || create.isPending || !validChannelValue(channelStrategy, channelValue)} onClick={() => create.mutate()}>{create.isPending ? "Guardando…" : `Crear canal${channelStrategy === "TieredProductPrice" && createItems.length ? ` · ${createItems.length} precios` : ""}`}</Button></DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog open={Boolean(createProduct)} onOpenChange={(open) => { if (!open) { setCreateProduct(null); setCreateProductPrices([]); } }}>
      <DialogContent className="max-h-[92dvh] max-w-4xl grid-cols-[minmax(0,1fr)] overflow-y-auto">
        {createProduct && <>
          <DialogHeader><DialogTitle>Precios por cantidad</DialogTitle><DialogDescription>{createProduct.productName} · {createProduct.productCode || "Sin código"}</DialogDescription></DialogHeader>
          <div className="space-y-3">
            {createProductPrices.map((item, index) => <TierPriceFields key={item.draftId} item={item} index={index}
              quantityId={`new-channel-product-minimum-${index}`} invalidQuantity={repeatedCreateQuantities.has(item.minimumQuantity)}
              onChange={value => updateCreateTier(index, value)}
              onRemove={createProductPrices.length > 1 ? () => setCreateProductPrices(current => current.filter(row => row.draftId !== item.draftId)) : undefined}/>)}
            {repeatedCreateQuantities.size > 0 && <p role="alert" className="text-sm text-destructive">Cada cantidad debe ser única para este producto.</p>}
            <TierCost item={createProductPrices[0]}/>
            <Button type="button" variant="outline" onClick={() => setCreateProductPrices(current => [...current, {
              ...current[current.length - 1], draftId: crypto.randomUUID(), minimumQuantity: nextTierQuantity(current), autoUpdatePrice: createSyncPrice,
            }])}><Plus className="mr-2 h-4 w-4"/>Agregar otro precio</Button>
          </div>
          <DialogFooter><Button type="button" variant="outline" onClick={() => { setCreateProduct(null); setCreateProductPrices([]); }}>Cancelar</Button><Button type="button" disabled={!validProductPrices(createProductPrices)} onClick={saveCreateProductPrices}>Agregar precios</Button></DialogFooter>
        </>}
      </DialogContent>
    </Dialog>

    <Dialog open={Boolean(selected) && detailOpen} onOpenChange={(open) => { if (saveChannel.isPending) return; setDetailOpen(open); if (!open) { setSelected(null); setEditingChannel(false); setEditedProducts({}); } }}>
      <DialogContent className="max-h-[92dvh] max-w-5xl grid-cols-[minmax(0,1fr)] overflow-y-auto">
        {selected && <>
          <DialogHeader>
            <div className="flex flex-col items-start justify-between gap-3 sm:flex-row">
              <div className="flex min-w-0 items-start gap-3"><span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary"><Radio className="h-5 w-5" /></span><div className="min-w-0 break-words"><DialogTitle>{editingChannel ? "Editar canal de precios" : selected.name}</DialogTitle><DialogDescription>{selected.code} · {selected.customerCount} cliente(s) · {selected.isActive ? "Activo" : "Inactivo"}</DialogDescription></div></div>
              {!editingChannel && <div className="flex flex-wrap gap-2"><Button type="button" variant="outline" disabled={Boolean(loadingReportId)} onClick={() => void openProductPriceReport(selected)}><FileText className="mr-2 h-4 w-4" />Lista de precios</Button>{canManage && <Button type="button" onClick={() => setEditingChannel(true)}><Pencil className="mr-2 h-4 w-4" />Editar canal</Button>}</div>}
            </div>
          </DialogHeader>
          <section className="space-y-4 rounded-2xl border bg-muted/15 p-5">
            <div className="space-y-2"><Label>Nombre *</Label><Input value={name} disabled={!editingChannel || saveChannel.isPending} onChange={(event) => setName(event.target.value)} maxLength={120} /></div>
            <div className="space-y-3"><Label>Modo de precio</Label><div className="grid gap-2 sm:grid-cols-2">{channelStrategies.map((mode) => <Button key={mode.value} type="button" disabled={!editingChannel || saveChannel.isPending} variant={channelStrategy === mode.value ? "default" : "outline"} className="h-auto justify-start whitespace-normal py-3 text-left" onClick={() => { setChannelStrategy(mode.value); setChannelValue(0); }}>{mode.label}</Button>)}</div>{requiresChannelValue(channelStrategy) && <div className="space-y-2"><Label>{channelValueLabel(channelStrategy)}</Label><FormattedNumberInput disabled={!editingChannel || saveChannel.isPending} kind="percent" allowNegative={allowsNegativeChannelValue(channelStrategy)} value={channelValue} invalid={!validChannelValue(channelStrategy, channelValue)} onValueChange={(value) => setChannelValue(value ?? 0)} /></div>}<p className="text-xs text-muted-foreground">{channelStrategyHelp(channelStrategy)}</p></div>
          </section>
          {channelStrategy === "TieredProductPrice" && <section className="space-y-4 rounded-2xl border bg-muted/15 p-4">
            <div className="flex flex-col items-start justify-between gap-3 sm:flex-row sm:items-center"><div><h3 className="font-semibold">Productos y precios por cantidad</h3><p className="text-sm text-muted-foreground">Un producto puede tener varias escalas por cantidad.</p></div>{editingChannel && canManage && <Button disabled={items.isFetching || items.isError || saveChannel.isPending || changedDrafts.length >= 500} onClick={() => { setDetailOpen(false); setDraft([{ ...emptyDraft(), autoUpdatePrice: syncPriceChange ?? storedSyncPrice }]); }}><Plus className="mr-2 h-4 w-4" />Agregar producto</Button>}</div>
            <SynchronizePrices checked={syncPrice} disabled={!editingChannel || items.isLoading || saveChannel.isPending || !listedItems.length} onChange={(checked) => { setSyncPriceChange(checked); setItemSyncChanges({}); setEditedProducts(current => Object.fromEntries(Object.entries(current).map(([productId, rows]) => [productId, rows.map(item => ({ ...item, autoUpdatePrice: checked }))]))); }} />
            {items.isError && <p role="alert" className="text-sm text-destructive">
              No se pudieron consultar los precios del canal.
              <Button type="button" variant="link" onClick={() => void items.refetch()}>Reintentar</Button>
            </p>}
            {(!items.isError || listedItems.length > 0) && <GroupedTiers items={listedItems} emptyMessage={items.isLoading ? "Cargando precios…" : "Sin productos configurados"}
              editing={editingChannel && canManage} disabled={saveChannel.isPending || items.isError}
              isSync={listedItemSync} onSync={changeItemSync} isPending={item => changedKeys.has(itemSyncKey(item))}
              onEdit={item => {
                setDetailOpen(false);
                setDraft(listedItems.filter(row => row.productId === item.productId)
                  .sort((left, right) => left.minimumQuantity - right.minimumQuantity)
                  .map(row => row.draft ?? fromItem({ ...row, autoUpdatePrice: listedItemSync(row) })));
              }}
              onRemove={removeScale}/>}
          </section>}
          {businessId && <PriceChannelExclusions channelId={selected.id} businessId={businessId} canManage={editingChannel && canManage} />}
          <DialogFooter>
            <Button variant="outline" disabled={saveChannel.isPending} onClick={() => { if (editingChannel) { setEditingChannel(false); setName(selected.name); setChannelStrategy(selected.strategy); setChannelValue(selected.value ?? 0); setItemSyncChanges({}); setSyncPriceChange(null); setEditedProducts({}); } else { setDetailOpen(false); setSelected(null); } }}>{editingChannel ? "Cancelar" : "Cerrar"}</Button>
            {editingChannel && <Button disabled={saveChannel.isPending || !name.trim() || !validChannelValue(channelStrategy, channelValue)
              || channelStrategy === "TieredProductPrice" && (items.isError || items.isFetching || changedDrafts.length > 500
                || retiredItems.length > 500 || changedDrafts.some(item => !validTierPrice(item)))}
              onClick={() => saveChannel.mutate()}>{saveChannel.isPending ? "Guardando…" : "Guardar canal"}</Button>}
          </DialogFooter>
        </>}
      </DialogContent>
    </Dialog>

    {draft && channelStrategy === "TieredProductPrice" && <PriceItemDialog initial={draft} listedItems={listedItems}
      onClose={closeItemEditor} onSave={saveItemEditor}/>}

  </div>;
}

function itemSyncKey(item: { productId: string; minimumQuantity: number }) {
  return `${item.productId}:${item.minimumQuantity}`;
}

function GroupedTiers<T extends Pick<ItemDraft, "productId" | "productName" | "productCode" | "minimumQuantity" | "amount" | "targetMarginPercent">>({
  items, emptyMessage, editing, disabled, isSync, onSync, onEdit, onRemove, isPending,
}: {
  items: T[]; emptyMessage: string; editing: boolean; disabled: boolean;
  isSync: (item: T) => boolean; onSync: (item: T, checked: boolean) => void;
  onEdit: (item: T) => void; onRemove: (item: T) => void; isPending?: (item: T) => boolean;
}) {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const group = groups.get(item.productId);
    if (group) group.push(item);
    else groups.set(item.productId, [item]);
  }
  if (!groups.size) return <p className="rounded-xl border bg-background p-6 text-center text-sm text-muted-foreground">{emptyMessage}</p>;

  return <ul className="space-y-3">
    {[...groups.values()].sort(([left], [right]) => left.productName.localeCompare(right.productName, "es-CO")).map(group => {
      const product = group[0];
      return <li key={product.productId} className="min-w-0 overflow-hidden rounded-xl border bg-background">
        <div className="flex items-start justify-between gap-2 bg-muted/50 px-3 py-2">
          <div className="min-w-0"><h4 className="break-words text-sm font-semibold">{product.productName}</h4>
            <p className="text-xs text-muted-foreground">{product.productCode}</p></div>
          {editing && <Button type="button" size="sm" variant="outline" disabled={disabled}
            aria-label={`Editar precios de ${product.productName}`} onClick={() => onEdit(product)}>
            <Pencil className="mr-1.5 size-3.5" aria-hidden="true"/>Editar
          </Button>}
        </div>
        <table aria-label={`Escalas de ${product.productName}`} className="w-full table-fixed text-sm">
          <thead className="text-xs text-muted-foreground"><tr>
            <th className="px-2 py-2 text-left font-medium">Desde</th>
            <th className="w-[28%] px-1 py-2 text-right font-medium">Precio</th>
            <th className="px-1 py-2 text-right font-medium">Margen</th>
            <th className="w-[24%] px-1 py-2 text-center text-[11px] font-medium normal-case tracking-normal sm:text-xs">Sincronizar</th>
            {editing && <th className="w-8"><span className="sr-only">Retirar</span></th>}
          </tr></thead>
          <tbody>
            {group.sort((left, right) => left.minimumQuantity - right.minimumQuantity).map(item =>
              <tr key={item.minimumQuantity} className="border-t">
                <td className="break-words px-2 py-2 tabular-nums">{item.minimumQuantity.toLocaleString("es-CO", { maximumFractionDigits: 6 })}
                  {isPending?.(item) && <span className="block text-[11px] text-muted-foreground">Por guardar</span>}</td>
                <td className="break-words px-1 py-2 text-right font-medium tabular-nums">{formatCurrency(item.amount)}</td>
                <td className="break-words px-1 py-2 text-right tabular-nums">{item.targetMarginPercent === null ? "—" : `${item.targetMarginPercent.toLocaleString("es-CO")} %`}</td>
                <td className="px-1 py-2 text-center"><Checkbox checked={isSync(item)}
                  disabled={!editing || disabled || item.targetMarginPercent === null && !isSync(item)}
                  aria-label={`Sincronizar ${item.productName} desde ${item.minimumQuantity} unidades`}
                  onCheckedChange={checked => onSync(item, checked === true)}/></td>
                {editing && <td><Button type="button" size="icon" variant="ghost" disabled={disabled}
                  className="size-8 text-destructive" aria-label={`Retirar escala de ${item.productName} desde ${item.minimumQuantity}`}
                  onClick={() => onRemove(item)}><Trash2 className="size-4" aria-hidden="true"/></Button></td>}
              </tr>)}
          </tbody>
        </table>
      </li>;
    })}
  </ul>;
}

function SynchronizePrices({ checked, disabled, onChange }: {
  checked: boolean | "indeterminate"; disabled: boolean; onChange: (checked: boolean) => void;
}) {
  return <label className="flex items-center justify-between gap-3 rounded-xl border bg-background p-3">
    <span><b className="block text-sm">Sincronizar precio · marcar todos</b>
      <small className="text-muted-foreground">Al publicar un nuevo costo, conserva el margen de cada escala y actualiza su precio. Puedes desmarcar filas individuales. Se aplica al guardar el canal.</small></span>
    <Checkbox aria-label="Sincronizar precio de todos los productos" checked={checked} disabled={disabled}
      onCheckedChange={(value) => onChange(value === true)} />
  </label>;
}

function PriceItemDialog({ initial, listedItems, onClose, onSave }: {
  initial: ItemDraft[];
  listedItems: ListedPriceItem[];
  onClose: () => void;
  onSave: (rows: ItemDraft[]) => void;
}) {
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const canReadCost = useAuthStore(state => state.user?.permissions.includes("pricing.cost-basis.read") ?? false);
  const adding = !initial[0].productId;
  const [drafts, setDrafts] = useState(initial);
  const first = drafts[0];
  const cost = useQuery({
    queryKey: ["price-segment-cost", businessId, first.productId],
    queryFn: () => priceSegmentsApi.productCost(first.productId),
    enabled: adding && canReadCost && Boolean(first.productId),
    staleTime: 0, refetchOnWindowFocus: false, refetchOnReconnect: false,
  });
  const awaitingCost = adding && canReadCost && Boolean(first.productId) && (cost.isPending || cost.isFetching || cost.isError);
  const productCost = adding && canReadCost ? cost.data : undefined;
  const rows = drafts.map(item => item.costBasisAmount === null && productCost ? updateTierPrice({
    ...item, costBasisAmount: productCost.costBasisAmount, salesTaxRate: productCost.salesTaxRate,
  }, "SalePrice", item.amount) : item);

  const occupied = new Set(adding ? listedItems.filter(item => item.productId === first.productId)
    .map(item => item.minimumQuantity) : []);
  const invalidQuantities = repeatedQuantities(rows, occupied);
  const initialByQuantity = new Map(adding ? [] : initial.map(item => [item.minimumQuantity, item]));
  const invalidPrices = rows.some(item => tierPriceChanged(item, initialByQuantity.get(item.minimumQuantity)) && !validTierPrice(item));
  return <Dialog open onOpenChange={open => { if (!open) onClose(); }}>
    <DialogContent className="max-h-[92dvh] max-w-3xl grid-cols-[minmax(0,1fr)] overflow-y-auto">
      <DialogHeader>
        <DialogTitle>{adding ? "Agregar producto" : "Editar precios por cantidad"}</DialogTitle>
        <DialogDescription>{adding ? "Selecciona un producto y define sus precios por cantidad." : "Los cambios se aplican al guardar el canal."}</DialogDescription>
      </DialogHeader>
      {adding && businessId && <ProductPicker businessId={businessId} selectedProductIds={new Set(first.productId ? [first.productId] : [])}
        label="Producto *" onSelect={product => {
          const existing = listedItems.filter(item => item.productId === product.productId);
          setDrafts([{ ...emptyDraft(), productId: product.productId, productCode: product.productCode,
            productName: product.productName, amount: product.saleUnitPrice ?? 0,
            autoUpdatePrice: initial[0].autoUpdatePrice, minimumQuantity: nextTierQuantity(existing) }]);
        }}/>}
      {first.productId && <>
        <p className="flex min-w-0 items-center gap-2 text-sm font-medium"><Check aria-hidden="true" className="size-4 shrink-0 text-primary"/><span className="break-words">{first.productName}</span></p>
        {awaitingCost && !cost.isError && <p role="status" className="text-sm text-muted-foreground">Consultando el costo...</p>}
        {cost.isError && <p role="alert" className="text-sm text-destructive">No se pudo consultar el costo. <Button type="button" variant="link" onClick={() => void cost.refetch()}>Reintentar</Button></p>}
        <div className="space-y-3">
          {rows.map((item, index) => <TierPriceFields key={item.draftId} item={item} index={index}
            invalidQuantity={invalidQuantities.has(item.minimumQuantity)} priceDisabled={awaitingCost}
            onChange={value => setDrafts(current => current.map(row => row.draftId === value.draftId ? value : row))}
            onRemove={rows.length > 1 ? () => setDrafts(current => current.filter(row => row.draftId !== item.draftId)) : undefined}/>)}
        </div>
        {invalidQuantities.size > 0 && <p role="alert" className="text-sm text-destructive">Cada cantidad debe ser única para este producto.</p>}
        <TierCost item={rows[0]}/>
        <Button type="button" variant="outline" disabled={awaitingCost || rows.length >= 500}
          onClick={() => setDrafts([...rows, { ...rows[rows.length - 1], draftId: crypto.randomUUID(),
            minimumQuantity: nextTierQuantity([...rows, ...Array.from(occupied, minimumQuantity => ({ minimumQuantity }))]) }])}>
          <Plus aria-hidden="true" className="mr-2 size-4"/>Agregar otro precio
        </Button>
      </>}
      <DialogFooter><Button type="button" variant="outline" onClick={onClose}>Cancelar</Button>
        <Button type="button" disabled={awaitingCost || !first.productId || invalidQuantities.size > 0 ||
          invalidPrices || rows.some(item => item.minimumQuantity <= 0)} onClick={() => onSave(rows)}>
          {adding ? "Agregar precios" : "Aplicar cambios"}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

function TierPriceFields({ item, index, quantityId, priceDisabled, invalidQuantity, onChange, onRemove }: {
  item: ItemDraft; index: number; quantityId?: string; priceDisabled?: boolean;
  invalidQuantity?: boolean; onChange: (value: ItemDraft) => void; onRemove?: () => void;
}) {
  return <fieldset aria-label={`Escala ${index + 1}`} className="grid min-w-0 grid-cols-2 items-end gap-3 rounded-xl border p-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)_minmax(0,1fr)_auto]">
    <label className="space-y-2 text-sm"><span>Cantidad mínima *</span>
      <FormattedNumberInput id={quantityId} ariaLabel="Cantidad mínima *" allowNegative value={item.minimumQuantity} invalid={item.minimumQuantity <= 0 || invalidQuantity}
        onValueChange={value => onChange({ ...item, minimumQuantity: value ?? 0 })}/>
    </label>
    <label className="space-y-2 text-sm"><span>Precio *</span>
      <FormattedNumberInput ariaLabel="Precio *" kind="currency" allowNegative value={item.amount} disabled={priceDisabled}
        invalid={item.inputMode === "SalePrice" && !validTierPrice(item)}
        onValueChange={value => onChange(updateTierPrice(item, "SalePrice", value))}/>
    </label>
    <label className="space-y-2 text-sm"><span>Margen (%)</span>
      <FormattedNumberInput ariaLabel="Margen (%)" kind="percent" allowNegative disabled={priceDisabled || !item.costBasisAmount}
        value={item.targetMarginPercent ?? ""} invalid={item.inputMode === "Margin" && !validTierPrice(item)}
        onValueChange={value => onChange(updateTierPrice(item, "Margin", value))}/>
    </label>
    {onRemove && <Button type="button" size="icon" variant="ghost" className="justify-self-end" aria-label={`Eliminar escala ${index + 1}`} onClick={onRemove}><Trash2 className="size-4 text-destructive"/></Button>}
  </fieldset>;
}

function TierCost({ item }: { item: ItemDraft }) {
  return item.costBasisAmount === null ? null : <p className="text-xs text-muted-foreground">
    Costo base de la sede: {formatCurrency(item.costBasisAmount)}. El precio debe cubrir el costo más IVA.
  </p>;
}

function emptyDraft(): ItemDraft {
  return { draftId: crypto.randomUUID(), productId: "", productCode: "", productName: "", amount: 0,
    minimumQuantity: 1, targetMarginPercent: null, autoUpdatePrice: false, costBasisAmount: null, salesTaxRate: 0, inputMode: "SalePrice" };
}

function fromItem(item: PriceSegmentItem): ItemDraft {
  return { ...item, draftId: crypto.randomUUID(), inputMode: "SalePrice" };
}

function tierRequest({ productId, minimumQuantity, amount, targetMarginPercent, autoUpdatePrice, inputMode }: ItemDraft) {
  return { productId, minimumQuantity, amount, targetMarginPercent, autoUpdatePrice, inputMode };
}

function tierPriceChanged(draft: ItemDraft, saved?: { amount: number; targetMarginPercent: number | null }) {
  return !saved || draft.amount !== saved.amount ||
    draft.inputMode === "Margin" && draft.targetMarginPercent !== saved.targetMarginPercent;
}

function validTierPrice(item: ItemDraft) {
  const validMargin = item.targetMarginPercent !== null && item.targetMarginPercent >= 0 && item.targetMarginPercent < 100;
  const positiveCost = item.costBasisAmount !== null && item.costBasisAmount > 0;
  return item.amount > 0 && (item.inputMode === "Margin" ? positiveCost && validMargin
    : item.costBasisAmount === null || item.amount >= item.costBasisAmount * (1 + item.salesTaxRate / 100)) &&
    (!item.autoUpdatePrice || positiveCost && validMargin);
}

function repeatedQuantities(items: ItemDraft[], occupied = new Set<number>()) {
  const seen = new Set(occupied);
  return new Set(items.flatMap(({ minimumQuantity }) => {
    if (seen.has(minimumQuantity)) return [minimumQuantity];
    seen.add(minimumQuantity);
    return [];
  }));
}

function validProductPrices(items: ItemDraft[]) {
  return items.length > 0 && items.every(item => item.minimumQuantity > 0 && validTierPrice(item)) &&
    repeatedQuantities(items).size === 0;
}

function nextTierQuantity(items: { minimumQuantity: number }[]) {
  const used = new Set(items.map(item => item.minimumQuantity));
  let quantity = 1;
  while (used.has(quantity)) quantity++;
  return quantity;
}

function updateTierPrice(item: ItemDraft, inputMode: ItemDraft["inputMode"], value: number | null): ItemDraft {
  const cost = item.costBasisAmount;
  if (inputMode === "SalePrice") {
    const amount = value ?? 0;
    const targetMarginPercent = cost && amount > 0 ? marginFromCostAndGrossSale(cost, amount, item.salesTaxRate, 6) : null;
    return { ...item, inputMode, amount, targetMarginPercent };
  }
  const amount = cost && value !== null && value >= 0 && value <= 99.9999
    ? previewPriceChannelSalePrice(cost, value, item.salesTaxRate) : 0;
  return { ...item, inputMode, amount, targetMarginPercent: value };
}
const channelStrategies: Array<{ value: PriceChannelStrategy; label: string }> = [
  { value: "TieredProductPrice", label: "Precios por producto y cantidad" },
  { value: "PercentageOverBasePrice", label: "% sobre precio público" },
  { value: "MarginOverLatestCost", label: "Margen sobre último costo" },
  { value: "FixedMarginOverAverageCost", label: "Margen sobre costo promedio" },
  { value: "ProductMarginAdjustment", label: "Ajustar margen del producto" },
  { value: "SellAtAverageCost", label: "Vender al costo promedio" },
];
function channelStrategyLabel(value: PriceChannelStrategy | null) { return channelStrategies.find((item) => item.value === value)?.label ?? "Sin configurar"; }
function requiresChannelValue(value: PriceChannelStrategy) { return value === "PercentageOverBasePrice" || value === "MarginOverLatestCost" || value === "FixedMarginOverAverageCost" || value === "ProductMarginAdjustment"; }
function allowsNegativeChannelValue(value: PriceChannelStrategy) { return value === "PercentageOverBasePrice" || value === "ProductMarginAdjustment"; }
function validChannelValue(strategy: PriceChannelStrategy, value: number) { return !requiresChannelValue(strategy) || (strategy === "FixedMarginOverAverageCost" || strategy === "MarginOverLatestCost" ? value >= 0 && value < 100 : strategy === "ProductMarginAdjustment" ? value >= -99.999999 && value <= 99.999999 : value >= -100 && value <= 1000); }
function channelValueLabel(strategy: PriceChannelStrategy) { return strategy === "FixedMarginOverAverageCost" || strategy === "MarginOverLatestCost" ? "Margen objetivo (%)" : strategy === "ProductMarginAdjustment" ? "Puntos de ajuste" : "Variación (%) — usa negativo para descuento"; }
function channelStrategyHelp(strategy: PriceChannelStrategy) { return ({ TieredProductPrice: "Define precios por producto y escalas desde cualquier cantidad. El precio efectivo nunca queda por debajo del costo promedio.", PercentageOverBasePrice: "Aumenta o reduce el precio público vigente; acepta valores negativos, con costo promedio como piso.", MarginOverLatestCost: "Calcula el precio con el margen objetivo sobre el último costo de compra observado; si aún no existe una recepción usa el costo configurado del producto.", FixedMarginOverAverageCost: "Calcula el precio para un margen objetivo entre 0% y menos de 100% sobre el costo promedio.", ProductMarginAdjustment: "Suma o resta puntos al margen de cada producto. Ejemplo: 20% + 10 puntos = 30%; el resultado mínimo es 0%.", SellAtAverageCost: "Vende al costo promedio vigente, con margen de 0%." } satisfies Record<PriceChannelStrategy,string>)[strategy]; }
