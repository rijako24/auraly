"use client";

import { BrainCircuit, Check, MoreHorizontal, Sparkles, X } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  ProductAliasResolutionMode,
  ProductAliasReviewAction,
  ProductAliasScope,
  ProductAliasSource,
  ProductAliasStatus,
  type ProductAlias,
} from "@/services/api/products";
import { aliasMappingExplanation } from "./product-recognition-sections";

const statusLabels: Record<ProductAliasStatus, string> = {
  [ProductAliasStatus.Pending]: "Pendiente",
  [ProductAliasStatus.Active]: "Activo",
  [ProductAliasStatus.Rejected]: "Rechazado",
};
const resolutionLabels: Record<ProductAliasResolutionMode, string> = {
  [ProductAliasResolutionMode.SuggestOnly]: "Solo sugerir",
  [ProductAliasResolutionMode.AutoResolve]: "Resolución automática",
};
const dateFormatter = new Intl.DateTimeFormat("es-CO", { dateStyle: "medium", timeStyle: "short" });

function maskCustomerKey(value: string): string {
  const normalized = value.trim();
  if (normalized.length <= 4) return "••••";
  return `${normalized.slice(0, 2)}••••${normalized.slice(-2)}`;
}

interface LearningCardProps {
  alias: ProductAlias;
  isPending: boolean;
  onReview: (alias: ProductAlias, action: ProductAliasReviewAction, resolutionMode: ProductAliasResolutionMode) => void;
  onPromote: (alias: ProductAlias, resolutionMode: ProductAliasResolutionMode) => void;
}

function LearningCard({ alias, isPending, onReview, onPromote }: LearningCardProps) {
  const isCustomerLearning = alias.scope === ProductAliasScope.Customer;
  const mappingExplanation = aliasMappingExplanation(alias);
  const lastConfirmation = alias.lastConfirmedAt ? dateFormatter.format(new Date(alias.lastConfirmedAt)) : "Sin confirmaciones";

  return (
    <article className="space-y-3 rounded-xl border bg-muted/15 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="break-words font-medium">{alias.alias}</p>
          <p className="mt-1 text-xs text-muted-foreground">
            {isCustomerLearning ? `Cliente ${maskCustomerKey(alias.customerKey)}` : "Aprendizaje global"}
          </p>
        </div>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button type="button" variant="ghost" size="icon" disabled={isPending} aria-label="Gestionar aprendizaje">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-64">
            <DropdownMenuLabel>Acciones de aprendizaje</DropdownMenuLabel>
            {(alias.status !== ProductAliasStatus.Active || alias.resolutionMode !== ProductAliasResolutionMode.SuggestOnly) && (
              <DropdownMenuItem onSelect={() => onReview(alias, ProductAliasReviewAction.Approve, ProductAliasResolutionMode.SuggestOnly)}>
                <Check /> {alias.status === ProductAliasStatus.Active ? "Cambiar a solo sugerencias" : "Aprobar para sugerencias"}
              </DropdownMenuItem>
            )}
            {(alias.status !== ProductAliasStatus.Active || alias.resolutionMode !== ProductAliasResolutionMode.AutoResolve) && (
              <DropdownMenuItem onSelect={() => onReview(alias, ProductAliasReviewAction.Approve, ProductAliasResolutionMode.AutoResolve)}>
                <Sparkles /> {alias.status === ProductAliasStatus.Active ? "Activar resolución automática" : "Aprobar y resolver automáticamente"}
              </DropdownMenuItem>
            )}
            {isCustomerLearning && (
              <DropdownMenuItem onSelect={() => onPromote(alias, ProductAliasResolutionMode.SuggestOnly)}>
                <BrainCircuit /> Promover a aprendizaje global
              </DropdownMenuItem>
            )}
            {alias.status !== ProductAliasStatus.Rejected && (
              <>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  className="text-destructive focus:text-destructive"
                  onSelect={() => onReview(alias, ProductAliasReviewAction.Reject, ProductAliasResolutionMode.SuggestOnly)}
                >
                  <X /> Rechazar aprendizaje
                </DropdownMenuItem>
              </>
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
      <div className="flex flex-wrap gap-1.5">
        <Badge variant={alias.status === ProductAliasStatus.Active ? "default" : "secondary"}>{statusLabels[alias.status]}</Badge>
        <Badge variant="outline">{resolutionLabels[alias.resolutionMode]}</Badge>
        <Badge variant="outline">{alias.usageCount} {alias.usageCount === 1 ? "uso" : "usos"}</Badge>
      </div>
      {mappingExplanation && (
        <p className="text-xs text-muted-foreground">{mappingExplanation}</p>
      )}
      <p className="text-xs text-muted-foreground">Última confirmación: {lastConfirmation}</p>
    </article>
  );
}

interface ProductLearningSectionProps {
  aliases: ProductAlias[];
  isLoading: boolean;
  isError: boolean;
  isPending: boolean;
  onReview: LearningCardProps["onReview"];
  onPromote: LearningCardProps["onPromote"];
}

export function ProductLearningSection({
  aliases,
  isLoading,
  isError,
  isPending,
  onReview,
  onPromote,
}: ProductLearningSectionProps) {
  const learnedAliases = aliases.filter((alias) => alias.source === ProductAliasSource.Learned);
  const globalLearning = learnedAliases.filter((alias) => alias.scope === ProductAliasScope.Business);
  const customerLearning = learnedAliases.filter((alias) => alias.scope === ProductAliasScope.Customer);

  return (
    <section className="space-y-4">
      <div className="flex items-center gap-2">
        <BrainCircuit className="h-4 w-4 text-primary" />
        <div>
          <h3 className="text-sm font-semibold">Aprendizaje</h3>
          <p className="text-xs text-muted-foreground">
            Expresiones aprendidas en conversaciones, separadas por alcance y sujetas a revisión.
          </p>
        </div>
      </div>
      {isLoading ? (
        <div className="h-24 animate-pulse rounded-xl border bg-muted/30" />
      ) : isError ? (
        <div className="rounded-xl border border-destructive/30 p-4 text-sm">No se pudo cargar el aprendizaje.</div>
      ) : learnedAliases.length === 0 ? (
        <div className="rounded-xl border border-dashed p-4 text-sm text-muted-foreground">
          Este producto todavía no tiene expresiones aprendidas.
        </div>
      ) : (
        <div className="grid gap-5 lg:grid-cols-2">
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <h4 className="text-sm font-medium">Global</h4>
              <Badge variant="secondary">{globalLearning.length}</Badge>
            </div>
            {globalLearning.length ? globalLearning.map((alias) => (
              <LearningCard key={alias.productAliasId} alias={alias} isPending={isPending} onReview={onReview} onPromote={onPromote} />
            )) : <p className="rounded-lg border border-dashed p-3 text-xs text-muted-foreground">Sin aprendizaje global.</p>}
          </div>
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <h4 className="text-sm font-medium">Por cliente</h4>
              <Badge variant="secondary">{customerLearning.length}</Badge>
            </div>
            {customerLearning.length ? customerLearning.map((alias) => (
              <LearningCard key={alias.productAliasId} alias={alias} isPending={isPending} onReview={onReview} onPromote={onPromote} />
            )) : <p className="rounded-lg border border-dashed p-3 text-xs text-muted-foreground">Sin aprendizaje por cliente.</p>}
          </div>
        </div>
      )}
    </section>
  );
}