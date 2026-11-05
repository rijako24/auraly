"use client";

import { forwardRef, useEffect, useImperativeHandle, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useReferenceOptions } from "@/hooks/use-reference-options";
import { ProductCompositionSelector } from "@/components/products/product-composition-selector";
import { ChefHat } from "lucide-react";
import { ProductFormSection } from "@/components/products/product-create-workspace";
import { ProductRecipeFields } from "@/components/products/product-recipe-fields";
import { productRecipesApi, type ProductRecipeIngredient } from "@/services/api/product-recipes";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { formatCurrency } from "@/lib/utils";

export interface ProductCompositionDraft { kind: string; hasComponents: boolean; costBasisAmount?: number }

export interface ProductRecipeEditorHandle {
  getValue: () => { recipeIngredients: Array<{ ingredientProductId: string; quantityPerUnit: number }>; recipeExpectedVersion: number; compositionKind: string } | null;
}

export const ProductRecipePanel = forwardRef<ProductRecipeEditorHandle, {
  productId: string; managesInventory: boolean; editable?: boolean; onCompositionChange?: (value: ProductCompositionDraft) => void;
}>(function ProductRecipePanel({ productId, managesInventory, editable = false, onCompositionChange }, ref) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const recipe = useQuery({ queryKey: ["product-recipe", productId, businessId], queryFn: () => productRecipesApi.get(productId), enabled: !!businessId, refetchOnMount: "always" });
  const kinds = useReferenceOptions("product-composition-kind");
  const [kind, setKind] = useState("Ingredients");
  const [lines, setLines] = useState<ProductRecipeIngredient[]>([]);
  const [changed, setChanged] = useState(false);
  const [baseVersion, setBaseVersion] = useState(0);
  useEffect(() => {
    if (changed) return;
    setKind(recipe.data?.compositionKind ?? "Ingredients");
    setLines(recipe.data?.ingredients.map((line) => ({ ...line })) ?? []);
    setBaseVersion(recipe.data?.version ?? 0);
  }, [recipe.data, changed]);
  const validComponents = lines.length > 0 && lines.every(line => Number.isFinite(line.quantityPerUnit) && line.quantityPerUnit > 0);
  const cost = useQuery({ queryKey: ["composition-cost", businessId, lines],
    queryFn: () => productRecipesApi.compositionCost(lines.map(line => ({ ingredientProductId: line.ingredientProductId, quantityPerUnit: line.quantityPerUnit }))),
    enabled: editable && changed && validComponents && !!businessId, retry: false });
  useEffect(() => {
    if (!recipe.isSuccess) return;
    onCompositionChange?.({ kind: changed ? kind : recipe.data?.compositionKind ?? "Ingredients",
      hasComponents: changed ? lines.length > 0 : recipe.data?.isEnabled === true && recipe.data.ingredients.length > 0,
      costBasisAmount: changed ? cost.data?.costBasisAmount : undefined });
  }, [kind, lines.length, cost.data, changed, onCompositionChange, recipe.isSuccess, recipe.data]);
  useImperativeHandle(ref, () => ({ getValue: () => {
    if (recipe.isPending || recipe.isError || recipe.isFetching && !changed)
      throw new Error("Espera a que cargue la receta vigente del producto.");
    if (!changed) return null;
    if (lines.length > 0 && (cost.isPending || cost.isFetching || cost.isError)) throw new Error("Espera a que se calcule el costo de la composición y corrige los componentes sin costo.");
    if (lines.some((line) => !Number.isFinite(line.quantityPerUnit) || line.quantityPerUnit <= 0))
      throw new Error("Cada ingrediente necesita una cantidad mayor que cero.");
    return { recipeExpectedVersion: baseVersion, compositionKind: kind,
      recipeIngredients: lines.map((line) => ({ ingredientProductId: line.ingredientProductId, quantityPerUnit: line.quantityPerUnit })) };
  } }), [recipe.isPending, recipe.isError, recipe.isFetching, changed, baseVersion, lines, kind, cost.isPending, cost.isFetching, cost.isError]);
  return <ProductFormSection id="product-recipe" icon={ChefHat} title="Composición del producto"
    description={managesInventory
      ? "La receta sirve para producir existencias. Al vender se descuenta el producto terminado."
      : "Al facturar se descuentan los componentes de esta lista; el producto no lleva saldo propio."}>
    {(recipe.isPending || recipe.isFetching && !changed) && <p className="text-sm text-muted-foreground">Cargando receta vigente…</p>}
    {recipe.isError && <p className="text-sm text-destructive">No fue posible cargar la receta. Actualiza la ficha antes de guardar.</p>}
    {!recipe.isPending && !recipe.isError && (!recipe.isFetching || changed) && <div className="space-y-3">
      {editable && changed && validComponents && cost.isFetching && <p className="text-sm text-muted-foreground">Calculando costo de los componentes…</p>}
      {cost.isError && changed && <p role="alert" className="text-sm text-destructive">No se pudo calcular el costo. Revisa costos e impuestos de compra de los componentes.</p>}
      {editable && <ProductCompositionSelector value={kind} disabled={kinds.isPending || kinds.isError} options={kinds.data??[]} onChange={value => { setKind(value); setLines([]); setChanged(true); }}/>}
      <p className="text-sm text-muted-foreground">{recipe.data?.isEnabled
        ? `Versión ${recipe.data.version} · ${recipe.data.ingredients.length} componentes`
        : "Todavía no hay componentes configurados."}</p>
      {editable && businessId ? <ProductRecipeFields businessId={businessId} parentProductId={productId} lines={lines} compositionKind={kind}
        onChange={(next) => { setLines(next); setChanged(true); }} inputId={`recipe-ingredient-search-${productId}`} />
        : recipe.data?.isEnabled && <div className="divide-y rounded-xl border">{recipe.data.ingredients.map((line) =>
          <div key={line.ingredientProductId} className="flex flex-wrap items-center justify-between gap-3 p-3 text-sm">
            <span>{line.name} <span className="text-muted-foreground">· {line.productCode}</span></span>
            <span><span className="block text-xs text-muted-foreground">Costo unitario con IVA</span><strong>{line.unitCostWithTax == null ? "No disponible" : formatCurrency(line.unitCostWithTax, "COP", 6)}</strong> / {line.baseUnitCode}</span>
            <strong>{line.quantityPerUnit.toLocaleString("es-CO", { maximumFractionDigits: 6 })} {line.baseUnitCode}</strong>
          </div>)}</div>}
    </div>}
  </ProductFormSection>;
});
