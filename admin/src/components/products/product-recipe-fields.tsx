"use client";

import { Trash2 } from "lucide-react";
import { toast } from "sonner";
import { ProductPicker } from "@/components/products/product-picker";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { formatCurrency } from "@/lib/utils";
import type { InventoryProductItem } from "@/services/api/inventory";
import type { ProductRecipeIngredient } from "@/services/api/product-recipes";

export function ProductRecipeFields({ businessId, lines, onChange, disabled = false, inputId, parentProductId, compositionKind = "Ingredients" }: {
  businessId: string; lines: ProductRecipeIngredient[];
  onChange: (lines: ProductRecipeIngredient[]) => void; disabled?: boolean; inputId: string; parentProductId?: string; compositionKind?: string;
}) {
  const add = (item: InventoryProductItem, unitCostWithTax?: number | null) => {
    if (lines.some((line) => line.ingredientProductId === item.productId)) {
      toast.error("Este componente ya está en la lista.");
      return;
    }
    onChange([...lines, { ingredientProductId: item.productId, productCode: item.productCode,
      name: item.productName, baseUnitCode: item.unitCode, quantityPerUnit: 1, unitCostWithTax }]);
  };
  return <div className="space-y-4">
    <div className="max-w-xl rounded-xl border bg-muted/20 p-3"><ProductPicker businessId={businessId} ingredientOnly compositionKind={compositionKind}
      selectedProductIds={new Set(lines.map((line) => line.ingredientProductId))}
      excludedProductIds={new Set([...lines.map((line) => line.ingredientProductId), ...(parentProductId ? [parentProductId] : [])])}
      disabled={disabled} label={compositionKind === "Combo" ? "Agregar producto terminado" : "Agregar ingrediente"} inputId={inputId} onSelect={add} /></div>
    <div className="divide-y rounded-xl border">{lines.map((line) => <div key={line.ingredientProductId} className="flex flex-wrap items-center gap-3 p-3">
      <div className="min-w-40 flex-1"><p className="font-medium">{line.name}</p><p className="text-xs text-muted-foreground">{line.productCode} · por unidad terminada</p></div>
      <div className="min-w-40 text-sm"><p className="text-xs text-muted-foreground">Costo unitario con IVA</p><p className="font-medium tabular-nums">{line.unitCostWithTax == null ? "No disponible" : formatCurrency(line.unitCostWithTax, "COP", 6)} <span className="text-xs text-muted-foreground">/ {line.baseUnitCode}</span></p></div>
      <Input type="number" min="0.000001" step="any" aria-label={`Cantidad de ${line.name}`} className="w-32" value={line.quantityPerUnit}
        disabled={disabled} onChange={(event) => onChange(lines.map((item) => item.ingredientProductId === line.ingredientProductId
          ? { ...item, quantityPerUnit: Number(event.target.value) } : item))} />
      <span className="w-12 text-sm">{line.baseUnitCode}</span>
      <Button type="button" variant="ghost" size="icon" aria-label={`Quitar ${line.name}`} disabled={disabled}
        onClick={() => onChange(lines.filter((item) => item.ingredientProductId !== line.ingredientProductId))}><Trash2 className="h-4 w-4" /></Button>
    </div>)}{lines.length === 0 && <p className="p-5 text-sm text-muted-foreground">Agrega los componentes que se consumen por una unidad del producto.</p>}</div>
  </div>;
}
