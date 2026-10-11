"use client";

import { forwardRef, useEffect, useImperativeHandle, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChefHat } from "lucide-react";
import { ProductFormSection } from "@/components/products/product-create-workspace";
import { ProductRecipeFields } from "@/components/products/product-recipe-fields";
import { productRecipesApi, type ProductRecipeIngredient } from "@/services/api/product-recipes";
import { useBusinessContextStore } from "@/stores/business-context-store";

export interface ProductRecipeEditorHandle {
  getValue: () => { recipeIngredients: Array<{ ingredientProductId: string; quantityPerUnit: number }>; recipeExpectedVersion: number } | null;
}

export const ProductRecipePanel = forwardRef<ProductRecipeEditorHandle, {
  productId: string; managesInventory: boolean; editable?: boolean;
}>(function ProductRecipePanel({ productId, managesInventory, editable = false }, ref) {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const recipe = useQuery({ queryKey: ["product-recipe", productId], queryFn: () => productRecipesApi.get(productId), refetchOnMount: "always" });
  const [lines, setLines] = useState<ProductRecipeIngredient[]>([]);
  const [changed, setChanged] = useState(false);
  const [baseVersion, setBaseVersion] = useState(0);
  useEffect(() => {
    if (changed) return;
    setLines(recipe.data?.ingredients.map((line) => ({ ...line })) ?? []);
    setBaseVersion(recipe.data?.version ?? 0);
  }, [recipe.data, changed]);
  useImperativeHandle(ref, () => ({ getValue: () => {
    if (recipe.isPending || recipe.isError || recipe.isFetching && !changed)
      throw new Error("Espera a que cargue la receta vigente del producto.");
    if (!changed) return null;
    if (lines.some((line) => !Number.isFinite(line.quantityPerUnit) || line.quantityPerUnit <= 0))
      throw new Error("Cada ingrediente necesita una cantidad mayor que cero.");
    return { recipeExpectedVersion: baseVersion,
      recipeIngredients: lines.map((line) => ({ ingredientProductId: line.ingredientProductId, quantityPerUnit: line.quantityPerUnit })) };
  } }), [recipe.isPending, recipe.isError, recipe.isFetching, changed, baseVersion, lines]);
  return <ProductFormSection id="product-recipe" icon={ChefHat} title="Ingredientes de la receta"
    description={managesInventory
      ? "La receta sirve para producir existencias. Al vender se descuenta el producto terminado."
      : "Al facturar se descuentan los ingredientes de esta receta; el producto no lleva saldo propio."}>
    {(recipe.isPending || recipe.isFetching && !changed) && <p className="text-sm text-muted-foreground">Cargando receta vigente…</p>}
    {recipe.isError && <p className="text-sm text-destructive">No fue posible cargar la receta. Actualiza la ficha antes de guardar.</p>}
    {!recipe.isPending && !recipe.isError && (!recipe.isFetching || changed) && <div className="space-y-3">
      <p className="text-sm text-muted-foreground">{recipe.data?.isEnabled
        ? `Versión ${recipe.data.version} · ${recipe.data.ingredients.length} ingredientes`
        : "Todavía no hay ingredientes configurados."}</p>
      {editable && businessId ? <ProductRecipeFields businessId={businessId} lines={lines}
        onChange={(next) => { setLines(next); setChanged(true); }} inputId={`recipe-ingredient-search-${productId}`} />
        : recipe.data?.isEnabled && <div className="divide-y rounded-xl border">{recipe.data.ingredients.map((line) =>
          <div key={line.ingredientProductId} className="flex justify-between gap-3 p-3 text-sm">
            <span>{line.name} <span className="text-muted-foreground">· {line.productCode}</span></span>
            <strong>{line.quantityPerUnit.toLocaleString("es-CO", { maximumFractionDigits: 6 })} {line.baseUnitCode}</strong>
          </div>)}</div>}
    </div>}
  </ProductFormSection>;
});
