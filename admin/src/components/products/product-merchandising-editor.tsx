"use client";

import { forwardRef, useEffect, useImperativeHandle, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Barcode, Boxes, Link2, Plus, Save, Scale, Tags, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { useProductCategories, useProducts } from "@/hooks/use-products";
import {
  productMerchandisingApi,
  type LinkedProduct,
  type ProductBarcode,
  type ProductMerchandising,
} from "@/services/api/product-merchandising";

const none = "__none__";
const emptyScale = {
  scaleCode: "",
  barcodePrefix: "",
  embeddedValueType: "Weight" as const,
  valueStart: 0,
  valueLength: 5,
  decimalPlaces: 3,
};

export interface ProductMerchandisingEditorHandle { save: () => Promise<void> }

export const ProductMerchandisingEditor = forwardRef<ProductMerchandisingEditorHandle, { productId: string; embedded?: boolean }>(function ProductMerchandisingEditor({ productId, embedded = false }, ref) {
  const client = useQueryClient();
  const config = useQuery({
    queryKey: ["product-merchandising", productId],
    queryFn: () => productMerchandisingApi.get(productId),
  });
  const brands = useQuery({ queryKey: ["product-brands"], queryFn: productMerchandisingApi.brands });
  const units = useQuery({ queryKey: ["product-units"], queryFn: productMerchandisingApi.units });
  const categories = useProductCategories(false);
  const [form, setForm] = useState<ProductMerchandising | null>(null);
  const [barcode, setBarcode] = useState("");
  const [newBrand, setNewBrand] = useState("");
  const [newUnit, setNewUnit] = useState({
    code: "",
    name: "",
    symbol: "",
    allowsFractionalQuantity: true,
    decimalPlaces: 6,
  });
  const [showBrandCreate, setShowBrandCreate] = useState(false);
  const [showUnitCreate, setShowUnitCreate] = useState(false);
  const [linkedSearch, setLinkedSearch] = useState("");
  const candidates = useProducts({
    page: 1,
    pageSize: 20,
    search: linkedSearch || undefined,
    includeInactive: false,
  });

  useEffect(() => {
    if (config.data) {
      setForm({ ...config.data, linkedProducts: config.data.linkedProducts ?? [] });
    }
  }, [config.data]);

  const chain = useMemo(
    () => categoryChain(categories.data ?? [], form?.productCategoryId ?? null),
    [categories.data, form?.productCategoryId],
  );
  const selectedUnit = units.data?.find((item) => item.code === form?.baseUnitCode);

  const save = useMutation({
    mutationFn: () => productMerchandisingApi.save(productId, {
      productCategoryId: form!.productCategoryId,
      productBrandId: form!.productBrandId,
      baseUnitCode: form!.baseUnitCode,
      manageInventory: form!.link?.sharesInventory ? false : form!.manageInventory,
      allowsFractionalSale: form!.allowsFractionalSale,
      isWeighable: form!.isWeighable,
      scale: form!.isWeighable ? form!.scale : null,
      barcodes: form!.barcodes,
      link: form!.link
        ? {
            parentProductId: form!.link.parentProductId,
            sharesInventory: form!.link.sharesInventory,
            inventoryFactor: form!.link.sharesInventory ? form!.link.inventoryFactor : null,
            sharesPrice: form!.link.sharesPrice,
            priceFactor: form!.link.sharesPrice ? form!.link.priceFactor : null,
          }
        : null,
      linkedProducts: form!.linkedProducts.map((item) => ({
        childProductId: item.childProductId,
        sharesInventory: item.sharesInventory,
        inventoryFactor: item.sharesInventory ? item.inventoryFactor : null,
        sharesPrice: item.sharesPrice,
        priceFactor: item.sharesPrice ? item.priceFactor : null,
      })),
    }),
    onSuccess: async (value) => {
      setForm(value);
      await client.invalidateQueries({ queryKey: ["products"] });
      toast.success("Configuración comercial actualizada.");
    },
    onError: (error: { message?: string }) =>
      toast.error(error.message ?? "No fue posible guardar la configuración."),
  });

  useImperativeHandle(ref, () => ({
    save: async () => { await save.mutateAsync(); },
  }), [save]);
  const createBrand = useMutation({
    mutationFn: () => productMerchandisingApi.createBrand(newBrand.trim()),
    onSuccess: async (value) => {
      await client.invalidateQueries({ queryKey: ["product-brands"] });
      setForm((current) => current && ({ ...current, productBrandId: value.productBrandId }));
      setNewBrand("");
      setShowBrandCreate(false);
    },
  });

  const createUnit = useMutation({
    mutationFn: () => productMerchandisingApi.createUnit(newUnit),
    onSuccess: async (value) => {
      await client.invalidateQueries({ queryKey: ["product-units"] });
      setForm((current) => current && ({ ...current, baseUnitCode: value.code }));
      setShowUnitCreate(false);
    },
  });

  if (config.isLoading || !form) {
    return <section className="rounded-2xl border p-6 text-sm text-muted-foreground">Cargando identidad comercial del producto…</section>;
  }
  if (config.isError) {
    return <section className="rounded-2xl border border-destructive/30 p-6 text-sm text-destructive">No fue posible cargar la configuración comercial.</section>;
  }

  function setCategoryAt(depth: number, value: string) {
    if (value === none) {
      setForm((current) => current && ({
        ...current,
        productCategoryId: depth === 0 ? null : chain[depth - 1]?.productCategoryId ?? null,
      }));
      return;
    }
    setForm((current) => current && ({ ...current, productCategoryId: value }));
  }

  function addBarcode() {
    if (!form) return;
    const value = barcode.trim();
    if (!value) return;
    if (form.barcodes.some((item) => item.value.toLocaleLowerCase() === value.toLocaleLowerCase())) {
      toast.error("Este código ya está agregado.");
      return;
    }
    setForm({
      ...form,
      barcodes: [...form.barcodes, { value, isPrimary: form.barcodes.length === 0 }],
    });
    setBarcode("");
  }

  function updateBarcode(index: number, update: Partial<ProductBarcode>) {
    if (!form) return;
    setForm({
      ...form,
      barcodes: form.barcodes.map((item, currentIndex) =>
        currentIndex === index
          ? { ...item, ...update }
          : update.isPrimary
            ? { ...item, isPrimary: false }
            : item),
    });
  }

  function updateLinkedProduct(index: number, update: Partial<LinkedProduct>) {
    if (!form) return;
    setForm({
      ...form,
      linkedProducts: form.linkedProducts.map((item, currentIndex) =>
        currentIndex === index ? { ...item, ...update } : item),
    });
  }

  const availableCandidates = (candidates.data?.items ?? []).filter(
    (item) => item.productId !== productId
      && !form.linkedProducts.some((link) => link.childProductId === item.productId),
  );

  return <section className={embedded ? "" : "overflow-hidden rounded-2xl border bg-card"}>
    {!embedded && <header className="border-b bg-gradient-to-r from-slate-950 to-teal-950 p-5 text-white">
      <p className="flex items-center gap-2 text-xs font-bold uppercase tracking-[.14em] text-teal-300">
        <Boxes className="h-4 w-4" /> Identidad y operación
      </p>
      <h3 className="mt-1 text-lg font-semibold">Todo lo necesario para encontrar, vender y descontar este producto</h3>
    </header>}

    <div className={`space-y-5 ${embedded ? "" : "p-5"}`}>
      <Block icon={Tags} title="Clasificacion, marca y unidad" description="Auraly conserva la ruta completa y la unidad real en la que se vende.">
        <div className="grid gap-3 sm:grid-cols-2">
          {["Área", "Línea", "Grupo", "Subgrupo"].map((label, depth) => {
            const parent = depth === 0 ? null : chain[depth - 1]?.productCategoryId ?? null;
            const options = (categories.data ?? []).filter(
              (item) => item.depth === depth && item.parentProductCategoryId === parent,
            );
            return <div className="space-y-1.5" key={label}>
              <Label>{label}</Label>
              <Select
                value={chain[depth]?.productCategoryId ?? none}
                disabled={depth > 0 && !parent}
                onValueChange={(value) => setCategoryAt(depth, value)}
              >
                <SelectTrigger><SelectValue placeholder={`Selecciona ${label.toLocaleLowerCase()}`} /></SelectTrigger>
                <SelectContent>
                  <SelectItem value={none}>Sin {label.toLocaleLowerCase()}</SelectItem>
                  {options.map((item) => <SelectItem key={item.productCategoryId} value={item.productCategoryId}>{item.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>;
          })}
        </div>
      <div className="mt-5 border-t pt-5">
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label>Marca</Label>
            <Select value={form.productBrandId ?? none} onValueChange={(value) => setForm({ ...form, productBrandId: value === none ? null : value })}>
              <SelectTrigger><SelectValue placeholder="Sin marca" /></SelectTrigger>
              <SelectContent>
                <SelectItem value={none}>Sin marca</SelectItem>
                {(brands.data ?? []).map((item) => <SelectItem key={item.productBrandId} value={item.productBrandId}>{item.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label>Unidad en la que se vende</Label>
            <Select value={form.baseUnitCode} onValueChange={(value) => setForm({ ...form, baseUnitCode: value })}>
              <SelectTrigger><SelectValue placeholder="Selecciona" /></SelectTrigger>
              <SelectContent>
                {(units.data ?? []).map((item) => <SelectItem key={item.productUnitId} value={item.code}>
                  {item.name} · {item.symbol}
                </SelectItem>)}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">Describe qué cantidad se vende; la regla de fracciones pertenece al producto.</p>
          </div>
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          <Button type="button" size="sm" variant="outline" onClick={() => setShowBrandCreate(!showBrandCreate)}><Plus className="mr-1 h-4 w-4" />Nueva marca</Button>
          <Button type="button" size="sm" variant="outline" onClick={() => setShowUnitCreate(!showUnitCreate)}><Plus className="mr-1 h-4 w-4" />Nueva unidad de venta</Button>
        </div>
        {showBrandCreate && <div className="mt-3 flex gap-2">
          <Input value={newBrand} onChange={(event) => setNewBrand(event.target.value)} placeholder="Ej. Samsung" />
          <Button type="button" disabled={!newBrand.trim()} onClick={() => createBrand.mutate()}>Crear</Button>
        </div>}
        {showUnitCreate && <div className="mt-3 grid gap-3 rounded-xl border bg-muted/20 p-4 sm:grid-cols-3">
          <Input value={newUnit.name} onChange={(event) => setNewUnit({ ...newUnit, name: event.target.value })} placeholder="Nombre: Kilogramo" />
          <Input value={newUnit.code} onChange={(event) => setNewUnit({ ...newUnit, code: event.target.value.toUpperCase() })} placeholder="Código: KG" />
          <Input value={newUnit.symbol} onChange={(event) => setNewUnit({ ...newUnit, symbol: event.target.value })} placeholder="Símbolo: kg" />
          <Button type="button" disabled={!newUnit.name || !newUnit.code || !newUnit.symbol} onClick={() => createUnit.mutate()}>Crear y asignar</Button>
        </div>}
      </div></Block>

      <Block icon={Barcode} title="Captura, cantidad y balanza" description="Varios codigos de barras y reglas de cantidad en un mismo lugar.">
        <div className="flex gap-2">
          <Input value={barcode} onChange={(event) => setBarcode(event.target.value)} onKeyDown={(event) => {
            if (event.key === "Enter") { event.preventDefault(); addBarcode(); }
          }} placeholder="Escanea o escribe el código" />
          <Button type="button" onClick={addBarcode}>Agregar</Button>
        </div>
        <div className="mt-3 space-y-2">
          {form.barcodes.length === 0 && <p className="rounded-lg border border-dashed p-3 text-sm text-muted-foreground">No hay códigos asignados.</p>}
          {form.barcodes.map((item, index) => <div key={`${item.value}-${index}`} className="flex items-center gap-2 rounded-lg border p-2">
            <Input value={item.value} onChange={(event) => updateBarcode(index, { value: event.target.value })} />
            <Button type="button" size="sm" variant={item.isPrimary ? "default" : "outline"} onClick={() => updateBarcode(index, { isPrimary: true })}>{item.isPrimary ? "Principal" : "Hacer principal"}</Button>
            <Button type="button" size="sm" variant="ghost" onClick={() => setForm({ ...form, barcodes: form.barcodes.filter((_, current) => current !== index).map((code, current) => ({ ...code, isPrimary: current === 0 })) })}>Quitar</Button>
          </div>)}
        </div>
      <div className="mt-5 grid gap-3 border-t pt-5 md:grid-cols-3">
        <Toggle
          label="Permitir venta fraccionada"
          detail={`Permite cantidades decimales para este producto vendido en ${selectedUnit?.name ?? "la unidad seleccionada"}.`}
          checked={form.allowsFractionalSale}
          onChange={(checked) => setForm({ ...form, allowsFractionalSale: checked, isWeighable: checked ? form.isWeighable : false, scale: checked ? form.scale : null })}
        />
        <Toggle
          label="Captura desde balanza"
          detail={form.allowsFractionalSale ? "La balanza podrá completar automáticamente la cantidad." : "Primero habilita la venta fraccionada para este producto."}
          checked={form.isWeighable}
          disabled={!form.allowsFractionalSale}
          onChange={(checked) => setForm({ ...form, isWeighable: checked, scale: checked ? form.scale ?? emptyScale : null })}
        />
        {form.isWeighable && form.scale && <div className="mt-3 grid gap-4 rounded-xl bg-muted/30 p-4 sm:col-span-3 sm:grid-cols-3">
          <div className="space-y-2"><Label className="flex min-h-10 items-center">Código del producto en la balanza</Label><Input value={form.scale.scaleCode} onChange={(event) => setForm({ ...form, scale: { ...form.scale!, scaleCode: event.target.value } })} placeholder="Ej. 125" /><p className="min-h-8 text-xs text-muted-foreground">También conocido como PLU.</p></div>
          <div className="space-y-2"><Label className="flex min-h-10 items-center">Inicio del código de balanza</Label><Input value={form.scale.barcodePrefix} onChange={(event) => setForm({ ...form, scale: { ...form.scale!, barcodePrefix: event.target.value } })} placeholder="Ej. 20" /><p className="min-h-8 text-xs text-muted-foreground">Prefijo que identifica una etiqueta generada por la balanza.</p></div>
          <div className="space-y-2"><Label className="flex min-h-10 items-center">Decimales del peso</Label><Input type="number" min="0" max="6" value={form.scale.decimalPlaces} onChange={(event) => setForm({ ...form, scale: { ...form.scale!, decimalPlaces: Number(event.target.value) } })} /><p className="min-h-8 text-xs text-muted-foreground">3 interpreta, por ejemplo, 1250 como 1,250 kg.</p></div>
        </div>}
        <Toggle
          label="Controla inventario"
          detail={form.link?.sharesInventory ? `No puede habilitarse porque comparte inventario con ${form.link.parentProductName}. Desactiva esa opcion o desvincula el producto para controlar inventario propio.` : "Compras, ventas, traslados y ajustes cambian sus unidades disponibles."}
          checked={form.link?.sharesInventory ? false : form.manageInventory}
          disabled={Boolean(form.link?.sharesInventory)}
          invalid={Boolean(form.link?.sharesInventory)}
          onChange={(checked) => setForm({ ...form, manageInventory: checked })}
        />
      </div></Block>

      <div>
        <Block icon={Link2} title="Familia de productos" description="Relaciona presentaciones, colores o tallas. Cada producto conserva su inventario y precio, salvo que elijas compartirlos.">
          {form.link ? <div className="rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950">
            <p className="font-semibold">Este producto está vinculado a {form.link.parentProductName}</p>
            <p className="mt-1 text-xs">Esta relacion permite encontrar todas las opciones de la familia. El inventario solo se bloquea cuando se comparte con el producto principal.</p>
          </div> : <>
            <div className="rounded-xl border bg-muted/20 p-3">
              <Label htmlFor={`linked-search-${productId}`}>Agregar producto a la lista</Label>
              <Input id={`linked-search-${productId}`} className="mt-2" value={linkedSearch} onChange={(event) => setLinkedSearch(event.target.value)} placeholder="Busca por nombre, código o referencia" />
              {linkedSearch.trim() && <div className="mt-2 max-h-52 space-y-1 overflow-y-auto rounded-lg border bg-background p-1">
                {availableCandidates.map((item) => <button key={item.productId} type="button" className="flex w-full items-center justify-between rounded-md px-3 py-2 text-left hover:bg-muted" onClick={() => {
                  setForm({ ...form, linkedProducts: [...form.linkedProducts, {
                    childProductId: item.productId,
                    childProductCode: item.sku ?? "",
                    childProductName: item.name,
                    sharesInventory: false,
                    inventoryFactor: null,
                    sharesPrice: false,
                    priceFactor: null,
                  }] });
                  setLinkedSearch("");
                }}>
                  <span><strong className="block text-sm">{item.name}</strong><small className="text-muted-foreground">{item.sku ?? "Sin código"}</small></span>
                  <span className="flex items-center text-sm font-medium text-primary"><Plus className="mr-1 h-4 w-4" />Agregar</span>
                </button>)}
                {!candidates.isLoading && availableCandidates.length === 0 && <p className="p-3 text-sm text-muted-foreground">No hay productos disponibles con esa búsqueda.</p>}
              </div>}
            </div>

            <div className="mt-3 space-y-3">
              {form.linkedProducts.length === 0 && <div className="rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">Todavia no has agregado opciones a esta familia.</div>}
              {form.linkedProducts.map((item, index) => <article key={item.childProductId} className="rounded-xl border p-4">
                <div className="mb-3 flex items-start justify-between gap-3">
                  <div><p className="font-semibold">{item.childProductName}</p><p className="text-xs text-muted-foreground">{item.childProductCode || "Sin código"}</p></div>
                  <Button type="button" size="icon" variant="ghost" aria-label={`Quitar ${item.childProductName}`} onClick={() => setForm({ ...form, linkedProducts: form.linkedProducts.filter((_, current) => current !== index) })}><Trash2 className="h-4 w-4" /></Button>
                </div>
                <div className="grid gap-3 md:grid-cols-2">
                  <div>
                    <Toggle label="Compartir inventario" detail="Cada venta descontará del producto principal." checked={item.sharesInventory} onChange={(checked) => updateLinkedProduct(index, { sharesInventory: checked, inventoryFactor: checked ? item.inventoryFactor ?? 1 : null })} />
                    <Label>Unidades del principal por cada unidad vendida</Label>
                    <Input className="mt-1" type="number" min="0.000001" step="0.001" disabled={!item.sharesInventory} value={item.inventoryFactor ?? ""} onChange={(event) => updateLinkedProduct(index, { inventoryFactor: Number(event.target.value) })} />
                  </div>
                  <div>
                    <Toggle label="Compartir precio" detail="Al publicar el principal, este precio se actualizará por el factor." checked={item.sharesPrice} onChange={(checked) => updateLinkedProduct(index, { sharesPrice: checked, priceFactor: checked ? item.priceFactor ?? 1 : null })} />
                    <Label>Multiplicador del precio principal</Label>
                    <Input className="mt-1" type="number" min="0.000001" step="0.001" disabled={!item.sharesPrice} value={item.priceFactor ?? ""} onChange={(event) => updateLinkedProduct(index, { priceFactor: Number(event.target.value) })} />
                  </div>
                </div>
              </article>)}
            </div>
          </>}
        </Block>
      </div>
    </div>

    {!embedded && <footer className="flex justify-end border-t bg-muted/20 p-4">
      <Button type="button" onClick={() => save.mutate()} disabled={save.isPending}>
        <Save className="mr-2 h-4 w-4" />{save.isPending ? "Guardando…" : "Guardar identidad y operación"}
      </Button>
    </footer>}
  </section>;
});

function Block({ icon: Icon, title, description, children }: {
  icon: typeof Tags;
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return <section className="scroll-mt-5 rounded-2xl border bg-background p-5 shadow-sm">
    <div className="mb-4 flex gap-3">
      <span className="rounded-lg bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5" /></span>
      <div><h4 className="font-semibold">{title}</h4><p className="text-xs text-muted-foreground">{description}</p></div>
    </div>
    {children}
  </section>;
}

function Toggle({ label, detail, checked, disabled = false, invalid = false, onChange }: {
  label: string;
  detail: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
  invalid?: boolean;
}) {
  return <div className={`mb-2 flex items-center justify-between gap-3 rounded-lg border p-3 ${disabled ? "opacity-60" : ""} ${invalid ? "border-destructive/60 bg-destructive/5" : ""}`}>
    <div><p className={`text-sm font-medium ${invalid ? "text-destructive" : ""}`}>{label}</p><p className={`text-xs ${invalid ? "text-destructive" : "text-muted-foreground"}`}>{detail}</p></div>
    <Switch checked={checked} disabled={disabled} onCheckedChange={onChange} />
  </div>;
}

function categoryChain(
  categories: Array<{ productCategoryId: string; parentProductCategoryId: string | null; depth: number }>,
  selectedId: string | null,
) {
  const byId = new Map(categories.map((item) => [item.productCategoryId, item]));
  const result: typeof categories = [];
  let current = selectedId ? byId.get(selectedId) : undefined;
  while (current) {
    result.unshift(current);
    current = current.parentProductCategoryId ? byId.get(current.parentProductCategoryId) : undefined;
  }
  return result;
}
