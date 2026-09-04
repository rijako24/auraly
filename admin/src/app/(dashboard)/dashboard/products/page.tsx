"use client";

import { KeyboardEvent, useRef, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, BarChart3, ChevronDown, CircleDollarSign, Images, PackagePlus, Pencil, Power, Search, SlidersHorizontal, Truck, X } from "lucide-react";
import { toast } from "sonner";

import { ProductLearningSection } from "@/components/products/product-learning-section";
import { ProductCreateWorkspace, ProductFormSection } from "@/components/products/product-create-workspace";
import { ProductOverview } from "@/components/products/product-overview";
import { ProductPricingEditor, type ProductPricingEditorHandle } from "@/components/products/product-price-publisher";
import { ProductMerchandisingEditor, type ProductMerchandisingEditorHandle } from "@/components/products/product-merchandising-editor";
import { ProductSupplierEditor, type ProductSupplierEditorHandle } from "@/components/products/product-supplier-editor";
import { ProductTaxEditor, type ProductTaxEditorHandle } from "@/components/products/product-tax-editor";
import { ProductRecognitionSections, type ProductRecognitionSectionsHandle } from "@/components/products/product-recognition-sections";
import { ProductImageEditor, type ProductImageEditorHandle } from "@/components/products/product-image-gallery";
import { DataTable } from "@/components/tables/data-table";
import { ReportViewer } from "@/components/reports/report-viewer";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import {
  useProductConfiguration,
  usePromoteProductAlias,
  useReviewProductAlias,
  useProducts,
  useProductCategories,
  useUpdateProductStatus,
} from "@/hooks/use-products";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import {
  ProductAliasResolutionMode,
  ProductAliasReviewAction,
  type Product,
  type ProductAlias,
  productsApi,
} from "@/services/api/products";
import { productMerchandisingApi } from "@/services/api/product-merchandising";
import { partiesApi } from "@/services/api/parties";
import { useBusinessContextStore } from "@/stores/business-context-store";

interface ProductFormState {
  name: string;
  reference: string;
  description: string;
  categoryName: string;
}

type ModalMode = "details" | "edit";

const emptyForm: ProductFormState = {
  name: "",
  reference: "",
  description: "",
  categoryName: "",
};

function productToForm(product: Product): ProductFormState {
  return {
    name: product.name,
    reference: product.reference ?? product.sku ?? "",
    description: product.description ?? "",
    categoryName: product.categoryName ?? "",
  };
}

type TriState = "all" | "yes" | "no";

function TriStateFilter({ label, value, onChange }: { label: string; value: TriState; onChange: (value: TriState) => void }) {
  return <fieldset className="space-y-2"><legend className="text-sm font-medium">{label}</legend><div className="inline-flex rounded-lg border bg-muted/30 p-1" role="group" aria-label={label}>{(["all", "yes", "no"] as const).map(option => <Button key={option} type="button" size="sm" variant={value === option ? "default" : "ghost"} className="h-8 px-3" onClick={() => onChange(option)}>{option === "all" ? "Todos" : option === "yes" ? "Sí" : "No"}</Button>)}</div></fieldset>;
}

const triValue = (value: TriState): boolean | undefined => value === "all" ? undefined : value === "yes";

export default function ProductsPage() {
  const queryClient = useQueryClient();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(true);
  const [areaId, setAreaId] = useState<string>();
  const [lineId, setLineId] = useState<string>();
  const [groupId, setGroupId] = useState<string>();
  const [subgroupId, setSubgroupId] = useState<string>();
  const [supplierId, setSupplierId] = useState<string>();
  const [brandId, setBrandId] = useState<string>();
  const [managesInventory, setManagesInventory] = useState<TriState>("all");
  const [allowsFractionalSale, setAllowsFractionalSale] = useState<TriState>("all");
  const [isWeighable, setIsWeighable] = useState<TriState>("all");
  const [selectedProduct, setSelectedProduct] = useState<Product | null>(null);
  const [modalMode, setModalMode] = useState<ModalMode>("details");
  const [form, setForm] = useState<ProductFormState>(emptyForm);
  const { data, isLoading, isError, refetch } = useProducts({
    page,
    pageSize: 20,
    search: search || undefined,
    includeInactive,
    areaId, lineId, groupId, subgroupId, supplierId, brandId,
    managesInventory: triValue(managesInventory),
    allowsFractionalSale: triValue(allowsFractionalSale),
    isWeighable: triValue(isWeighable),
  });
  const categoriesQuery = useProductCategories(false);
  const brandsQuery = useQuery({ queryKey: ["product-brands"], queryFn: productMerchandisingApi.brands, staleTime: 5 * 60 * 1000 });
  const suppliersQuery = useQuery({
    queryKey: ["product-filter-suppliers", businessId], enabled: !!businessId, staleTime: 5 * 60 * 1000,
    queryFn: async () => { const items: Awaited<ReturnType<typeof partiesApi.page>>["items"] = []; let current = 1; let totalPages = 1; do { const result = await partiesApi.page({ page: current, pageSize: 200, role: "Supplier", isActive: true }); items.push(...result.items); totalPages = result.totalPages; current += 1; } while (current <= totalPages); return items.filter(item => item.supplierId).sort((left, right) => left.displayName.localeCompare(right.displayName, "es", { sensitivity: "base" })); },
  });
  const categories = categoriesQuery.data ?? [];
  const areas = categories.filter(item => item.depth === 0);
  const lines = categories.filter(item => item.depth === 1 && (!areaId || item.parentProductCategoryId === areaId));
  const groups = categories.filter(item => item.depth === 2 && (!lineId || item.parentProductCategoryId === lineId));
  const subgroups = categories.filter(item => item.depth === 3 && (!groupId || item.parentProductCategoryId === groupId));
  const activeFilterCount = [search.trim() || undefined, areaId, lineId, groupId, subgroupId, supplierId, brandId, managesInventory !== "all" ? managesInventory : undefined, allowsFractionalSale !== "all" ? allowsFractionalSale : undefined, isWeighable !== "all" ? isWeighable : undefined, includeInactive ? undefined : "active"].filter(Boolean).length;
  const resetFilters = () => { setSearch(""); setAreaId(undefined); setLineId(undefined); setGroupId(undefined); setSubgroupId(undefined); setSupplierId(undefined); setBrandId(undefined); setManagesInventory("all"); setAllowsFractionalSale("all"); setIsWeighable("all"); setIncludeInactive(true); setPage(1); };
  const configurationQuery = useProductConfiguration(selectedProduct?.productId);
  const rotationQuery = useQuery({
    queryKey: ["product-rotation", businessId, selectedProduct?.productId],
    queryFn: () => productsApi.rotation(selectedProduct!.productId),
    enabled: !!businessId && !!selectedProduct && modalMode === "details",
  });
  const reviewAlias = useReviewProductAlias();
  const promoteAlias = usePromoteProductAlias();
  const updateStatus = useUpdateProductStatus();
  const merchandisingEditorRef = useRef<ProductMerchandisingEditorHandle>(null);
  const pricingEditorRef = useRef<ProductPricingEditorHandle>(null);
  const imageEditorRef = useRef<ProductImageEditorHandle>(null);
  const taxEditorRef = useRef<ProductTaxEditorHandle>(null);
  const supplierEditorRef = useRef<ProductSupplierEditorHandle>(null);
  const recognitionEditorRef = useRef<ProductRecognitionSectionsHandle>(null);
  const [savingProduct, setSavingProduct] = useState(false);
  const [productValidationError, setProductValidationError] = useState<string>();
  const [editingSalesTaxRate, setEditingSalesTaxRate] = useState<number>();
  const [reportRows,setReportRows]=useState<Array<Record<string,string|number>>|null>(null);
  const [loadingReport,setLoadingReport]=useState(false);

  const openReport=async()=>{if(!businessId||loadingReport)return;setLoadingReport(true);try{const products:Product[]=[];let next=1,totalPages=1;do{const result=await productsApi.list(businessId,{page:next,pageSize:200,includeInactive});products.push(...result.items);totalPages=result.totalPages;next+=1}while(next<=totalPages);setReportRows(products.sort((left,right)=>left.name.localeCompare(right.name,"es",{sensitivity:"base"})).map(product=>({id:product.productId,code:product.productCode??product.sku??"Sin código",description:product.name,price:product.unitPrice,currency:product.currency||"COP"})))}catch{toast.error("No fue posible cargar todos los productos para el reporte.")}finally{setLoadingReport(false)}};

  const openDetails = (product: Product) => {
    setSelectedProduct(product);
    setModalMode("details");
  };

  const openEditing = (product: Product) => {
    setSelectedProduct(product);
    setForm(productToForm(product));
    setProductValidationError(undefined);
    setEditingSalesTaxRate(undefined);
    setModalMode("edit");
  };

  const closeModal = () => {
    setSelectedProduct(null);
    setEditingSalesTaxRate(undefined);
    setModalMode("details");
  };

  const beginEditing = () => {
    if (!selectedProduct) return;
    setForm(productToForm(selectedProduct));
    setEditingSalesTaxRate(undefined);
    setModalMode("edit");
  };

  const handleReviewLearning = async (
    alias: ProductAlias,
    action: ProductAliasReviewAction,
    resolutionMode: ProductAliasResolutionMode
  ) => {
    if (!selectedProduct) return;
    try {
      await reviewAlias.mutateAsync({
        productId: selectedProduct.productId,
        productAliasId: alias.productAliasId,
        request: { action, resolutionMode },
      });
      toast.success(action === ProductAliasReviewAction.Approve ? "Aprendizaje aprobado" : "Aprendizaje rechazado");
    } catch {
      toast.error("No se pudo actualizar el aprendizaje. Revisa si existe un conflicto con otro producto.");
    }
  };

  const handlePromoteLearning = async (
    alias: ProductAlias,
    resolutionMode: ProductAliasResolutionMode
  ) => {
    if (!selectedProduct) return;
    try {
      await promoteAlias.mutateAsync({
        productId: selectedProduct.productId,
        productAliasId: alias.productAliasId,
        request: { resolutionMode },
      });
      toast.success("Aprendizaje promovido al alcance global");
    } catch {
      toast.error("No se pudo promover el aprendizaje. Revisa si existe un conflicto global.");
    }
  };

  const toggleStatus = async (product: Product) => {
    try {
      await updateStatus.mutateAsync({
        productId: product.productId,
        isActive: !product.isActive,
      });
      if (selectedProduct?.productId === product.productId) {
        setSelectedProduct({ ...product, isActive: !product.isActive });
      }
      toast.success(product.isActive ? "Producto desactivado" : "Producto activado");
    } catch {
      toast.error("No se pudo actualizar el producto");
    }
  };

  const saveProduct = async () => {
    if (!selectedProduct || savingProduct) return;
    setProductValidationError(undefined);
    if (!form.name.trim()) {
      const message = "Este campo es requerido";
      setProductValidationError(message);
      toast.error(message);
      requestAnimationFrame(() => document.getElementById("product-name")?.focus());
      return;
    }
    setSavingProduct(true);
    try {
      if (!businessId || !merchandisingEditorRef.current || !taxEditorRef.current
          || !pricingEditorRef.current || !supplierEditorRef.current)
        throw new Error("Espera a que termine de cargar la ficha del producto.");
      const merchandising = merchandisingEditorRef.current.getValue();
      const taxes = taxEditorRef.current.getValue();
      const pricing = pricingEditorRef.current.getValue();
      const supplier = supplierEditorRef.current.getValue();
      const images = imageEditorRef.current ? await imageEditorRef.current.stage() : [];
      const aliases = recognitionEditorRef.current?.getValue().map((alias) => ({ alias })) ?? [];
      await productsApi.updateCatalog(selectedProduct.productId, {
        businessId,
        productCode: selectedProduct.productCode ?? selectedProduct.sku ?? "",
        reference: form.reference.trim() || null,
        name: form.name.trim(),
        description: form.description.trim() || null,
        baseUnitCode: merchandising.baseUnitCode,
        taxProfileId: taxes.salesTaxProfileId,
        purchaseTaxProfileId: taxes.purchaseTaxProfileId,
        purchaseTaxTreatment: taxes.purchaseTaxTreatment,
        manageInventory: merchandising.manageInventory,
        isWeighable: merchandising.isWeighable,
        barcodes: merchandising.barcodes,
        identifiers: [],
        prices: [{
          amount: pricing.publicAmount,
          preparedAmount: pricing.amount,
          currencyCode: selectedProduct.currency || "COP",
          costBasisAmount: pricing.costBasisAmount,
          targetMarginPercent: pricing.targetMarginPercent,
          inputMode: pricing.inputMode,
          roundingIncrement: pricing.roundingIncrement,
          roundingMode: pricing.roundingMode,
        }],
        suppliers: [{ ...supplier, baseUnitCost: pricing.costBasisAmount, isPrimary: true }],
        scale: merchandising.scale,
        productCategoryId: merchandising.productCategoryId,
        productBrandId: merchandising.productBrandId,
        allowsFractionalSale: merchandising.allowsFractionalSale,
        link: merchandising.link,
        linkedProducts: merchandising.linkedProducts,
        conversionMaximumLossPercent: merchandising.conversionMaximumLossPercent,
        aliases,
        images,
      });
      await Promise.all([
        refetch(),
        queryClient.invalidateQueries({ queryKey: ["product-merchandising"] }),
      ]);
      setProductValidationError(undefined);
      toast.success("Producto guardado completamente");
      closeModal();
    } catch (error) {
      const message = error instanceof Error ? error.message : "No se pudo guardar el producto";
      setProductValidationError(message);
      toast.error(message);
    } finally {
      setSavingProduct(false);
    }
  };
  const columns: ColumnDef<Product>[] = [
    {
      accessorKey: "name",
      header: "Producto",
      cell: ({ row }) => (
        <div>
          <p className="font-medium">{row.original.name}</p>
          <p className="text-xs text-muted-foreground">{row.original.reference || row.original.sku || "Sin referencia"}</p>
          <p className="max-w-md truncate text-xs text-muted-foreground">{row.original.description || "Sin descripción"}</p>
        </div>
      ),
    },
    {
      accessorKey: "areaName",
      header: "Área",
      cell: ({ row }) => row.original.areaName || "Sin área",
    },
    {
      accessorKey: "unitPrice",
      header: "Precio",
      cell: ({ row }) => formatCurrency(row.original.unitPrice, row.original.currency || "COP"),
    },
    {
      accessorKey: "stockQuantity",
      header: "Inventario",
      cell: ({ row }) =>
        row.original.manageStock ? (
          `${row.original.stockQuantity ?? 0} unidades`
        ) : (
          <span className="text-muted-foreground">No controlado</span>
        ),
    },
    {
      accessorKey: "isActive",
      header: "Estado",
      cell: ({ row }) => (
        <Badge variant={row.original.isActive ? "default" : "secondary"}>
          {row.original.isActive ? "Activo" : "Inactivo"}
        </Badge>
      ),
    },
    {
      id: "actions",
      header: "",
      cell: ({ row }) => (
        <div className="flex justify-end gap-1" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
          <Button type="button" variant="ghost" size="sm" onClick={() => openEditing(row.original)}>
            <Pencil className="mr-2 h-4 w-4" />Editar
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={updateStatus.isPending}
            onClick={() => toggleStatus(row.original)}
          >
            <Power className="mr-2 h-4 w-4" />
            {row.original.isActive ? "Desactivar" : "Activar"}
          </Button>
        </div>
      ),
    },
  ];

  const handleCardKeyDown = (event: KeyboardEvent<HTMLElement>, product: Product) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      openDetails(product);
    }
  };

  const renderProductCard = (product: Product) => (
    <article
      className="cursor-pointer space-y-4 rounded-xl border bg-card p-4 shadow-sm transition-colors hover:bg-muted/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      role="button"
      tabIndex={0}
      onClick={() => openDetails(product)}
      onKeyDown={(event) => handleCardKeyDown(event, product)}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="break-words font-semibold">{product.name}</h2>
          <p className="text-xs text-muted-foreground">{product.reference || product.sku || "Sin referencia"}</p>
          <p className="mt-2 line-clamp-3 text-sm text-muted-foreground">{product.description || "Sin descripción"}</p>
        </div>
        <Badge variant={product.isActive ? "default" : "secondary"}>
          {product.isActive ? "Activo" : "Inactivo"}
        </Badge>
      </div>
      <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
        <div>
          <dt className="text-xs text-muted-foreground">Área</dt>
          <dd className="break-words">{product.areaName || "Sin área"}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Precio</dt>
          <dd className="font-medium">{formatCurrency(product.unitPrice, product.currency || "COP")}</dd>
        </div>
      </dl>
      <div className="flex justify-end border-t pt-3" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
        <Button type="button" variant="ghost" size="sm" onClick={() => openEditing(product)}>
          <Pencil className="mr-2 h-4 w-4" />Editar
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={updateStatus.isPending}
          onClick={() => toggleStatus(product)}
        >
          <Power className="mr-2 h-4 w-4" />
          {product.isActive ? "Desactivar" : "Activar"}
        </Button>
      </div>
    </article>
  );

  if(reportRows)return <div className="space-y-4"><Button variant="ghost" onClick={()=>setReportRows(null)}><ArrowLeft className="mr-2 h-4 w-4"/>Volver a productos</Button><ReportViewer onClose={()=>setReportRows(null)} title="Productos" description={`${reportRows.length.toLocaleString("es-CO")} productos · orden alfabético`} fileName="productos" rows={reportRows} columns={[{key:"code",label:"Código interno"},{key:"description",label:"Descripción"},{key:"price",label:"Precio público",align:"right",format:(value,row)=>formatCurrency(Number(value),String(row.currency||"COP"))}]}/></div>;

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Productos</h1>
        <p className="text-muted-foreground">
          Catálogo sincronizado para ventas, pedidos y recomendaciones del agente.
        </p>
      </div>
      <div className="flex flex-wrap justify-end gap-2">
        <Button type="button" size="lg" variant="outline" disabled={loadingReport} onClick={openReport}><BarChart3 className="mr-2 h-5 w-5" />{loadingReport?"Cargando reporte…":"Reporte de productos"}</Button><Button type="button" size="lg" onClick={() => setCreateOpen(true)}><PackagePlus className="mr-2 h-5 w-5" />Nuevo producto</Button>
      </div>


      <details className="group rounded-xl border bg-card">
        <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3">
          <span className="flex items-center gap-2 font-medium"><SlidersHorizontal className="h-4 w-4"/>Filtros {activeFilterCount > 0 && <Badge variant="secondary">{activeFilterCount} activos</Badge>}</span>
          <ChevronDown className="h-4 w-4 transition-transform group-open:rotate-180"/>
        </summary>
        <div className="space-y-5 border-t p-4">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <div className="space-y-2 md:col-span-2 xl:col-span-4"><Label htmlFor="product-filter-search">Producto o SKU</Label><div className="relative">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input
            id="product-filter-search"
            className="pl-9"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
            placeholder="Buscar por nombre o SKU"
          />
            </div></div>
            {([['Área', areaId, areas, (value:string|undefined)=>{setAreaId(value);setLineId(undefined);setGroupId(undefined);setSubgroupId(undefined)}],['Línea', lineId, lines, (value:string|undefined)=>{setLineId(value);setGroupId(undefined);setSubgroupId(undefined)}],['Grupo', groupId, groups, (value:string|undefined)=>{setGroupId(value);setSubgroupId(undefined)}],['Subgrupo', subgroupId, subgroups, setSubgroupId]] as const).map(([label,value,options,onChange])=><div key={label} className="space-y-2"><Label>{label}</Label><Select value={value ?? "all"} onValueChange={next=>{onChange(next === "all" ? undefined : next);setPage(1)}}><SelectTrigger><SelectValue placeholder={`Todos los ${label.toLocaleLowerCase("es")}`}/></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem>{options.map(option=><SelectItem key={option.productCategoryId} value={option.productCategoryId}>{option.name}</SelectItem>)}</SelectContent></Select></div>)}
            <div className="space-y-2"><Label>Proveedor</Label><Select value={supplierId ?? "all"} onValueChange={value=>{setSupplierId(value === "all" ? undefined : value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todos"/></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem>{suppliersQuery.data?.map(item=><SelectItem key={item.supplierId!} value={item.supplierId!}>{item.displayName}</SelectItem>)}</SelectContent></Select></div>
            <div className="space-y-2"><Label>Marca</Label><Select value={brandId ?? "all"} onValueChange={value=>{setBrandId(value === "all" ? undefined : value);setPage(1)}}><SelectTrigger><SelectValue placeholder="Todas"/></SelectTrigger><SelectContent><SelectItem value="all">Todas</SelectItem>{brandsQuery.data?.map(item=><SelectItem key={item.productBrandId} value={item.productBrandId}>{item.name}</SelectItem>)}</SelectContent></Select></div>
          </div>
          <div className="flex flex-wrap gap-5"><TriStateFilter label="Controla inventario" value={managesInventory} onChange={value=>{setManagesInventory(value);setPage(1)}}/><TriStateFilter label="Permite venta fraccionada" value={allowsFractionalSale} onChange={value=>{setAllowsFractionalSale(value);setPage(1)}}/><TriStateFilter label="Producto de balanza" value={isWeighable} onChange={value=>{setIsWeighable(value);setPage(1)}}/></div>
          <div className="flex flex-wrap items-center justify-between gap-3 border-t pt-4"><label className="flex items-center gap-2 text-sm">
          <Switch
            checked={includeInactive}
            onCheckedChange={(checked) => {
              setIncludeInactive(checked);
              setPage(1);
            }}
          />
          Incluir inactivos
          </label><Button type="button" variant="ghost" disabled={activeFilterCount === 0} onClick={resetFilters}><X className="mr-2 h-4 w-4"/>Limpiar filtros</Button></div>
        </div>
      </details>

      {isError ? (
        <div className="rounded-xl border border-destructive/30 p-6 text-sm">
          No se pudo cargar el catálogo. <Button variant="link" onClick={() => refetch()}>Reintentar</Button>
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={data?.items ?? []}
          isLoading={isLoading}
          page={data?.page}
          pageSize={data?.pageSize}
          pageCount={data?.totalPages}
          totalItems={data?.totalCount}
          onPaginationChange={(nextPage) => setPage(nextPage)}
          onRowClick={openDetails}
          searchKey="name"
          searchPlaceholder="Buscar en esta página..."
          enableRowSelection={false}
          cardRenderer={renderProductCard}
        />
      )}

      <Dialog open={selectedProduct !== null} onOpenChange={(open) => !open && closeModal()}>
        <DialogContent className="h-[96dvh] max-h-[96dvh] w-[96vw] max-w-[1480px] overflow-hidden p-0">
          {selectedProduct && (
            <div className="grid h-full min-h-0 lg:grid-cols-[250px_1fr]">
              <aside className="hidden border-r bg-slate-950 p-6 text-white lg:block">
                <div className="sticky top-0">
                  <p className="text-xs font-bold uppercase tracking-[.2em] text-teal-300">{modalMode === "edit" ? "Editar producto" : "Ficha del producto"}</p>
                  <h2 className="mt-2 text-2xl font-semibold">Una ficha, todo conectado</h2>
                  <p className="mt-2 text-sm text-slate-300">Consulta y modifica la información conservando siempre el mismo orden.</p>
                  <nav className="mt-8 space-y-1">
                    {[
                      ["identity", "Identidad"],
                      ["classification", "Clasificación, marca y unidad"],
                      ["sale", "Captura, cantidad y balanza"],
                      ["family", "Familia de productos"],
                      ["supplier", "Proveedor y empaque"],
                      ["taxes", "IVA, costo y precio"],
                      ["images", "Imágenes"],
                      ["recognition", "Reconocimiento avanzado"],
                    ].map(([id, label], index) => <a key={id} href={`#product-${id}`} className="block rounded-xl px-3 py-2.5 text-sm text-slate-200 hover:bg-white/10">{index + 1}. {label}</a>)}
                  </nav>
                </div>
              </aside>
              <div className="flex min-h-0 flex-col bg-muted/20">
              <DialogHeader className="border-b bg-background px-6 py-5 pr-14">
                <div className="flex flex-wrap items-center justify-between gap-3 pr-8">
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <DialogTitle className="text-2xl">{selectedProduct.name}</DialogTitle>
                      <Badge variant={selectedProduct.isActive ? "default" : "secondary"}>{selectedProduct.isActive ? "Activo" : "Inactivo"}</Badge>
                    </div>
                    <DialogDescription className="mt-1">Una sola ficha para consultar y administrar el producto, sin saltar entre ventanas.</DialogDescription>
                  </div>
                  <div className="flex gap-2">
                    {modalMode === "details" && <Button type="button" onClick={beginEditing}><Pencil className="mr-2 h-4 w-4" />Editar información</Button>}
                    <Button type="button" variant="outline" disabled={updateStatus.isPending} onClick={() => void toggleStatus(selectedProduct)}>
                      <Power className="mr-2 h-4 w-4" />{selectedProduct.isActive ? "Desactivar" : "Activar"}
                    </Button>
                  </div>
                </div>
              </DialogHeader>

              <div className="min-h-0 flex-1 overflow-y-auto scroll-smooth p-4 sm:p-6">
              {modalMode !== "edit" && <div className="mx-auto w-full max-w-5xl space-y-5"><ProductOverview product={selectedProduct} />
                <section className="rounded-xl border bg-background p-5"><div className="flex items-start justify-between gap-3"><div><h3 className="font-semibold">Rotación por bodega</h3><p className="text-sm text-muted-foreground">Información calculada y almacenada por Reportes; no se edita manualmente.</p></div><BarChart3 className="h-5 w-5 text-primary"/></div>
                  {rotationQuery.isLoading&&<p className="mt-4 text-sm text-muted-foreground">Cargando rotación…</p>}
                  {!rotationQuery.isLoading&&(rotationQuery.data?.length??0)===0&&<p className="mt-4 rounded-lg bg-muted/40 p-4 text-sm text-muted-foreground">Todavía no hay ventas proyectadas para este producto en la sede.</p>}
                  <div className="mt-4 grid gap-3 md:grid-cols-2">{rotationQuery.data?.map(item=><article key={item.warehouseId} className="rounded-xl border p-4"><h4 className="font-medium">{item.warehouseName} · {item.warehouseCode}</h4><dl className="mt-3 grid grid-cols-2 gap-3 text-sm"><div><dt className="text-muted-foreground">Rotación neta 30 días</dt><dd className="text-lg font-semibold">{item.netUnitsSold30Days}</dd></div><div><dt className="text-muted-foreground">Rotación neta 90 días</dt><dd className="text-lg font-semibold">{item.netUnitsSold90Days}</dd></div><div><dt className="text-muted-foreground">Demanda diaria</dt><dd>{item.dailyDemand90Days.toFixed(3)}</dd></div><div><dt className="text-muted-foreground">Cobertura</dt><dd>{item.coverageDays==null?"Sin demanda":`${item.coverageDays.toFixed(1)} días`}</dd></div><div><dt className="text-muted-foreground">Disponible</dt><dd>{item.quantityOnHand}</dd></div><div><dt className="text-muted-foreground">En órdenes abiertas</dt><dd>{item.incomingQuantity}</dd></div></dl><p className="mt-3 text-xs text-muted-foreground">Corte {item.windowEndDate} · actualizado {formatDateTime(item.calculatedAt)}</p></article>)}</div>
                </section><details id="product-recognition" className="group scroll-mt-5 rounded-xl border bg-background"><summary className="cursor-pointer list-none p-5 font-semibold">Reconocimiento, alias y aprendizaje <span className="ml-2 text-xs font-normal text-muted-foreground">Información avanzada</span></summary><div className="space-y-5 border-t p-5"><ProductRecognitionSections productId={selectedProduct.productId} aliases={configurationQuery.data?.aliases ?? []} searchTerms={configurationQuery.data?.searchTerms ?? []} isLoading={configurationQuery.isLoading} isError={configurationQuery.isError} /><ProductLearningSection aliases={configurationQuery.data?.aliases ?? []} isLoading={configurationQuery.isLoading} isError={configurationQuery.isError} isPending={reviewAlias.isPending || promoteAlias.isPending} onReview={handleReviewLearning} onPromote={handlePromoteLearning} /></div></details></div>}

              {modalMode === "edit" && <div className="mx-auto w-full max-w-5xl space-y-5">
                {productValidationError && <div role="alert" className="rounded-xl border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive"><strong>No se puede guardar todavía.</strong> {productValidationError}</div>}
                <ProductFormSection id="product-identity" icon={PackagePlus} title="Identidad" description="Lo que el equipo vera al buscar y vender.">
                  <div className="grid gap-4 md:grid-cols-2">
                    <div className="space-y-2 md:col-span-2"><Label htmlFor="product-name">Nombre <span className="text-destructive">*</span></Label><Input id="product-name" aria-invalid={Boolean(productValidationError && !form.name.trim())} className={productValidationError && !form.name.trim() ? "border-destructive ring-1 ring-destructive/20" : ""} value={form.name} maxLength={200} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} />{productValidationError && !form.name.trim() && <p className="text-sm text-destructive">Este campo es requerido</p>}</div>
                    <div className="space-y-2 md:col-span-2"><Label htmlFor="product-reference">Referencia</Label><Input id="product-reference" value={form.reference} maxLength={120} onChange={(event) => setForm((current) => ({ ...current, reference: event.target.value }))} placeholder="Referencia del fabricante" /></div>
                    <div className="space-y-2 md:col-span-2"><Label htmlFor="product-description">Descripción</Label><Textarea id="product-description" className="min-h-24" value={form.description} maxLength={2000} onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))} /></div>
                  </div>
                </ProductFormSection>

                <ProductMerchandisingEditor ref={merchandisingEditorRef} embedded productId={selectedProduct.productId} />

                <ProductFormSection id="product-supplier" icon={Truck} title="Proveedor principal y empaque habitual" description="Requerido. Permite recibir por caja, bulto o paquete y convertir a la unidad del producto.">
                  <ProductSupplierEditor ref={supplierEditorRef} embedded productId={selectedProduct.productId} productName={selectedProduct.name} />
                </ProductFormSection>

                <ProductFormSection id="product-taxes" icon={CircleDollarSign} title="IVA, costo y precio" description="El IVA se incluye en el precio de venta; publicar sigue siendo una decisión explícita.">
                  <div className="space-y-5">
                    <ProductTaxEditor ref={taxEditorRef} embedded productId={selectedProduct.productId} onSalesTaxRateChange={setEditingSalesTaxRate} />
                    <ProductPricingEditor ref={pricingEditorRef} embedded productId={selectedProduct.productId} productName={selectedProduct.name} salesTaxRateOverride={editingSalesTaxRate} />
                  </div>
                </ProductFormSection>
                <ProductFormSection id="product-images" icon={Images} title="Imágenes del producto" description="Los archivos se transfieren al almacenamiento y su metadata se confirma con el único guardado del producto.">
                  <ProductImageEditor ref={imageEditorRef} productId={selectedProduct.productId} />
                </ProductFormSection>
                <details id="product-recognition" className="group scroll-mt-5 rounded-xl border bg-muted/10">
                  <summary className="cursor-pointer list-none p-5 font-semibold">Reconocimiento, alias y aprendizaje <span className="ml-2 text-xs font-normal text-muted-foreground">Información avanzada</span></summary>
                  <div className="space-y-5 border-t p-5"><ProductRecognitionSections ref={recognitionEditorRef} productId={selectedProduct.productId} editable aliases={configurationQuery.data?.aliases ?? []} searchTerms={configurationQuery.data?.searchTerms ?? []} isLoading={configurationQuery.isLoading} isError={configurationQuery.isError} /><ProductLearningSection aliases={configurationQuery.data?.aliases ?? []} isLoading={configurationQuery.isLoading} isError={configurationQuery.isError} isPending={reviewAlias.isPending || promoteAlias.isPending} onReview={handleReviewLearning} onPromote={handlePromoteLearning} /></div>
                </details>
              </div>}
              </div>
              <footer className="flex flex-col-reverse gap-3 border-t bg-background px-6 py-4 sm:flex-row sm:items-center sm:justify-between">
                <p className="text-xs text-muted-foreground">{modalMode === "edit" ? "Una sola acción guarda toda la ficha del producto." : "Toda la información se presenta en el mismo orden usado al crear y editar."}</p>
                <div className="flex justify-end gap-2">
                  <Button type="button" variant="outline" onClick={closeModal}>{modalMode === "edit" ? "Cancelar" : "Cerrar"}</Button>
                  {modalMode === "edit" && <Button type="button" onClick={() => void saveProduct()} disabled={savingProduct}>{savingProduct ? "Guardando producto…" : "Guardar producto"}</Button>}
                </div>
              </footer>
              </div>
            </div>
          )}
        </DialogContent>
      </Dialog>
      <ProductCreateWorkspace open={createOpen} onOpenChange={setCreateOpen} />

    </div>
  );
}
