"use client";

import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { ProductPicker } from "@/components/products/product-picker";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { priceSegmentsApi, type PriceChannelExclusionScope } from "@/services/api/price-segments";
import { productMerchandisingApi } from "@/services/api/product-merchandising";
import { productsApi } from "@/services/api/products";

type SelectorKind = "Area" | "Line" | "Group" | "Subgroup" | "Brand" | "Product";

const selectorKinds: Array<{ value: SelectorKind; label: string; depth?: number }> = [
  { value: "Area", label: "Área", depth: 0 },
  { value: "Line", label: "Línea", depth: 1 },
  { value: "Group", label: "Grupo", depth: 2 },
  { value: "Subgroup", label: "Subgrupo", depth: 3 },
  { value: "Brand", label: "Marca" },
  { value: "Product", label: "Producto" },
];

export function PriceChannelExclusions({ channelId, businessId, canManage }: {
  channelId: string;
  businessId: string;
  canManage: boolean;
}) {
  const client = useQueryClient();
  const [kind, setKind] = useState<SelectorKind>("Area");
  const [scopeId, setScopeId] = useState("");
  const exclusions = useQuery({
    queryKey: ["price-channel-exclusions", channelId],
    queryFn: () => priceSegmentsApi.exclusions(channelId),
  });
  const categories = useQuery({
    queryKey: ["product-categories", businessId, false],
    queryFn: () => productsApi.listCategories(businessId),
    enabled: canManage,
  });
  const brands = useQuery({
    queryKey: ["product-brands"],
    queryFn: productMerchandisingApi.brands,
    enabled: canManage,
  });
  const selectedKind = selectorKinds.find((item) => item.value === kind)!;
  const categoryOptions = useMemo(
    () => (categories.data ?? []).filter((item) => item.depth === selectedKind.depth),
    [categories.data, selectedKind.depth],
  );
  const excludedProductIds = useMemo(
    () => new Set((exclusions.data ?? []).filter((item) => item.scopeType === "Product").map((item) => item.scopeId)),
    [exclusions.data],
  );
  const save = useMutation({
    mutationFn: ({ scopeType, id }: { scopeType: PriceChannelExclusionScope; id: string }) =>
      priceSegmentsApi.saveExclusion(channelId, scopeType, id),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["price-channel-exclusions", channelId] });
      setScopeId("");
      toast.success("Exclusión agregada al canal.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible agregar la exclusión."),
  });
  const remove = useMutation({
    mutationFn: (exclusionId: string) => priceSegmentsApi.deleteExclusion(channelId, exclusionId),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ["price-channel-exclusions", channelId] });
      toast.success("Exclusión retirada del canal.");
    },
    onError: (error: { message?: string }) => toast.error(error.message ?? "No fue posible retirar la exclusión."),
  });

  function addSelected() {
    if (!scopeId) return;
    save.mutate({ scopeType: kind === "Brand" ? "Brand" : "Category", id: scopeId });
  }

  return <section className="space-y-4 rounded-2xl border border-amber-200/80 bg-amber-50/40 p-4 dark:border-amber-900/60 dark:bg-amber-950/15">
    <div className="flex items-start gap-3">
      <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-amber-500/15 text-amber-700 dark:text-amber-300"><Ban className="h-5 w-5" /></span>
      <div><h3 className="font-semibold">Excluidos</h3><p className="text-sm text-muted-foreground">Estos productos conservan el precio público aunque el cliente tenga este canal. Excluir una categoría también cubre todos sus niveles inferiores.</p></div>
    </div>

    {canManage && <div className="space-y-3 rounded-xl border bg-background p-3">
      <div className="flex flex-wrap gap-2" aria-label="Tipo de exclusión">
        {selectorKinds.map((item) => <Button key={item.value} type="button" size="sm" variant={kind === item.value ? "default" : "outline"} onClick={() => { setKind(item.value); setScopeId(""); }}>{item.label}</Button>)}
      </div>
      {kind === "Product"
        ? <ProductPicker businessId={businessId} selectedProductIds={excludedProductIds} disabled={save.isPending} label="Buscar producto para excluir" resultsMode="inline" inputId={`channel-excluded-product-${channelId}`} onSelect={(product) => save.mutate({ scopeType: "Product", id: product.productId })} />
        : <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-end">
            <div className="space-y-2"><Label>{selectedKind.label}</Label><Select value={scopeId} onValueChange={setScopeId}><SelectTrigger><SelectValue placeholder={`Selecciona ${selectedKind.label.toLocaleLowerCase("es-CO")}`} /></SelectTrigger><SelectContent>{kind === "Brand" ? (brands.data ?? []).map((item) => <SelectItem key={item.productBrandId} value={item.productBrandId}>{item.name}</SelectItem>) : categoryOptions.map((item) => <SelectItem key={item.productCategoryId} value={item.productCategoryId}>{item.path}</SelectItem>)}</SelectContent></Select></div>
            <Button type="button" disabled={!scopeId || save.isPending} onClick={addSelected}><Plus className="mr-2 h-4 w-4" />Agregar exclusión</Button>
          </div>}
    </div>}

    <div className="overflow-hidden rounded-xl border bg-background">
      {(exclusions.data ?? []).map((item) => <div key={item.exclusionId} className="flex items-center gap-3 border-t px-3 py-2.5 first:border-t-0">
        <Badge variant="outline">{exclusionLabel(item.scopeType, item.categoryDepth)}</Badge>
        <div className="min-w-0 flex-1"><p className="truncate text-sm font-medium">{item.scopeName}</p>{item.productCode && <p className="truncate text-xs text-muted-foreground">{item.productCode}</p>}</div>
        {canManage && <Button type="button" size="icon" variant="ghost" className="text-destructive" aria-label={`Retirar exclusión de ${item.scopeName}`} disabled={remove.isPending} onClick={() => remove.mutate(item.exclusionId)}><Trash2 className="h-4 w-4" /></Button>}
      </div>)}
      {!exclusions.isLoading && (exclusions.data ?? []).length === 0 && <p className="p-6 text-center text-sm text-muted-foreground">Este canal impacta todos los productos.</p>}
      {exclusions.isError && <p className="p-6 text-center text-sm text-destructive">No fue posible cargar los excluidos.</p>}
    </div>
  </section>;
}

function exclusionLabel(scopeType: PriceChannelExclusionScope, categoryDepth: number | null) {
  if (scopeType === "Brand") return "Marca";
  if (scopeType === "Product") return "Producto";
  return ["Área", "Línea", "Grupo", "Subgrupo"][categoryDepth ?? -1] ?? "Categoría";
}
