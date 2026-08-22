"use client";

import { KeyboardEvent, useRef, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowLeft, BarChart3, CircleDollarSign, Images, PackagePlus, Pencil, Power, Search, Truck } from "lucide-react";
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
import { Textarea } from "@/components/ui/textarea";
import {
  useProductConfiguration,
  usePromoteProductAlias,
  useReviewProductAlias,
  useProducts,
  useUpdateProduct,
  useUpdateProductStatus,
} from "@/hooks/use-products";
import { formatCurrency } from "@/lib/utils";
import {
  ProductAliasResolutionMode,
  ProductAliasReviewAction,
  type Product,
  type ProductAlias,
  productsApi,
} from "@/services/api/products";
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

export default function ProductsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [page, setPage] = useState(1);
  const [createOpen, setCreateOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(true);
  const [selectedProduct, setSelectedProduct] = useState<Product | null>(null);
  const [modalMode, setModalMode] = useState<ModalMode>("details");
  const [form, setForm] = useState<ProductFormState>(emptyForm);
  const { data, isLoading, isError, refetch } = useProducts({
    page,
    pageSize: 20,
    search: search || undefined,
    includeInactive,
  });
  const configurationQuery = useProductConfiguration(selectedProduct?.productId);
  const reviewAlias = useReviewProductAlias();
  const promoteAlias = usePromoteProductAlias();
  const updateProduct = useUpdateProduct();
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

  const openEditor = (product: Product) => {
    setSelectedProduct(product);
    setForm(productToForm(product));
    setModalMode("edit");
  };

  const closeModal = () => {
    setSelectedProduct(null);
    setModalMode("details");
  };

  const beginEditing = () => {
    if (!selectedProduct) return;
    setForm(productToForm(selectedProduct));
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
      const updated = await updateProduct.mutateAsync({
        productId: selectedProduct.productId,
        request: {
          name: form.name.trim(),
          reference: form.reference.trim() || null,
          description: form.description.trim() || null,
          categoryName: selectedProduct.categoryName ?? null,
          unitPrice: selectedProduct.unitPrice,
          currency: selectedProduct.currency || "COP",
        },
      });
      await merchandisingEditorRef.current?.save();
      await taxEditorRef.current?.save();
      await imageEditorRef.current?.save();
      await pricingEditorRef.current?.save();
      await supplierEditorRef.current?.save();
      await recognitionEditorRef.current?.save();
      setSelectedProduct(updated);
      await refetch();
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
          <Button type="button" variant="ghost" size="sm" onClick={() => openEditor(row.original)}>
            <Pencil className="mr-2 h-4 w-4" />
            Editar
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
      <div className="grid grid-cols-2 gap-2 border-t pt-3" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
        <Button type="button" variant="outline" size="sm" onClick={() => openEditor(product)}>
          <Pencil className="mr-2 h-4 w-4" />
          Editar
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


      <div className="flex flex-col gap-3 rounded-xl border bg-card p-4 sm:flex-row sm:items-center">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input
            className="pl-9"
            value={search}
            onChange={(event) => {
              setSearch(event.target.value);
              setPage(1);
            }}
            placeholder="Buscar por nombre o SKU"
          />
        </div>
        <label className="flex items-center gap-2 text-sm">
          <Switch
            checked={includeInactive}
            onCheckedChange={(checked) => {
              setIncludeInactive(checked);
              setPage(1);
            }}
          />
          Incluir inactivos
        </label>
      </div>

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
              {modalMode !== "edit" && <div className="mx-auto w-full max-w-5xl space-y-5"><ProductOverview product={selectedProduct} /><details id="product-recognition" className="group scroll-mt-5 rounded-xl border bg-background"><summary className="cursor-pointer list-none p-5 font-semibold">Reconocimiento, alias y aprendizaje <span className="ml-2 text-xs font-normal text-muted-foreground">Información avanzada</span></summary><div className="space-y-5 border-t p-5"><ProductRecognitionSections productId={selectedProduct.productId} aliases={configurationQuery.data?.aliases ?? []} searchTerms={configurationQuery.data?.searchTerms ?? []} isLoading={configurationQuery.isLoading} isError={configurationQuery.isError} /><ProductLearningSection aliases={configurationQuery.data?.aliases ?? []} isLoading={configurationQuery.isLoading} isError={configurationQuery.isError} isPending={reviewAlias.isPending || promoteAlias.isPending} onReview={handleReviewLearning} onPromote={handlePromoteLearning} /></div></details></div>}

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

                <ProductFormSection id="product-supplier" icon={Truck} title="Proveedor principal y empaque habitual" description="Opcional. Permite recibir por caja, bulto o paquete y convertir a la unidad del producto.">
                  <ProductSupplierEditor ref={supplierEditorRef} embedded productId={selectedProduct.productId} productName={selectedProduct.name} />
                </ProductFormSection>

                <ProductFormSection id="product-taxes" icon={CircleDollarSign} title="IVA, costo y precio" description="El IVA se incluye en el precio de venta; publicar sigue siendo una decisión explícita.">
                  <div className="space-y-5">
                    <ProductTaxEditor ref={taxEditorRef} embedded productId={selectedProduct.productId} onSalesTaxRateChange={setEditingSalesTaxRate} />
                    <ProductPricingEditor ref={pricingEditorRef} embedded productId={selectedProduct.productId} productName={selectedProduct.name} salesTaxRateOverride={editingSalesTaxRate} />
                  </div>
                </ProductFormSection>
                <ProductFormSection id="product-images" icon={Images} title="Imágenes del producto" description="Carga varias imágenes, revisa su vista previa y elige una portada. Se guardarán junto con el producto.">
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
