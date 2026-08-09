"use client";

import { FormEvent, KeyboardEvent, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, Power, Search } from "lucide-react";
import { toast } from "sonner";

import { ProductLearningSection } from "@/components/products/product-learning-section";
import { ProductOffersSection } from "@/components/products/product-offers-section";
import { ProductRecognitionSections } from "@/components/products/product-recognition-sections";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
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
} from "@/services/api/products";

interface ProductFormState {
  name: string;
  description: string;
  categoryName: string;
  unitPrice: string;
  currency: string;
}

type ModalMode = "details" | "edit";

const emptyForm: ProductFormState = {
  name: "",
  description: "",
  categoryName: "",
  unitPrice: "0",
  currency: "COP",
};

function productToForm(product: Product): ProductFormState {
  return {
    name: product.name,
    description: product.description ?? "",
    categoryName: product.categoryName ?? "",
    unitPrice: String(product.unitPrice),
    currency: product.currency || "COP",
  };
}

export default function ProductsPage() {
  const [page, setPage] = useState(1);
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
      const updated = await updateStatus.mutateAsync({
        productId: product.productId,
        isActive: !product.isActive,
      });
      if (selectedProduct?.productId === product.productId) setSelectedProduct(updated);
      toast.success(product.isActive ? "Producto desactivado" : "Producto activado");
    } catch {
      toast.error("No se pudo actualizar el producto");
    }
  };

  const saveProduct = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selectedProduct) return;

    const unitPrice = Number(form.unitPrice);
    if (!form.name.trim()) {
      toast.error("El nombre del producto es obligatorio");
      return;
    }
    if (!Number.isFinite(unitPrice) || unitPrice < 0) {
      toast.error("Ingresa un precio válido");
      return;
    }
    if (!/^[A-Za-z]{3}$/.test(form.currency.trim())) {
      toast.error("La moneda debe tener tres letras, por ejemplo COP");
      return;
    }

    try {
      const updated = await updateProduct.mutateAsync({
        productId: selectedProduct.productId,
        request: {
          name: form.name.trim(),
          description: form.description.trim() || null,
          categoryName: form.categoryName.trim() || null,
          unitPrice,
          currency: form.currency.trim().toUpperCase(),
        },
      });
      setSelectedProduct(updated);
      setModalMode("details");
      toast.success("Producto actualizado");
    } catch {
      toast.error("No se pudo guardar el producto");
    }
  };

  const columns: ColumnDef<Product>[] = [
    {
      accessorKey: "name",
      header: "Producto",
      cell: ({ row }) => (
        <div>
          <p className="font-medium">{row.original.name}</p>
          <p className="text-xs text-muted-foreground">{row.original.sku || "Sin SKU"}</p>
        </div>
      ),
    },
    {
      accessorKey: "categoryName",
      header: "Categoría",
      cell: ({ row }) => row.original.categoryName || "Sin categoría",
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
          <p className="text-xs text-muted-foreground">{product.sku || "Sin SKU"}</p>
        </div>
        <Badge variant={product.isActive ? "default" : "secondary"}>
          {product.isActive ? "Activo" : "Inactivo"}
        </Badge>
      </div>
      <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-sm">
        <div>
          <dt className="text-xs text-muted-foreground">Categoría</dt>
          <dd className="break-words">{product.categoryName || "Sin categoría"}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Precio</dt>
          <dd className="font-medium">{formatCurrency(product.unitPrice, product.currency || "COP")}</dd>
        </div>
      </dl>
      {product.description && (
        <p className="line-clamp-3 text-sm text-muted-foreground">{product.description}</p>
      )}
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

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Productos</h1>
        <p className="text-muted-foreground">
          Catálogo sincronizado para ventas, pedidos y recomendaciones del agente.
        </p>
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
        <DialogContent className="max-h-[100dvh] overflow-y-auto sm:max-h-[90vh] sm:max-w-3xl">
          {selectedProduct && modalMode === "details" ? (
            <div className="space-y-6">
              <DialogHeader>
                <div className="flex flex-wrap items-center gap-2 pr-8">
                  <DialogTitle>{selectedProduct.name}</DialogTitle>
                  <Badge variant={selectedProduct.isActive ? "default" : "secondary"}>
                    {selectedProduct.isActive ? "Activo" : "Inactivo"}
                  </Badge>
                </div>
                <DialogDescription>
                  Información comercial y configuración usada para encontrar este producto.
                </DialogDescription>
              </DialogHeader>

              <section className="space-y-3">
                <h3 className="text-sm font-semibold">Información del producto</h3>
                <dl className="grid gap-3 rounded-xl border bg-muted/15 p-4 text-sm sm:grid-cols-2 lg:grid-cols-4">
                  <div>
                    <dt className="text-xs text-muted-foreground">Precio</dt>
                    <dd className="font-medium">{formatCurrency(selectedProduct.unitPrice, selectedProduct.currency || "COP")}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-muted-foreground">Categoría</dt>
                    <dd>{selectedProduct.categoryName || "Sin categoría"}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-muted-foreground">SKU</dt>
                    <dd>{selectedProduct.sku || "Sin SKU"}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-muted-foreground">Inventario</dt>
                    <dd>{selectedProduct.manageStock ? `${selectedProduct.stockQuantity ?? 0} unidades` : "No controlado"}</dd>
                  </div>
                </dl>
                <div className="rounded-xl border p-4">
                  <p className="text-xs font-medium text-muted-foreground">Descripción</p>
                  <p className="mt-1 whitespace-pre-wrap text-sm">
                    {selectedProduct.description || "Este producto no tiene descripción."}
                  </p>
                </div>
              </section>

              <ProductOffersSection productId={selectedProduct.productId} />

              <ProductRecognitionSections
                aliases={configurationQuery.data?.aliases ?? []}
                searchTerms={configurationQuery.data?.searchTerms ?? []}
                isLoading={configurationQuery.isLoading}
                isError={configurationQuery.isError}
              />

              <ProductLearningSection
                aliases={configurationQuery.data?.aliases ?? []}
                isLoading={configurationQuery.isLoading}
                isError={configurationQuery.isError}
                isPending={reviewAlias.isPending || promoteAlias.isPending}
                onReview={handleReviewLearning}
                onPromote={handlePromoteLearning}
              />
              <DialogFooter className="gap-2">
                <Button type="button" variant="outline" onClick={closeModal}>Cerrar</Button>
                <Button type="button" onClick={beginEditing}>
                  <Pencil className="mr-2 h-4 w-4" /> Editar producto
                </Button>
              </DialogFooter>
            </div>
          ) : selectedProduct ? (
            <form className="space-y-5" onSubmit={saveProduct}>
              <DialogHeader>
                <DialogTitle>Editar producto</DialogTitle>
                <DialogDescription>
                  El índice se regenerará solo si cambias nombre, categoría o descripción.
                </DialogDescription>
              </DialogHeader>

              <div className="space-y-2">
                <Label htmlFor="product-name">Nombre</Label>
                <Input
                  id="product-name"
                  value={form.name}
                  maxLength={200}
                  onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))}
                  required
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="product-category">Categoría</Label>
                <Input
                  id="product-category"
                  value={form.categoryName}
                  maxLength={150}
                  placeholder="Ej. Cuidado personal"
                  onChange={(event) => setForm((current) => ({ ...current, categoryName: event.target.value }))}
                />
              </div>

              <div className="grid grid-cols-[minmax(0,1fr)_7rem] gap-3">
                <div className="space-y-2">
                  <Label htmlFor="product-price">Precio</Label>
                  <Input
                    id="product-price"
                    type="number"
                    min="0"
                    step="0.01"
                    inputMode="decimal"
                    value={form.unitPrice}
                    onChange={(event) => setForm((current) => ({ ...current, unitPrice: event.target.value }))}
                    required
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="product-currency">Moneda</Label>
                  <Input
                    id="product-currency"
                    value={form.currency}
                    maxLength={3}
                    autoCapitalize="characters"
                    onChange={(event) => setForm((current) => ({ ...current, currency: event.target.value.toUpperCase() }))}
                    required
                  />
                </div>
              </div>

              <div className="space-y-2">
                <Label htmlFor="product-description">Descripción</Label>
                <Textarea
                  id="product-description"
                  className="min-h-28"
                  value={form.description}
                  maxLength={2000}
                  placeholder="Describe el producto, sus características o presentación."
                  onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
                />
              </div>

              <DialogFooter className="gap-2">
                <Button type="button" variant="outline" onClick={() => setModalMode("details")}>Volver al detalle</Button>
                <Button type="submit" disabled={updateProduct.isPending}>
                  {updateProduct.isPending ? "Guardando..." : "Guardar cambios"}
                </Button>
              </DialogFooter>
            </form>
          ) : null}
        </DialogContent>
      </Dialog>
    </div>
  );
}