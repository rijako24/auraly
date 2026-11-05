"use client";

import { useQuery } from "@tanstack/react-query";
import { useTenantContextStore } from "@/stores/tenant-context-store";
import { productMerchandisingApi, type ProductVariantAttribute, type ProductVariantInput } from "@/services/api/product-merchandising";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Label } from "@/components/ui/label";

export function useProductVariantOptions(attribute: ProductVariantAttribute) {
  const tenantId = useTenantContextStore(state => state.selectedTenantId);
  return useQuery({ queryKey: ["product-variant-options", tenantId, attribute],
    queryFn: () => productMerchandisingApi.variantOptions(attribute), enabled: !!tenantId });
}

export function ProductVariantFields({ value, onChange }: {
  value: { productColorId?: string | null; productSizeId?: string | null };
  onChange: (value: ProductVariantInput) => void;
}) {
  const colors = useProductVariantOptions("colors");
  const sizes = useProductVariantOptions("sizes");
  return <div className="mt-4 space-y-2">
    <div className="grid gap-4 sm:grid-cols-2">{[
      { field: "productColorId", label: "Color", query: colors },
      { field: "productSizeId", label: "Talla", query: sizes },
    ].map(({ field, label, query }) => {
      const selected = value[field as keyof typeof value] ?? null;
      return <div key={field} className="space-y-1.5"><Label htmlFor={`product-${field}`}>{label} (opcional)</Label>
        <Select value={selected ?? "__none__"} disabled={query.isLoading || query.isError} onValueChange={id => onChange({
          productColorId: value.productColorId ?? null, productSizeId: value.productSizeId ?? null,
          [field]: id === "__none__" ? null : id,
        })}><SelectTrigger id={`product-${field}`} aria-label={label}><SelectValue placeholder="Sin especificar" /></SelectTrigger>
          <SelectContent><SelectItem value="__none__">Sin especificar</SelectItem>{(query.data ?? [])
            .filter(item => item.isActive || item.id === selected).map(item => <SelectItem key={item.id} value={item.id} disabled={!item.isActive}>{item.name}{!item.isActive ? " (inactivo)" : ""}</SelectItem>)}</SelectContent>
        </Select>
        {query.isError && <p role="alert" className="text-sm text-destructive">No fue posible cargar {label.toLocaleLowerCase()}. <button type="button" className="underline" onClick={() => void query.refetch()}>Reintentar</button></p>}
      </div>;
    })}</div>
    <p className="text-xs text-muted-foreground">Se administran en Maestros de productos. El nombre se escribe manualmente y no cambia al elegir talla o color.</p>
  </div>;
}
