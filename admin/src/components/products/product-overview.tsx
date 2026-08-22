"use client";

import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Barcode, CircleDollarSign, Images, Link2, PackagePlus, Tags, Truck } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { ProductFormSection } from "@/components/products/product-create-workspace";
import { ProductImageGallery } from "@/components/products/product-image-gallery";
import { ProductInventoryByWarehouse } from "@/components/products/product-inventory-by-warehouse";
import { useProductCategories } from "@/hooks/use-products";
import { formatCurrency } from "@/lib/utils";
import { productMerchandisingApi } from "@/services/api/product-merchandising";
import { pricingApi } from "@/services/api/pricing";
import { productsApi, type Product } from "@/services/api/products";
import { taxProfilesApi } from "@/services/api/tax-profiles";

export function ProductOverview({ product }: { product: Product }) {
  const detail = useQuery({ queryKey: ["catalog-product-detail", product.productId], queryFn: () => productsApi.getCatalog(product.productId) });
  const merchandising = useQuery({ queryKey: ["product-merchandising", product.productId], queryFn: () => productMerchandisingApi.get(product.productId) });
  const pricing = useQuery({ queryKey: ["product-pricing-context", product.productId], queryFn: () => pricingApi.getProductContext(product.productId) });
  const brands = useQuery({ queryKey: ["product-brands"], queryFn: productMerchandisingApi.brands });
  const units = useQuery({ queryKey: ["product-units"], queryFn: productMerchandisingApi.units });
  const taxes = useQuery({ queryKey: ["tax-profiles"], queryFn: () => taxProfilesApi.list(false) });
  const categories = useProductCategories(false);
  const info = detail.data;
  const merch = merchandising.data;
  const brand = brands.data?.find((item) => item.productBrandId === merch?.productBrandId);
  const unit = units.data?.find((item) => item.code === merch?.baseUnitCode);
  const salesTax = taxes.data?.find((item) => item.taxProfileId === info?.salesTaxProfileId);
  const purchaseTax = taxes.data?.find((item) => item.taxProfileId === info?.purchaseTaxProfileId);
  const classification = useMemo(() => categoryChain(categories.data ?? [], merch?.productCategoryId ?? null), [categories.data, merch?.productCategoryId]);
  const isLoading = detail.isLoading || merchandising.isLoading || pricing.isLoading;

  if (isLoading) return <div className="space-y-5">{Array.from({ length: 4 }).map((_, index) => <div key={index} className="h-44 animate-pulse rounded-2xl bg-muted" />)}</div>;

  return <div className="space-y-5">
    {(detail.isError || merchandising.isError) && <div className="rounded-2xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950">No fue posible cargar toda la ficha del producto. Reintenta antes de editar.</div>}

    <ProductFormSection id="product-identity" icon={PackagePlus} title="Identidad" description="Lo que el equipo usa para encontrar y reconocer el producto.">
      <div className="grid gap-4 md:grid-cols-3">
        <Summary label="Nombre" value={product.name} />
        <Summary label="Referencia" value={info?.reference ?? product.reference ?? product.sku ?? "Sin referencia"} />
        <Summary label="Descripción" value={info?.description ?? product.description ?? "Sin descripción"} />
        <Summary label="Código interno" value={product.productCode || "Generado por Auraly"} />
        <Summary label="Estado" value={product.isActive ? "Activo" : "Inactivo"} />
      </div>
    </ProductFormSection>

    <ProductFormSection id="product-classification" icon={Tags} title="Clasificación, marca y unidad" description="La ruta comercial y la unidad real en la que se vende.">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        {["Area", "Linea", "Grupo", "Subgrupo"].map((label, index) => <Summary key={label} label={label} value={classification[index]?.name ?? "Sin asignar"} />)}
      </div>
      <div className="mt-4 grid gap-4 md:grid-cols-2">
        <Summary label="Marca" value={brand?.name ?? "Sin marca"} />
        <Summary label="Unidad en la que se vende" value={unit ? `${unit.name} - ${unit.symbol}` : merch?.baseUnitCode ?? "Sin unidad"} />
      </div>
    </ProductFormSection>

    <ProductFormSection id="product-sale" icon={Barcode} title="Captura, cantidad y balanza" description="Códigos de barras y reglas usadas al vender y mover el producto.">
      <div className="flex flex-wrap gap-2">
        {(merch?.barcodes ?? []).length ? merch!.barcodes.map((code) => <Badge key={code.value} variant={code.isPrimary ? "default" : "secondary"}>{code.value}{code.isPrimary ? " - Principal" : ""}</Badge>) : <p className="text-sm text-muted-foreground">No tiene codigos de barras asignados.</p>}
      </div>
      <div className="mt-4 grid gap-3 md:grid-cols-3">
        <Summary label="Controla inventario" value={info?.manageInventory ? "Sí, controla saldo por bodega" : "No controla saldo"} />
        <Summary label="Cantidad de venta" value={merch?.allowsFractionalSale ? "Permite cantidades decimales" : "Solo cantidades completas"} />
        <Summary label="Balanza" value={merch?.isWeighable ? `Habilitada${merch.scale?.scaleCode ? ` - codigo ${merch.scale.scaleCode}` : ""}` : "No utiliza balanza"} />
      </div>
      <div className="mt-5"><ProductInventoryByWarehouse productId={product.productId} manageInventory={info?.manageInventory ?? product.manageStock} /></div>
    </ProductFormSection>

    <ProductFormSection id="product-family" icon={Link2} title="Familia de productos" description="Presentaciones, colores o tallas vinculados a este producto.">
      {merch?.link && <div className="rounded-xl border bg-muted/20 p-4 text-sm"><p>Producto principal: <strong>{merch.link.parentProductName}</strong></p><p className="mt-1 text-xs text-muted-foreground">Inventario {merch.link.sharesInventory ? `x ${merch.link.inventoryFactor}` : "propio"} · precio {merch.link.sharesPrice ? `x ${merch.link.priceFactor}` : "propio"} · conversión {merch.link.allowsConversion ? `x ${merch.link.conversionFactor}` : "no habilitada"}</p></div>}
      {!merch?.link && (merch?.linkedProducts?.length ?? 0) > 0 && <div className="space-y-2">{merch!.linkedProducts.map((linked) => <div key={linked.childProductId} className="rounded-lg border bg-background p-3 text-sm"><strong>{linked.childProductName}</strong><p className="text-xs text-muted-foreground">{linked.childProductCode} · inventario {linked.sharesInventory ? `x ${linked.inventoryFactor}` : "propio"} · precio {linked.sharesPrice ? `x ${linked.priceFactor}` : "propio"} · conversión {linked.allowsConversion ? `x ${linked.conversionFactor}` : "no habilitada"}</p></div>)}</div>}
      {!merch?.link && (merch?.linkedProducts?.length ?? 0) === 0 && <div className="rounded-xl border border-dashed p-5 text-center text-sm text-muted-foreground">Todavía no hay productos vinculados a esta familia.</div>}
      {(merch?.linkedProducts?.some((item) => item.allowsConversion) || merch?.link?.allowsConversion) && <div className="mt-3 rounded-xl border border-emerald-200 bg-emerald-50/60 p-4"><p className="text-xs text-emerald-900/70">Merma máxima permitida en conversiones</p><p className="mt-1 font-semibold">{merch?.conversionMaximumLossPercent ?? 0} %</p></div>}
    </ProductFormSection>

    <ProductFormSection id="product-supplier" icon={Truck} title="Proveedor principal y empaque habitual" description="Cómo se compra y se convierte a la unidad del producto.">
      {(info?.suppliers ?? []).length ? <div className="grid gap-3 md:grid-cols-2">{info!.suppliers!.map((supplier) => <div key={supplier.supplierId} className="rounded-xl border p-4"><div className="flex items-center justify-between gap-3"><strong>{supplier.name}</strong>{supplier.isPrimary && <Badge>Principal</Badge>}</div><p className="mt-2 text-xs text-muted-foreground">1 {supplier.purchasePresentationName} = {supplier.unitsPerPresentation} {unit?.name ?? merch?.baseUnitCode ?? "unidades"}</p><p className="mt-1 text-xs text-muted-foreground">Costo: {formatCurrency(supplier.baseUnitCost)}</p></div>)}</div> : <p className="text-sm text-muted-foreground">No tiene proveedores asociados.</p>}
    </ProductFormSection>

    <ProductFormSection id="product-taxes" icon={CircleDollarSign} title="IVA, costo y precio" description="El IVA se incluye en el precio de venta; publicar sigue siendo una decisión explícita.">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Summary label="IVA de venta" value={salesTax ? `${salesTax.name} - ${salesTax.rate}%` : "Sin configurar"} />
        <Summary label="IVA de compra" value={purchaseTax ? `${purchaseTax.name} - ${purchaseTax.rate}%` : "Sin configurar"} />
        <Summary label="Costo base" value={formatCurrency(pricing.data?.costBasisAmount ?? 0)} />
        <Summary label="Margen" value={pricing.data?.currentMarginPercent == null ? "Sin calcular" : `${pricing.data.currentMarginPercent.toLocaleString("es-CO")}%`} />
        <Summary label="Precio preparado" value={formatCurrency(pricing.data?.preparedSalePrice ?? 0)} />
        <Summary label="Precio publico" value={formatCurrency(pricing.data?.publicSalePrice ?? product.unitPrice)} accent />
        <Summary label="Tratamiento IVA compra" value={taxTreatment(info?.purchaseTaxTreatment)} />
      </div>
    </ProductFormSection>
    <ProductFormSection id="product-images" icon={Images} title="Imágenes del producto" description="La portada y las demás vistas disponibles para reconocerlo.">
      <ProductImageGallery productId={product.productId} readOnly />
    </ProductFormSection>
  </div>;
}

function Summary({ label, value, accent = false }: { label: string; value: string; accent?: boolean }) { return <div className={`rounded-xl border p-4 ${accent ? "border-emerald-300 bg-emerald-50" : "bg-muted/10"}`}><p className="text-xs text-muted-foreground">{label}</p><p className="mt-1 font-semibold">{value}</p></div>; }
function taxTreatment(value?: string | null) { if (value === "CapitalizedCost") return "Mayor valor del costo"; if (value === "NotApplicable") return "No aplica"; return "IVA descontable"; }
function categoryChain(categories: Array<{ productCategoryId: string; parentProductCategoryId: string | null; depth: number; name: string }>, selectedId: string | null) { const byId = new Map(categories.map((item) => [item.productCategoryId, item])); const result: typeof categories = []; let current = selectedId ? byId.get(selectedId) : undefined; while (current) { result.unshift(current); current = current.parentProductCategoryId ? byId.get(current.parentProductCategoryId) : undefined; } return result; }
