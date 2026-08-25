"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Barcode, Boxes, Check, CircleDollarSign, Images, Link2, PackagePlus, ReceiptText, Scale, Tags, Trash2, Truck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useProductCategories } from "@/hooks/use-products";
import { recalculateProductPricing } from "@/lib/product-pricing-calculator";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { formatCurrency } from "@/lib/utils";
import { goodsReceiptsApi } from "@/services/api/goods-receipts";
import { productMerchandisingApi, type LinkedProduct, type ProductBarcode } from "@/services/api/product-merchandising";
import { productsApi } from "@/services/api/products";
import { productOffersApi } from "@/services/api/product-offers";
import { PendingProductImagePicker, type PendingProductImage } from "@/components/products/product-image-gallery";
import { ProductPicker } from "@/components/products/product-picker";
import { taxProfilesApi } from "@/services/api/tax-profiles";
import { useBusinessContextStore } from "@/stores/business-context-store";

const none = "__none__";
const sections = [
  ["identity", "Identidad", PackagePlus],
  ["classification", "Clasificación", Tags],
  ["sale", "Venta y balanza", Scale],
  ["family", "Productos vinculados", Link2],
  ["supplier", "Proveedor y empaque", Truck],
  ["taxes", "IVA y precios", ReceiptText],
  ["images", "Imágenes", Images],
] as const;

interface Props { open: boolean; onOpenChange: (open: boolean) => void; onCreated?: (productId: string) => void }

interface CreateState {
  reference: string; name: string; description: string;
  productCategoryId: string | null; productBrandId: string | null; baseUnitCode: string;
  manageInventory: boolean; allowsFractionalSale: boolean; isWeighable: boolean;
  salesTaxProfileId: string; purchaseTaxProfileId: string;
  purchaseTaxTreatment: "DeductibleInputVat" | "CapitalizedCost" | "NotApplicable";
  cost: number; margin: number; salePrice: number;
  barcodes: ProductBarcode[];
  scaleCode: string; scalePrefix: string; scaleDecimals: number;
  supplierId: string | null; supplierProductCode: string; packageName: string; unitsPerPackage: number;
}

const initialState: CreateState = {
  reference: "", name: "", description: "", productCategoryId: null,
  productBrandId: null, baseUnitCode: "EA", manageInventory: true, allowsFractionalSale: false,
  isWeighable: false, salesTaxProfileId: "", purchaseTaxProfileId: "",
  purchaseTaxTreatment: "DeductibleInputVat", cost: 0, margin: 0, salePrice: 0, barcodes: [],
  scaleCode: "", scalePrefix: "", scaleDecimals: 3, supplierId: null, supplierProductCode: "",
  packageName: "Unidad", unitsPerPackage: 1,
};

export function ProductCreateWorkspace({ open, onOpenChange, onCreated }: Props) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const queryClient = useQueryClient();
  const [form, setForm] = useState(initialState);
  const [barcode, setBarcode] = useState("");
  const [validationError, setValidationError] = useState<string>();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [pendingImages, setPendingImages] = useState<PendingProductImage[]>([]);
  const [linkedProducts, setLinkedProducts] = useState<LinkedProduct[]>([]);
  const [conversionMaximumLossPercent, setConversionMaximumLossPercent] = useState<number | null>(null);
  const categories = useProductCategories(false);
  const purchasePresentations = useReferenceOptions("purchase-presentation", open);
  const brands = useQuery({ queryKey: ["product-brands"], queryFn: productMerchandisingApi.brands, enabled: open });
  const units = useQuery({ queryKey: ["product-units"], queryFn: productMerchandisingApi.units, enabled: open });
  const taxes = useQuery({ queryKey: ["tax-profiles", businessId], queryFn: () => taxProfilesApi.list(false), enabled: open && !!businessId });
  const options = useQuery({ queryKey: ["goods-receipt-options", businessId], queryFn: goodsReceiptsApi.options, enabled: open && !!businessId });
  const chain = useMemo(() => categoryChain(categories.data ?? [], form.productCategoryId), [categories.data, form.productCategoryId]);
  const salesTax = taxes.data?.find((item) => item.taxProfileId === form.salesTaxProfileId);
  const purchaseTax = taxes.data?.find((item) => item.taxProfileId === form.purchaseTaxProfileId);
  const selectedUnit = units.data?.find((item) => item.code === form.baseUnitCode);
  const salesTaxRate = salesTax?.rate ?? 0;
  const netSalePrice = salesTaxRate > 0 ? form.salePrice / (1 + salesTaxRate / 100) : form.salePrice;
  const marginAmount = Math.max(0, netSalePrice - form.cost);

  const create = useMutation({
    mutationFn: async () => {
      if (!businessId) throw new Error("Selecciona un negocio antes de crear el producto.");
      const nextErrors: Record<string, string> = {};
      if (!form.name.trim()) nextErrors.name = "Este campo es requerido";
      if (!form.baseUnitCode) nextErrors.baseUnitCode = "Este campo es requerido";
      if (!form.salesTaxProfileId) nextErrors.salesTaxProfileId = "Este campo es requerido";
      if (!form.purchaseTaxProfileId) nextErrors.purchaseTaxProfileId = "Este campo es requerido";
      if (!form.supplierId) nextErrors.supplierId = "Este campo es requerido";
      if (!(form.cost > 0)) nextErrors.cost = "Este campo es requerido";
      if (!(form.margin > 0)) nextErrors.margin = "Este campo es requerido";
      else if (form.margin >= 100) nextErrors.margin = "Debe ser menor que 100 %";
      if (!(form.salePrice > 0)) nextErrors.salePrice = "Este campo es requerido";
      setFieldErrors(nextErrors);
      if (Object.keys(nextErrors).length > 0) throw new Error("Revisa los campos resaltados.");
      if (!form.name.trim()) throw new Error("El nombre es obligatorio.");
      if (!form.salesTaxProfileId || !form.purchaseTaxProfileId) throw new Error("Selecciona el IVA de venta y el IVA de compra.");
      if ((purchaseTax?.rate ?? 0) === 0 && form.purchaseTaxTreatment !== "NotApplicable") throw new Error("Un IVA de compra del 0 % debe usar el tratamiento No aplica.");
      if ((purchaseTax?.rate ?? 0) > 0 && form.purchaseTaxTreatment === "NotApplicable") throw new Error("Selecciona IVA descontable o Mayor valor del costo para un IVA de compra mayor que 0 %.");
      if (!form.baseUnitCode) throw new Error("Selecciona la unidad del producto.");
      if (!(form.cost > 0)) throw new Error("El costo debe ser mayor que cero.");
      if (!(form.margin > 0 && form.margin < 100)) throw new Error("El margen debe ser mayor que cero y menor que 100 %.");
      if (!(form.salePrice > 0)) throw new Error("El precio público debe ser mayor que cero.");
      if (form.isWeighable && !form.allowsFractionalSale) throw new Error("Habilita la venta fraccionada antes de usar balanza.");
      const supplier = options.data?.suppliers.find((item) => item.supplierId === form.supplierId);
      const product = await productsApi.createCatalog({
        businessId,
        productCode: "", reference: form.reference.trim() || null,
        name: form.name.trim(), description: form.description.trim() || null,
        baseUnitCode: form.baseUnitCode, taxProfileId: form.salesTaxProfileId,
        purchaseTaxProfileId: form.purchaseTaxProfileId, purchaseTaxTreatment: form.purchaseTaxTreatment,
        manageInventory: form.manageInventory, isWeighable: form.isWeighable,
        barcodes: form.barcodes, identifiers: [], prices: [{ amount: form.salePrice, currencyCode: "COP",
          costBasisAmount: form.cost, targetMarginPercent: form.margin }],
        suppliers: supplier ? [{ supplierId: supplier.supplierId, identification: supplier.identification,
          name: supplier.name, supplierProductCode: form.supplierProductCode.trim() || null,
          baseUnitCost: form.cost, isPrimary: true, purchasePresentationName: form.packageName.trim() || "Unidad",
          unitsPerPresentation: form.unitsPerPackage }] : [],
        scale: form.isWeighable ? { scaleCode: form.scaleCode.trim(), barcodePrefix: form.scalePrefix.trim(),
          embeddedValueType: "Weight", valueStart: 0, valueLength: 5, decimalPlaces: form.scaleDecimals } : null,
        productCategoryId: form.productCategoryId, productBrandId: form.productBrandId,
        allowsFractionalSale: form.allowsFractionalSale,
        link: null,

      });
      if (linkedProducts.length > 0) {
        await productMerchandisingApi.save(product.productId, {
          productCategoryId: form.productCategoryId,
          productBrandId: form.productBrandId,
          baseUnitCode: form.baseUnitCode,
          manageInventory: form.manageInventory,
          allowsFractionalSale: form.allowsFractionalSale,
          isWeighable: form.isWeighable,
          scale: form.isWeighable ? { scaleCode: form.scaleCode.trim(), barcodePrefix: form.scalePrefix.trim(), embeddedValueType: "Weight", valueStart: 0, valueLength: 5, decimalPlaces: form.scaleDecimals } : null,
          barcodes: form.barcodes,
          link: null,
          linkedProducts: linkedProducts.map(({ childProductId, sharesInventory, inventoryFactor, sharesPrice, priceFactor, allowsConversion, conversionFactor }) => ({ childProductId, sharesInventory, inventoryFactor, sharesPrice, priceFactor, allowsConversion, conversionFactor })),
          conversionMaximumLossPercent: linkedProducts.some((item) => item.allowsConversion) ? conversionMaximumLossPercent ?? 0 : null,
        });
      }
      const uploads = await Promise.allSettled(pendingImages.map((image) =>
        productOffersApi.uploadImage(businessId, product.productId, image.file, null, image.isPrimary)));
      const failedUploads = uploads.filter((result) => result.status === "rejected").length;
      if (failedUploads > 0)
        toast.warning(`El producto fue creado, pero ${failedUploads} imagen(es) no pudieron cargarse.`);
      return product;
    },
    onSuccess: async (product) => {
      await queryClient.invalidateQueries({ queryKey: ["products", businessId] });
      pendingImages.forEach((image) => URL.revokeObjectURL(image.previewUrl));
      setForm(initialState); setBarcode(""); setPendingImages([]); setLinkedProducts([]); setConversionMaximumLossPercent(null); setValidationError(undefined); setFieldErrors({});
      toast.success("Producto creado. El precio quedó preparado para publicación.");
      onOpenChange(false); onCreated?.(product.productId);
    },
    onError: (error: { message?: string }) => { const message = error.message ?? "No fue posible crear el producto."; setValidationError(message); toast.error(message); },
  });

  function setCategory(depth: number, value: string) {
    setForm((current) => ({ ...current, productCategoryId: value === none ? (depth === 0 ? null : chain[depth - 1]?.productCategoryId ?? null) : value }));
  }
  function addBarcode() {
    const value = barcode.trim();
    if (!value) return;
    if (form.barcodes.some((item) => item.value.toLowerCase() === value.toLowerCase())) { toast.error("Ese código ya está agregado."); return; }
    setForm((current) => ({ ...current, barcodes: [...current.barcodes, { value, isPrimary: current.barcodes.length === 0 }] }));
    setBarcode("");
  }
  function updateLinked(index: number, patch: Partial<LinkedProduct>) {
    setLinkedProducts((current) => current.map((item, currentIndex) => currentIndex === index ? { ...item, ...patch } : item));
  }
  function changePricing(field: "cost" | "margin" | "salePrice", value: number) {
    try {
      const next = recalculateProductPricing(field, value, { cost: form.cost, margin: form.margin, salePrice: form.salePrice, salesTaxRate: salesTax?.rate ?? 0 });
      setForm((current) => ({ ...current, cost: next.cost, margin: next.margin, salePrice: next.salePrice }));
    } catch { toast.error("Revisa el costo, el margen y el precio de venta."); }
  }

  return <Dialog open={open} onOpenChange={onOpenChange}>
    <DialogContent className="h-[96dvh] max-h-[96dvh] w-[96vw] max-w-[1480px] overflow-hidden p-0">
      <div className="grid h-full min-h-0 lg:grid-cols-[250px_1fr]">
        <aside className="hidden border-r bg-slate-950 p-6 text-white lg:block">
          <div className="sticky top-0">
            <div className="mb-7 flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-400 text-slate-950"><PackagePlus /></div>
            <p className="text-xs font-bold uppercase tracking-[.2em] text-teal-300">Nuevo producto</p>
            <h2 className="mt-2 text-2xl font-semibold">Una ficha, todo conectado</h2>
            <p className="mt-2 text-sm text-slate-300">Completa lo necesario para comprar, publicar y vender sin abrir ventanas adicionales.</p>
            <nav className="mt-8 space-y-1">{sections.map(([id, label, Icon], index) => <a key={id} href={`#new-${id}`} className="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm text-slate-200 hover:bg-white/10"><span className="flex h-7 w-7 items-center justify-center rounded-full border border-teal-400/40 text-xs text-teal-300">{index + 1}</span><Icon className="h-4 w-4" />{label}</a>)}</nav>
          </div>
        </aside>
        <div className="flex min-h-0 flex-col bg-muted/20">
          <DialogHeader className="border-b bg-background px-6 py-5 pr-14">
            <DialogTitle className="text-2xl">Crear producto</DialogTitle>
            <DialogDescription>El precio de venta se prepara ahora y solo se publica desde Rentabilidad y precios.</DialogDescription>
          </DialogHeader>
          <div className="min-h-0 flex-1 overflow-y-auto scroll-smooth p-4 sm:p-6">
            <div className="mx-auto max-w-5xl space-y-5">
              {validationError && <div role="alert" className="rounded-xl border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive"><strong>Revisa la informacion requerida.</strong> {validationError}</div>}
              <Section id="new-identity" icon={PackagePlus} title="Identidad" description="Lo que el equipo verá al buscar y vender.">
                <div className="grid gap-4 md:grid-cols-2"><Field label="Nombre *" className="md:col-span-2" error={fieldErrors.name}><Input aria-invalid={Boolean(fieldErrors.name)} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Nombre claro para venta y búsqueda" /></Field><Field label="Referencia" className="md:col-span-2"><Input value={form.reference} maxLength={120} onChange={(e) => setForm({ ...form, reference: e.target.value })} placeholder="Referencia del fabricante" /></Field><Field label="Descripción" className="md:col-span-2"><Textarea className="min-h-24" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} placeholder="Presentación, uso y detalles relevantes" /></Field></div>
              </Section>
              <Section id="new-classification" icon={Tags} title="Clasificación, marca y unidad" description="Auraly conserva la ruta completa y la unidad real en la que se vende.">
                <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">{["Área", "Línea", "Grupo", "Subgrupo"].map((label, depth) => { const parent = depth === 0 ? null : chain[depth - 1]?.productCategoryId ?? null; const items = (categories.data ?? []).filter((item) => item.depth === depth && item.parentProductCategoryId === parent); return <Field key={label} label={label}><Select value={chain[depth]?.productCategoryId ?? none} disabled={depth > 0 && !parent} onValueChange={(value) => setCategory(depth, value)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value={none}>Sin {label.toLowerCase()}</SelectItem>{items.map((item) => <SelectItem key={item.productCategoryId} value={item.productCategoryId}>{item.name}</SelectItem>)}</SelectContent></Select></Field>; })}</div>
                <div className="mt-4 grid gap-4 md:grid-cols-2"><Field label="Marca"><Select value={form.productBrandId ?? none} onValueChange={(value) => setForm({ ...form, productBrandId: value === none ? null : value })}><SelectTrigger><SelectValue placeholder="Sin marca" /></SelectTrigger><SelectContent><SelectItem value={none}>Sin marca</SelectItem>{(brands.data ?? []).map((item) => <SelectItem key={item.productBrandId} value={item.productBrandId}>{item.name}</SelectItem>)}</SelectContent></Select></Field><Field label="Unidad en la que se vende" error={fieldErrors.baseUnitCode}><Select value={form.baseUnitCode} onValueChange={(value) => setForm({ ...form, baseUnitCode: value })}><SelectTrigger aria-invalid={Boolean(fieldErrors.baseUnitCode)}><SelectValue /></SelectTrigger><SelectContent>{(units.data ?? []).map((item) => <SelectItem key={item.productUnitId} value={item.code}>{item.name} · {item.symbol}</SelectItem>)}</SelectContent></Select><p className="text-xs text-muted-foreground">Describe qué cantidad se vende; la regla de fracciones pertenece al producto.</p></Field></div>
              </Section>
              <Section id="new-sale" icon={Barcode} title="Captura, cantidad y balanza" description="Varios códigos de barras y reglas de cantidad en un mismo lugar.">
                <div className="flex gap-2"><Input value={barcode} onChange={(e) => setBarcode(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); addBarcode(); } }} placeholder="Escanea o escribe un código" /><Button type="button" variant="outline" onClick={addBarcode}>Agregar</Button></div>
                <div className="mt-3 flex flex-wrap gap-2">{form.barcodes.map((item, index) => <span key={`${item.value}-${index}`} className="inline-flex items-center gap-2 rounded-full border bg-background px-3 py-1.5 text-sm"><Barcode className="h-3.5 w-3.5" />{item.value}{item.isPrimary && <strong className="text-xs text-primary">Principal</strong>}<button type="button" className="text-muted-foreground hover:text-destructive" onClick={() => setForm({ ...form, barcodes: form.barcodes.filter((_, i) => i !== index).map((code, i) => ({ ...code, isPrimary: i === 0 })) })}>×</button></span>)}</div>
                <div className="mt-4 grid gap-3 md:grid-cols-3"><Toggle label="Controla inventario" detail="Compras, ventas, traslados y ajustes cambian sus unidades disponibles." checked={form.manageInventory} onChange={(checked) => setForm({ ...form, manageInventory: checked })} /><Toggle label="Permitir venta fraccionada" detail={`Acepta cantidades decimales en ${selectedUnit?.name ?? "la unidad seleccionada"}.`} checked={form.allowsFractionalSale} onChange={(checked) => setForm({ ...form, allowsFractionalSale: checked, isWeighable: checked ? form.isWeighable : false })} /><Toggle label="Captura desde balanza" detail={form.allowsFractionalSale ? "Lee peso o cantidad automáticamente." : "Primero habilita la venta fraccionada."} checked={form.isWeighable} disabled={!form.allowsFractionalSale} onChange={(checked) => setForm({ ...form, isWeighable: checked })} /></div>
                {form.isWeighable && <div className="mt-4 grid items-start gap-4 rounded-xl bg-muted/30 p-4 md:grid-cols-3 [&>div>label]:flex [&>div>label]:min-h-10 [&>div>label]:items-center [&>div>p]:min-h-8"><Field label="Código del producto en la balanza"><Input value={form.scaleCode} onChange={(e) => setForm({ ...form, scaleCode: e.target.value })} placeholder="Ej. 125" /><p className="text-xs text-muted-foreground">También conocido como PLU.</p></Field><Field label="Inicio del código de balanza"><Input value={form.scalePrefix} onChange={(e) => setForm({ ...form, scalePrefix: e.target.value })} placeholder="Ej. 20" /><p className="text-xs text-muted-foreground">Identifica las etiquetas generadas por la balanza.</p></Field><Field label="Decimales del peso"><Input type="number" min={0} max={6} value={form.scaleDecimals} onChange={(e) => setForm({ ...form, scaleDecimals: Number(e.target.value) })} /><p className="text-xs text-muted-foreground">3 interpreta 1250 como 1,250 kg.</p></Field></div>}
              </Section>

              <Section id="new-family" icon={Link2} title="Familia de productos" description="Relaciona presentaciones, colores o tallas. Cada producto conserva su inventario y precio, salvo que elijas compartirlos.">
                {businessId && <div className="rounded-xl border bg-muted/20 p-3"><ProductPicker businessId={businessId} selectedProductIds={new Set(linkedProducts.map((item) => item.childProductId))} excludedProductIds={new Set(linkedProducts.map((item) => item.childProductId))} disabled={create.isPending} requireZeroInventory label="Agregar producto a la lista" resultsMode="inline" inputId="new-linked-product-search" onSelect={(product) => setLinkedProducts((current) => [...current, { childProductId: product.productId, childProductCode: product.productCode, childProductName: product.productName, sharesInventory: false, inventoryFactor: null, sharesPrice: false, priceFactor: null, allowsConversion: false, conversionFactor: null }])} /></div>}
                <div className="mt-3 space-y-3">
                  {linkedProducts.some((item) => item.allowsConversion) && <div className="rounded-xl border border-emerald-200 bg-emerald-50/60 p-4"><Label>Merma máxima permitida en conversiones (%)</Label><FormattedNumberInput className="mt-2 max-w-52 bg-background" kind="percent" value={conversionMaximumLossPercent ?? ""} onValueChange={(value) => setConversionMaximumLossPercent(value)} placeholder="Ej. 5" /><p className="mt-2 text-xs text-muted-foreground">Se aplica a toda la familia. Una salida nunca puede superar las unidades equivalentes consumidas.</p></div>}
                  {linkedProducts.length === 0 && <div className="rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">Todavía no has agregado opciones a esta familia.</div>}
                  {linkedProducts.map((item, index) => <article key={item.childProductId} className="rounded-xl border p-4"><div className="mb-3 flex items-start justify-between gap-3"><div><p className="font-semibold">{item.childProductName}</p><p className="text-xs text-muted-foreground">{item.childProductCode || "Sin código"}</p></div><Button type="button" size="icon" variant="ghost" aria-label={`Quitar ${item.childProductName}`} onClick={() => setLinkedProducts((current) => current.filter((_, currentIndex) => currentIndex !== index))}><Trash2 className="h-4 w-4" /></Button></div><div className="grid items-start gap-3 md:grid-cols-3"><FamilyOption label="Compartir inventario" detail="Cada venta descontará del producto principal." checked={item.sharesInventory} valueLabel="Unidades del principal por cada unidad vendida" value={item.inventoryFactor} onToggle={(checked) => updateLinked(index, { sharesInventory: checked, inventoryFactor: checked ? item.inventoryFactor ?? 1 : null, allowsConversion: checked ? false : item.allowsConversion, conversionFactor: checked ? null : item.conversionFactor })} onValue={(value) => updateLinked(index, { inventoryFactor: value })} /><FamilyOption label="Vincular costo" detail="Deriva el costo del principal y prepara el precio conservando el margen propio." checked={item.sharesPrice} valueLabel="Multiplicador del costo principal" value={item.priceFactor} onToggle={(checked) => updateLinked(index, { sharesPrice: checked, priceFactor: checked ? item.priceFactor ?? 1 : null })} onValue={(value) => updateLinked(index, { priceFactor: value })} /><FamilyOption label="Permitir conversión" detail="Habilita conversiones con los integrantes permitidos." checked={item.allowsConversion} valueLabel="Unidades equivalentes al producto principal" value={item.conversionFactor} onToggle={(checked) => { updateLinked(index, { allowsConversion: checked, conversionFactor: checked ? item.conversionFactor ?? 1 : null, sharesInventory: checked ? false : item.sharesInventory, inventoryFactor: checked ? null : item.inventoryFactor }); if (checked) { setForm((current) => ({ ...current, manageInventory: true })); setConversionMaximumLossPercent((current) => current ?? 0); } }} onValue={(value) => updateLinked(index, { conversionFactor: value })} /></div></article>)}
                </div>
              </Section>

              <Section id="new-supplier" icon={Truck} title="Proveedor principal y empaque habitual" description="Requerido para que cada producto tenga trazabilidad de compra desde su creación.">
                <div className="grid items-start gap-4 lg:grid-cols-3"><Field label="Proveedor principal *" error={fieldErrors.supplierId}><Select value={form.supplierId??undefined} onValueChange={(value) => setForm({ ...form, supplierId:value })}><SelectTrigger aria-invalid={Boolean(fieldErrors.supplierId)}><SelectValue placeholder="Selecciona un proveedor" /></SelectTrigger><SelectContent>{(options.data?.suppliers ?? []).map((supplier) => <SelectItem key={supplier.supplierId} value={supplier.supplierId}>{supplier.name} · {supplier.identification}</SelectItem>)}</SelectContent></Select></Field><Field label="Código del proveedor"><Input value={form.supplierProductCode} onChange={(e) => setForm({ ...form, supplierProductCode: e.target.value })} /></Field><Field label="Empaque en que lo entrega"><Select value={form.packageName} onValueChange={(value) => setForm({ ...form, packageName: value })}><SelectTrigger><SelectValue placeholder="Selecciona el empaque" /></SelectTrigger><SelectContent>{(purchasePresentations.data ?? []).map((option) => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></Field><Field label="Contenido por empaque"><Input type="number" min="0.000001" step="0.001" value={form.unitsPerPackage} onChange={(e) => setForm({ ...form, unitsPerPackage: Number(e.target.value) })} /></Field></div>
              </Section>
              <Section id="new-taxes" icon={CircleDollarSign} title="IVA, costo y precio" description="El IVA se incluye en el precio público. El precio preparado y el público nacen con el mismo valor.">
                <div className="grid gap-4 md:grid-cols-3"><Field label="IVA de venta *" error={fieldErrors.salesTaxProfileId}><Select value={form.salesTaxProfileId} onValueChange={(value) => { const rate = taxes.data?.find((item) => item.taxProfileId === value)?.rate ?? 0; const next = recalculateProductPricing("cost", form.cost, { cost: form.cost, margin: form.margin, salePrice: form.salePrice, salesTaxRate: rate }); setForm({ ...form, salesTaxProfileId: value, salePrice: next.salePrice }); }}><SelectTrigger aria-invalid={Boolean(fieldErrors.salesTaxProfileId)}><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{(taxes.data ?? []).map((tax) => <SelectItem key={tax.taxProfileId} value={tax.taxProfileId}>{tax.name} · {tax.rate}%</SelectItem>)}</SelectContent></Select></Field><Field label="IVA de compra *" error={fieldErrors.purchaseTaxProfileId}><Select value={form.purchaseTaxProfileId} onValueChange={(value) => { const rate = taxes.data?.find((item) => item.taxProfileId === value)?.rate ?? 0; setForm({ ...form, purchaseTaxProfileId: value, purchaseTaxTreatment: rate === 0 ? "NotApplicable" : form.purchaseTaxTreatment === "NotApplicable" ? "DeductibleInputVat" : form.purchaseTaxTreatment }); }}><SelectTrigger aria-invalid={Boolean(fieldErrors.purchaseTaxProfileId)}><SelectValue placeholder="Selecciona" /></SelectTrigger><SelectContent>{(taxes.data ?? []).map((tax) => <SelectItem key={tax.taxProfileId} value={tax.taxProfileId}>{tax.name} · {tax.rate}%</SelectItem>)}</SelectContent></Select></Field><Field label="Tratamiento del IVA de compra"><Select value={form.purchaseTaxTreatment} disabled={(purchaseTax?.rate ?? 0) === 0} onValueChange={(value) => setForm({ ...form, purchaseTaxTreatment: value as CreateState["purchaseTaxTreatment"] })}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="DeductibleInputVat">IVA descontable</SelectItem><SelectItem value="CapitalizedCost">Mayor valor del costo</SelectItem><SelectItem value="NotApplicable">No aplica</SelectItem></SelectContent></Select></Field></div>
                <div className="mt-5 space-y-5">
                  <div><h4 className="font-semibold">Datos para calcular el precio</h4><p className="text-xs text-muted-foreground">Costo y margen determinan el precio antes de IVA.</p></div>
                  <div className="grid gap-4 lg:grid-cols-2"><MoneyField label="Costo base" kind="currency" value={form.cost} error={fieldErrors.cost} onChange={(value) => changePricing("cost", value)} /><MoneyField label="Margen sobre el precio antes de IVA" kind="percent" value={form.margin} error={fieldErrors.margin} onChange={(value) => changePricing("margin", value)} /></div>
                  <div className="rounded-2xl border border-emerald-200 bg-emerald-50/60 p-4 text-emerald-950">
                    <div className="mb-3"><h4 className="font-semibold">Así se forma el precio de venta</h4><p className="text-xs text-emerald-900/75">El margen se calcula antes del IVA; después se agrega el IVA de venta seleccionado arriba.</p></div>
                    <div className="grid items-stretch gap-2 md:grid-cols-[1fr_auto_1fr_auto_1fr_auto_1fr]"><CreateFormulaValue label="Costo base" value={form.cost} /><CreateFormulaSign value={`÷ (1 − ${form.margin.toLocaleString("es-CO")}%)`} /><CreateFormulaValue label="Precio antes de IVA" value={netSalePrice} detail={`Margen: ${formatCurrency(marginAmount)}`} /><CreateFormulaSign value="+" /><CreateFormulaValue label={`IVA de venta (${salesTaxRate}%)`} value={Math.max(0, form.salePrice - netSalePrice)} /><CreateFormulaSign value="=" /><div className={`rounded-xl bg-emerald-600 p-3 text-white ${fieldErrors.salePrice ? "ring-2 ring-destructive" : ""}`}><Label className="text-xs text-white">Precio de venta preparado · IVA incluido</Label><FormattedNumberInput className="mt-2 h-11 border-white/30 bg-white text-lg font-bold text-emerald-950" kind="currency" value={form.salePrice} onValueChange={(next) => changePricing("salePrice", next ?? 0)} /><p className="mt-2 text-xs text-white/75">Al cambiarlo se conserva el costo y se recalcula el margen.</p>{fieldErrors.salePrice && <p className="mt-2 text-sm font-medium text-red-100">{fieldErrors.salePrice}</p>}</div></div>
                    <p className="mt-3 rounded-lg bg-white/70 px-3 py-2 text-xs">Fórmula completa: precio antes de IVA = costo ÷ (1 − margen %). Precio de venta = precio antes de IVA + IVA.</p>
                  </div>
                  <p className="text-xs text-muted-foreground">Al crear el producto, costo, margen, precio preparado y precio público quedan completos.</p>
                </div>
              </Section>
              <Section id="new-images" icon={Images} title="Imágenes del producto" description="Añade varias imágenes, revisa su vista previa y elige una portada.">
                <PendingProductImagePicker images={pendingImages} onChange={setPendingImages} />
              </Section>
            </div>
          </div>
          <footer className="flex flex-col-reverse gap-3 border-t bg-background px-6 py-4 sm:flex-row sm:items-center sm:justify-between"><p className="text-xs text-muted-foreground">Los cambios de catálogo notifican a los equipos enrolados.</p><div className="flex justify-end gap-2"><Button type="button" variant="outline" onClick={() => onOpenChange(false)}>Cancelar</Button><Button type="button" onClick={() => { setValidationError(undefined); create.mutate(); }} disabled={create.isPending}><Check className="mr-2 h-4 w-4" />{create.isPending ? "Creando…" : "Crear producto"}</Button></div></footer>
        </div>
      </div>
    </DialogContent>
  </Dialog>;
}

export function ProductFormSection({ id, icon: Icon, title, description, children }: { id: string; icon: typeof Boxes; title: string; description: string; children: React.ReactNode }) { return <section id={id} className="scroll-mt-5 rounded-2xl border bg-background p-5 shadow-sm"><div className="mb-5 flex gap-3"><span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary"><Icon className="h-5 w-5" /></span><div><h3 className="font-semibold">{title}</h3><p className="text-sm text-muted-foreground">{description}</p></div></div>{children}</section>; }
const Section = ProductFormSection;
function Field({ label, children, className = "", error }: { label: string; children: React.ReactNode; className?: string; error?: string }) { return <div className={`space-y-2 ${className} ${error ? "[&_[aria-invalid=true]]:border-destructive [&_[aria-invalid=true]]:ring-destructive/20" : ""}`}><Label>{label}</Label>{children}{error && <p className="text-sm text-destructive">{error}</p>}</div>; }
function Toggle({ label, detail, checked, disabled = false, onChange }: { label: string; detail: string; checked: boolean; disabled?: boolean; onChange: (checked: boolean) => void }) { return <div className={`flex items-center justify-between gap-3 rounded-xl border bg-background p-3 ${disabled ? "opacity-60" : ""}`}><div><p className="text-sm font-medium">{label}</p><p className="text-xs text-muted-foreground">{detail}</p></div><Switch checked={checked} disabled={disabled} onCheckedChange={onChange} /></div>; }
function FamilyOption({ label, detail, checked, valueLabel, value, onToggle, onValue }: { label: string; detail: string; checked: boolean; valueLabel: string; value: number | null; onToggle: (checked: boolean) => void; onValue: (value: number | null) => void }) { return <div className="space-y-2"><Toggle label={label} detail={detail} checked={checked} onChange={onToggle} /><Label className="block min-h-10">{valueLabel}</Label><FormattedNumberInput disabled={!checked} value={value ?? ""} onValueChange={onValue} /></div>; }
function MoneyField({ label, kind, value, onChange, error }: { label: string; kind: "currency" | "percent"; value: number; onChange: (value: number) => void; error?: string }) { return <div className={`rounded-xl border bg-background p-4 ${error ? "border-destructive ring-1 ring-destructive/20" : ""}`}><Label>{label}</Label><FormattedNumberInput invalid={Boolean(error)} className="mt-3 h-12 text-lg font-semibold" kind={kind} value={value} onValueChange={(next) => onChange(next ?? 0)} />{error && <p className="mt-2 text-sm text-destructive">{error}</p>}</div>; }
function CreateFormulaValue({ label, value, detail }: { label: string; value: number; detail?: string }) { return <div className="p-3"><p className="text-xs text-emerald-900/70">{label}</p><p className="mt-1 text-lg font-bold">{formatCurrency(value)}</p>{detail && <p className="mt-1 text-xs text-emerald-900/70">{detail}</p>}</div>; }
function CreateFormulaSign({ value }: { value: string }) { return <div className="flex min-w-12 items-center justify-center px-1 py-2 text-center text-xs font-bold text-emerald-800">{value}</div>; }
function categoryChain(categories: Array<{ productCategoryId: string; parentProductCategoryId: string | null; depth: number }>, selectedId: string | null) { const byId = new Map(categories.map((item) => [item.productCategoryId, item])); const result: typeof categories = []; let current = selectedId ? byId.get(selectedId) : undefined; while (current) { result.unshift(current); current = current.parentProductCategoryId ? byId.get(current.parentProductCategoryId) : undefined; } return result; }
