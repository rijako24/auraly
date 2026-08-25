"use client";

import { forwardRef, useEffect, useImperativeHandle, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { PackageCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { useGoodsReceiptOptions } from "@/hooks/use-goods-receipts";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { goodsReceiptsApi } from "@/services/api/goods-receipts";
import { productsApi } from "@/services/api/products";

export interface ProductSupplierEditorValue {
  supplierId: string;
  identification: string;
  name: string;
  supplierProductCode: string | null;
  purchasePresentationName: string;
  unitsPerPresentation: number;
}
export interface ProductSupplierEditorHandle { getValue: () => ProductSupplierEditorValue; validate: () => void; save: () => Promise<void> }

export const ProductSupplierEditor = forwardRef<ProductSupplierEditorHandle, {
  embedded?: boolean;
  productId: string;
  productName: string;
  saleUnitName?: string;
}>(function ProductSupplierEditor({ productId, productName, saleUnitName = "unidad de venta", embedded = false }, ref) {
  const client = useQueryClient();
  const options = useGoodsReceiptOptions();
  const purchasePresentations = useReferenceOptions("purchase-presentation");
  const [supplierId, setSupplierId] = useState("");
  const [supplierProductCode, setSupplierProductCode] = useState("");
  const [packageName, setPackageName] = useState("Unidad");
  const [unitsPerPackage, setUnitsPerPackage] = useState("1");
  const [validationError, setValidationError] = useState<string>();

  const catalogProduct = useQuery({
    queryKey: ["catalog-product", productId],
    queryFn: () => productsApi.getCatalog(productId),
  });

  useEffect(() => {
    const primary = catalogProduct.data?.suppliers?.find((supplier) => supplier.isPrimary)
      ?? catalogProduct.data?.suppliers?.[0];
    if (!primary) return;
    setSupplierId(primary.supplierId);
    setSupplierProductCode(primary.supplierProductCode ?? "");
    setPackageName(primary.purchasePresentationName || "Unidad");
    setUnitsPerPackage(String(primary.unitsPerPresentation || 1));
  }, [catalogProduct.data]);

  const relation = useQuery({
    queryKey: ["product-supplier-relation", supplierId, productId],
    queryFn: async () => {
      const page = await goodsReceiptsApi.products(supplierId, productName, true, 1, 100);
      return page.items.find((item) => item.productId === productId) ?? null;
    },
    enabled: Boolean(supplierId),
  });

  useEffect(() => {
    const current = relation.data;
    if (!current) {
      setSupplierProductCode("");
      setPackageName("Unidad");
      setUnitsPerPackage("1");
      return;
    }
    setSupplierProductCode(current.supplierProductCode ?? "");
    setPackageName(current.purchasePresentationName || "Unidad");
    setUnitsPerPackage(String(current.unitsPerPresentation || 1));
  }, [relation.data]);

  const save = useMutation({
    mutationFn: () => goodsReceiptsApi.associateProduct({
      supplierId,
      productId,
      supplierProductCode: supplierProductCode.trim() || null,
      isPrimary: true,
      purchasePresentationName: packageName,
      unitsPerPresentation: Number(unitsPerPackage),
    }),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["product-supplier-relation", supplierId, productId] });
      await client.invalidateQueries({ queryKey: ["products"] });
      toast.success("Proveedor y empaque actualizados.");
    },
    onError: () => toast.error("No fue posible guardar la relación con el proveedor."),
  });

  useImperativeHandle(ref, () => ({
    getValue: () => {
      if (!supplierId) throw new Error("Selecciona el proveedor principal del producto.");
      if (!packageName || Number(unitsPerPackage) <= 0) throw new Error("Revisa el empaque del proveedor.");
      const supplier = options.data?.suppliers.find((item) => item.supplierId === supplierId);
      if (!supplier) throw new Error("El proveedor seleccionado ya no está disponible.");
      return {
        supplierId,
        identification: supplier.identification,
        name: supplier.name,
        supplierProductCode: supplierProductCode.trim() || null,
        purchasePresentationName: packageName,
        unitsPerPresentation: Number(unitsPerPackage),
      };
    },
    validate: () => {
      if (!supplierId) {
        const message = "Selecciona el proveedor principal del producto.";
        setValidationError(message);
        throw new Error(message);
      }
      if (!packageName || Number(unitsPerPackage) <= 0) {
        const message = "Revisa el empaque del proveedor.";
        setValidationError(message);
        throw new Error(message);
      }
      setValidationError(undefined);
    },
    save: async () => {
      if (!supplierId) throw new Error("Selecciona el proveedor principal del producto.");
      if (!packageName || Number(unitsPerPackage) <= 0) throw new Error("Revisa el empaque del proveedor.");
      await save.mutateAsync();
    },
  }), [options.data?.suppliers, packageName, save, supplierId, supplierProductCode, unitsPerPackage]);
  const valid = Boolean(supplierId && packageName) && Number(unitsPerPackage) > 0;
  const directUnit = Number(unitsPerPackage) === 1 && packageName.toLocaleLowerCase("es-CO") === "unidad";

  return <section className={`space-y-4 ${embedded ? "" : "rounded-xl border bg-muted/15 p-4"}`}>
    <div>
      <h3 className="text-sm font-semibold">Proveedor principal y empaque habitual</h3>
      <p className="text-xs text-muted-foreground">
        El producto conserva su unidad de venta. Aquí defines si el proveedor lo entrega por caja, bulto, paquete o unidad.
      </p>
    </div>

    <div className="grid items-start gap-4 lg:grid-cols-[minmax(260px,1.3fr)_minmax(180px,.8fr)_minmax(180px,.8fr)]">
      <div className="space-y-2">
        <Label>Proveedor <span className="text-destructive">*</span></Label>
        <Select value={supplierId} onValueChange={(value) => { setSupplierId(value); setValidationError(undefined); }}>
          <SelectTrigger aria-invalid={Boolean(validationError && !supplierId)}><SelectValue placeholder="Selecciona un proveedor" /></SelectTrigger>
          <SelectContent>
            {(options.data?.suppliers ?? []).map((supplier) => <SelectItem key={supplier.supplierId} value={supplier.supplierId}>
              {supplier.name} · {supplier.identification}
            </SelectItem>)}
          </SelectContent>
        </Select>
        {validationError && !supplierId && <p className="text-sm text-destructive">{validationError}</p>}
        <p className="text-xs text-muted-foreground">Al guardar, este proveedor queda como el principal del producto.</p>
      </div>

      <div className="space-y-2">
        <Label>Código usado por el proveedor</Label>
        <Input value={supplierProductCode} maxLength={80} onChange={(event) => setSupplierProductCode(event.target.value)} placeholder="Opcional" />
      </div>
      <div className="space-y-2">
        <Label>Empaque en que lo entrega</Label>
        <Select value={packageName} onValueChange={setPackageName}>
          <SelectTrigger><SelectValue placeholder="Selecciona el empaque" /></SelectTrigger>
          <SelectContent>
            {!(purchasePresentations.data ?? []).some((option) => option.code === packageName) && packageName &&
              <SelectItem value={packageName}>{packageName}</SelectItem>}
            {(purchasePresentations.data ?? []).map((option) => <SelectItem key={option.id} value={option.code}>{option.label}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-2">
        <Label>Contenido por empaque</Label>
        <div className="flex items-center gap-2">
          <Input type="number" min="0.000001" step="0.001" value={unitsPerPackage} onChange={(event) => setUnitsPerPackage(event.target.value)} />
          <span className="text-sm text-muted-foreground">{saleUnitName}</span>
        </div>
      </div>
    </div>

    <div className="rounded-lg bg-background px-3 py-3 text-sm">
      <span>{directUnit
        ? `Entrega directa: 1 empaque equivale a 1 ${saleUnitName}.`
        : `1 ${packageName} equivale a ${Number(unitsPerPackage) || 0} ${saleUnitName}.`}</span>
      {!embedded && <Button type="button" size="sm" disabled={!valid || save.isPending} onClick={() => save.mutate()}>
        <PackageCheck className="mr-2 h-4 w-4" /> Guardar proveedor
      </Button>}
    </div>
  </section>;
});
