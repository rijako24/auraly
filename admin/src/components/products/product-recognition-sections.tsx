"use client";

import { Loader2, SearchCheck, Tags } from "lucide-react";
import { forwardRef, useCallback, useImperativeHandle, useState } from "react";

import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { useAddProductAlias } from "@/hooks/use-products";
import { toast } from "sonner";
import {
  ProductAliasKind,
  ProductAliasResolutionMode,
  ProductAliasScope,
  ProductAliasSource,
  ProductAliasStatus,
  type ProductAlias,
} from "@/services/api/products";

const scopeLabels: Record<ProductAliasScope, string> = {
  [ProductAliasScope.Business]: "Negocio",
  [ProductAliasScope.Customer]: "Cliente",
};
const kindLabels: Record<ProductAliasKind, string> = {
  [ProductAliasKind.Alias]: "Alias",
  [ProductAliasKind.Keyword]: "Palabra clave",
  [ProductAliasKind.Misspelling]: "Error ortográfico",
};
const resolutionLabels: Record<ProductAliasResolutionMode, string> = {
  [ProductAliasResolutionMode.SuggestOnly]: "Solo sugerir",
  [ProductAliasResolutionMode.AutoResolve]: "Resolver automáticamente",
};
const sourceLabels: Record<ProductAliasSource, string> = {
  [ProductAliasSource.Manual]: "Manual",
  [ProductAliasSource.Imported]: "Importado",
  [ProductAliasSource.Learned]: "Aprendido",
};
const statusLabels: Record<ProductAliasStatus, string> = {
  [ProductAliasStatus.Pending]: "Pendiente",
  [ProductAliasStatus.Active]: "Activo",
  [ProductAliasStatus.Rejected]: "Rechazado",
};

export function aliasMappingExplanation(alias: ProductAlias): string | null {
  if (alias.sharedMappingCount <= 1) return null;
  if (alias.distinctProductCount > 1) {
    return `Esta expresi\u00f3n aparece en ${alias.distinctProductCount} productos para ofrecer alternativas; no identifica por s\u00ed sola un SKU.`;
  }
  if (alias.businessMappingCount > 0 && alias.distinctCustomerCount > 0) {
    return `Existe como regla global y tambi\u00e9n como aprendizaje independiente para ${alias.distinctCustomerCount} ${alias.distinctCustomerCount === 1 ? "cliente" : "clientes"}.`;
  }
  if (alias.distinctCustomerCount > 1) {
    return `Fue aprendida de forma independiente para ${alias.distinctCustomerCount} clientes.`;
  }
  return `La misma clave normalizada tiene ${alias.sharedMappingCount} registros con distinto alcance, estado o procedencia.`;
}

function ConfiguredAliasCard({ alias }: { alias: ProductAlias }) {
  const mappingExplanation = aliasMappingExplanation(alias);
  return (
    <div className="space-y-2 rounded-lg border bg-muted/20 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="font-medium">{alias.alias}</span>
        <Badge variant={alias.status === ProductAliasStatus.Active ? "default" : "secondary"}>
          {statusLabels[alias.status]}
        </Badge>
      </div>
      <div className="flex flex-wrap gap-1.5 text-xs">
        <Badge variant="outline">{scopeLabels[alias.scope]}</Badge>
        <Badge variant="outline">{kindLabels[alias.kind]}</Badge>
        <Badge variant="outline">{resolutionLabels[alias.resolutionMode]}</Badge>
        <Badge variant="outline">{sourceLabels[alias.source]}</Badge>
      </div>
      {mappingExplanation && (
        <p className="text-xs text-muted-foreground">{mappingExplanation}</p>
      )}
    </div>
  );
}

export interface ProductRecognitionSectionsHandle { getValue: () => string[]; save: () => Promise<void> }

interface ProductRecognitionSectionsProps {
  aliases: ProductAlias[];
  searchTerms: string[];
  isLoading: boolean;
  isError: boolean;
  productId?: string;
  editable?: boolean;
}

export const ProductRecognitionSections = forwardRef<ProductRecognitionSectionsHandle, ProductRecognitionSectionsProps>(function ProductRecognitionSections({
  aliases,
  searchTerms,
  isLoading,
  isError,
  productId,
  editable = false,
}, ref) {
  const addAlias = useAddProductAlias();
  const [alias, setAlias] = useState("");
  const submitAlias = useCallback(async () => {
    const value = alias.trim();
    if (!productId || !value) return;
    try {
      const result = await addAlias.mutateAsync({ productId, alias: value });
      if (result.errors.length) throw new Error(result.errors[0].message);
      setAlias("");
      toast.success("Alias agregado al producto.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible agregar el alias.");
    }
  }, [addAlias, alias, productId]);
  useImperativeHandle(ref, () => ({
    getValue: () => alias.trim() ? [alias.trim()] : [],
    save: submitAlias,
  }), [alias, submitAlias]);
  const configuredAliases = aliases.filter((alias) => alias.source !== ProductAliasSource.Learned);

  return (
    <>
      <section className="space-y-3">
        <div className="flex items-center gap-2">
          <Tags className="h-4 w-4 text-primary" />
          <div>
            <h3 className="text-sm font-semibold">Alias configurados</h3>
            <p className="text-xs text-muted-foreground">Expresiones manuales o importadas reconocidas como este producto.</p>
          </div>
        </div>
        {editable && productId && <div className="rounded-xl border bg-muted/20 p-3"><p className="mb-2 text-xs text-muted-foreground">Escribe un alias. Se guardará junto con el producto.</p><Input value={alias} onChange={(event) => setAlias(event.target.value)} placeholder="Nueva forma de encontrar este producto" /></div>}
        {isLoading ? (
          <div className="flex items-center gap-2 rounded-xl border p-4 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" /> Cargando configuración...
          </div>
        ) : isError ? (
          <div className="rounded-xl border border-destructive/30 p-4 text-sm">No se pudieron cargar los alias.</div>
        ) : configuredAliases.length ? (
          <div className="grid gap-2 sm:grid-cols-2">
            {configuredAliases.map((alias) => <ConfiguredAliasCard key={alias.productAliasId} alias={alias} />)}
          </div>
        ) : (
          <div className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
            No hay alias manuales o importados para este producto.
          </div>
        )}
      </section>

      <section className="space-y-3">
        <div className="flex items-center gap-2">
          <SearchCheck className="h-4 w-4 text-primary" />
          <div>
            <h3 className="text-sm font-semibold">Índice de búsqueda</h3>
            <p className="text-xs text-muted-foreground">
              Términos determinísticos generados desde nombre, SKU, categoría y descripción.
            </p>
          </div>
        </div>

        {isLoading ? (
          <div className="h-16 animate-pulse rounded-xl border bg-muted/30" />
        ) : isError ? (
          <div className="rounded-xl border border-destructive/30 p-4 text-sm">No se pudo cargar el índice de búsqueda.</div>
        ) : searchTerms.length ? (
          <div className="flex max-h-40 flex-wrap gap-2 overflow-y-auto rounded-xl border p-4">
            {searchTerms.map((term) => <Badge key={term} variant="secondary">{term}</Badge>)}
          </div>
        ) : (
          <div className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
            Este producto todavía no tiene términos indexados.
          </div>
        )}
      </section>
    </>
  );
});
