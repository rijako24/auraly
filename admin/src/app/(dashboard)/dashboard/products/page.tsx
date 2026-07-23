"use client";

import { FormEvent, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, Power, Search } from "lucide-react";
import { toast } from "sonner";

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
import { useProducts, useUpdateProduct, useUpdateProductStatus } from "@/hooks/use-products";
import { formatCurrency } from "@/lib/utils";
import type { Product } from "@/services/api/products";

interface ProductFormState {
  name: string;
  description: string;
  categoryName: string;
  unitPrice: string;
  currency: string;
}

const emptyForm: ProductFormState = {
  name: "",
  description: "",
  categoryName: "",
  unitPrice: "0",
  currency: "COP",
};

export default function ProductsPage() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(true);
  const [editingProduct, setEditingProduct] = useState<Product | null>(null);
  const [form, setForm] = useState<ProductFormState>(emptyForm);
  const { data, isLoading, isError, refetch } = useProducts({
    page,
    pageSize: 20,
    search: search || undefined,
    includeInactive,
  });
  const updateProduct = useUpdateProduct();
  const updateStatus = useUpdateProductStatus();

  const openEditor = (product: Product) => {
    setEditingProduct(product);
    setForm({
      name: product.name,
      description: product.description ?? "",
      categoryName: product.categoryName ?? "",
      unitPrice: String(product.unitPrice),
      currency: product.currency || "COP",
    });
  };

  const toggleStatus = async (product: Product) => {
    try {
      await updateStatus.mutateAsync({
        productId: product.productId,
        isActive: !product.isActive,
      });
      toast.success(product.isActive ? "Producto desactivado" : "Producto activado");
    } catch {
      toast.error("No se pudo actualizar el producto");
    }
  };

  const saveProduct = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!editingProduct) return;

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
      await updateProduct.mutateAsync({
        productId: editingProduct.productId,
        request: {
          name: form.name.trim(),
          description: form.description.trim() || null,
          categoryName: form.categoryName.trim() || null,
          unitPrice,
          currency: form.currency.trim().toUpperCase(),
        },
      });
      toast.success("Producto actualizado");
      setEditingProduct(null);
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
        <div className="flex justify-end gap-1">
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

  const renderProductCard = (product: Product) => (
    <article className="space-y-4 rounded-xl border bg-card p-4 shadow-sm">
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
      <div className="grid grid-cols-2 gap-2 border-t pt-3">
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
          searchKey="name"
          searchPlaceholder="Buscar en esta página..."
          enableRowSelection={false}
          cardRenderer={renderProductCard}
        />
      )}

      <Dialog open={editingProduct !== null} onOpenChange={(open) => !open && setEditingProduct(null)}>
        <DialogContent className="max-h-[100dvh] overflow-y-auto sm:max-h-[90vh] sm:max-w-xl">
          <form className="space-y-5" onSubmit={saveProduct}>
            <DialogHeader>
              <DialogTitle>Editar producto</DialogTitle>
              <DialogDescription>
                Cambia la información que usa el catálogo y el agente al ofrecer este producto.
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
              <Button type="button" variant="outline" onClick={() => setEditingProduct(null)}>
                Cancelar
              </Button>
              <Button type="submit" disabled={updateProduct.isPending}>
                {updateProduct.isPending ? "Guardando..." : "Guardar cambios"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
