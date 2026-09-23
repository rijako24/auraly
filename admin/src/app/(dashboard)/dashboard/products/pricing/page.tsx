"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useSearchParams } from "next/navigation";
import type { ColumnDef } from "@tanstack/react-table";
import { History, PackageCheck, Send, TrendingUp, XCircle } from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { ReportViewer } from "@/components/reports/report-viewer";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { ProductPriceHistoryDialog } from "@/components/pricing/product-price-history-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { usePriceProposals, usePublishPrices, useRejectPrice, useReviewPrice } from "@/hooks/use-pricing";
import {
  buildPriceReviewRequest,
  changeDraftMargin,
  changeDraftSalePrice,
  createPricePublicationDraft,
  type PricePublicationDraft,
} from "@/lib/pricing-publication-draft";
import { buildPricePublicationRequest } from "@/lib/pricing-publication-selection";
import type { ReportRow } from "@/lib/report-viewer";
import { formatCurrency, formatDateTime, formatRelativeTime } from "@/lib/utils";
import type { PriceInputMode, PriceProposalStatus, PriceRevisionListItem } from "@/services/api/pricing";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";

const statuses: Record<PriceProposalStatus, string> = {
  PendingReview: "Pendiente",
  Approved: "Preparada",
  Published: "Publicada",
  Rejected: "Rechazada",
  Superseded: "Reemplazada",
};

export default function PricingPage() {
  const searchParams = useSearchParams();
  const sourceDocumentId = searchParams.get("sourceDocumentId") ?? undefined;
  const selectedBusinessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canReview = permissions.has("pricing.proposals.review");
  const canPublish = permissions.has("pricing.prices.publish");
  const canBulk = permissions.has("pricing.bulk-publish");
  const canReadHistory = permissions.has("pricing.history.read");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<PriceProposalStatus | "Pending" | "all">("Pending");
  const [supplierId, setSupplierId] = useState("all");
  const [drafts, setDrafts] = useState<Record<string, PricePublicationDraft>>({});
  const [hiddenProductIds, setHiddenProductIds] = useState<Set<string>>(new Set());
  const [savingProductIds, setSavingProductIds] = useState<Set<string>>(new Set());
  const [saveErrors, setSaveErrors] = useState<Record<string, string>>({});
  const [priceReportRows, setPriceReportRows] = useState<ReportRow[] | null>(null);
  const [historyProduct, setHistoryProduct] = useState<{ id: string; name: string }>();
  const draftsRef = useRef(drafts);
  draftsRef.current = drafts;
  const saveChainsRef = useRef(new Map<string, Promise<void>>());
  const saveErrorsRef = useRef(new Map<string, string>());
  const businessScopeVersionRef = useRef(0);
  const publicationScopeKey = `${selectedBusinessId ?? ""}|${status}|${search.trim()}|${supplierId}|${sourceDocumentId ?? ""}`;
  const publicationScopeKeyRef = useRef(publicationScopeKey);
  publicationScopeKeyRef.current = publicationScopeKey;
  const query = usePriceProposals({
    page,
    pageSize,
    search: search.trim() || undefined,
    status: status === "all" ? undefined : status,
    supplierId: supplierId === "all" ? undefined : supplierId,
    sourceDocumentId,
  });
  const publish = usePublishPrices();
  const review = useReviewPrice();
  const reject = useRejectPrice();

  useEffect(() => {
    businessScopeVersionRef.current += 1;
    draftsRef.current = {};
    saveChainsRef.current.clear();
    saveErrorsRef.current.clear();
    setDrafts({});
    setHiddenProductIds(new Set());
    setSavingProductIds(new Set());
    setSaveErrors({});
  }, [selectedBusinessId]);

  useEffect(() => {
    saveErrorsRef.current.clear();
    setSaveErrors({});
  }, [publicationScopeKey]);

  useEffect(() => {
    const rows = query.data?.items ?? [];
    if (!rows.length) return;
    setDrafts((current) => {
      const next = { ...current };
      for (const row of rows) {
        const existing = next[row.productId];
        if (!existing || existing.concurrencyToken !== row.concurrencyToken)
          next[row.productId] = createPricePublicationDraft(row);
      }
      return next;
    });
  }, [query.data?.items]);

  const draftFor = useCallback((row: PriceRevisionListItem) =>
    draftsRef.current[row.productId] ?? createPricePublicationDraft(row), []);

  const replaceDraft = useCallback((
    productId: string,
    update: (current: PricePublicationDraft) => PricePublicationDraft,
    fallback: PricePublicationDraft,
  ) => {
    const next = update(draftsRef.current[productId] ?? fallback);
    const collection = { ...draftsRef.current, [productId]: next };
    draftsRef.current = collection;
    setDrafts(collection);
    return next;
  }, []);

  const queueDraftSave = useCallback((
    row: PriceRevisionListItem,
    draft: PricePublicationDraft,
    inputMode: PriceInputMode,
  ) => {
    const operationBusinessScopeVersion = businessScopeVersionRef.current;
    const operationScopeKey = publicationScopeKey;
    const previous = saveChainsRef.current.get(row.productId) ?? Promise.resolve();
    setSavingProductIds((current) => new Set(current).add(row.productId));
    saveErrorsRef.current.delete(row.productId);
    setSaveErrors((current) => {
      const next = { ...current };
      delete next[row.productId];
      return next;
    });
    const operation = previous.then(async () => {
      const authority = draftsRef.current[row.productId] ?? draft;
      try {
        const saved = await review.mutateAsync({
          proposalId: authority.proposalId,
          request: buildPriceReviewRequest(row, {
            ...draft,
            proposalId: authority.proposalId,
            concurrencyToken: authority.concurrencyToken,
          }, inputMode),
        });
        if (businessScopeVersionRef.current !== operationBusinessScopeVersion) return;
        replaceDraft(row.productId, (current) => ({
          ...current,
          proposalId: saved.proposalId,
          concurrencyToken: saved.concurrencyToken,
          ...(current.salePrice === draft.salePrice && current.margin === draft.margin
            ? {
                salePrice: saved.preparedAmount,
                margin: saved.effectiveMarginPercent,
              }
            : {}),
        }), draft);
        saveErrorsRef.current.delete(row.productId);
        setSaveErrors((current) => {
          const next = { ...current };
          delete next[row.productId];
          return next;
        });
      } catch (error) {
        if (businessScopeVersionRef.current !== operationBusinessScopeVersion ||
            publicationScopeKeyRef.current !== operationScopeKey) return;
        const message = error instanceof Error
          ? error.message
          : `No fue posible guardar el precio preparado de ${row.productName}.`;
        saveErrorsRef.current.set(row.productId, message);
        setSaveErrors((current) => ({ ...current, [row.productId]: message }));
        toast.error(message);
      }
    });
    saveChainsRef.current.set(row.productId, operation);
    void operation.finally(() => {
      if (saveChainsRef.current.get(row.productId) !== operation) return;
      saveChainsRef.current.delete(row.productId);
      setSavingProductIds((current) => {
        const next = new Set(current);
        next.delete(row.productId);
        return next;
      });
    });
  }, [publicationScopeKey, replaceDraft, review]);

  const updateMargin = useCallback((row: PriceRevisionListItem, margin: number | null) => {
    const fallback = createPricePublicationDraft(row);
    let valid = true;
    const next = replaceDraft(row.productId, (draft) => {
      try {
        return changeDraftMargin(row, draft, margin);
      } catch {
        valid = false;
        return { ...draft, margin, salePrice: null };
      }
    }, fallback);
    if (valid) queueDraftSave(row, next, "Margin");
  }, [queueDraftSave, replaceDraft]);

  const updateSalePrice = useCallback((row: PriceRevisionListItem, salePrice: number | null) => {
    const fallback = createPricePublicationDraft(row);
    let valid = true;
    const next = replaceDraft(row.productId, (draft) => {
      try {
        return changeDraftSalePrice(row, draft, salePrice);
      } catch {
        valid = false;
        return { ...draft, salePrice };
      }
    }, fallback);
    if (valid) queueDraftSave(row, next, "SalePrice");
  }, [queueDraftSave, replaceDraft]);

  const navigatePricingGrid = useCallback((
    event: KeyboardEvent<HTMLInputElement>,
    row: PriceRevisionListItem,
    field: "margin" | "price",
  ) => {
    const rows = query.data?.items ?? [];
    const index = rows.findIndex((item) => item.proposalId === row.proposalId);
    let targetIndex = index;
    let targetField = field;
    if (event.key === "ArrowDown") targetIndex = Math.min(rows.length - 1, index + 1);
    else if (event.key === "ArrowUp") targetIndex = Math.max(0, index - 1);
    else if (event.key === "ArrowLeft") targetField = "margin";
    else if (event.key === "ArrowRight") targetField = "price";
    else if (event.key === "Enter") {
      if (field === "margin") targetField = "price";
      else {
        targetIndex = Math.min(rows.length - 1, index + 1);
        targetField = "margin";
      }
    } else return;
    event.preventDefault();
    const target = rows[targetIndex];
    if (!target) return;
    // Moving focus blurs the current formatted input, which commits its draft and
    // re-renders the row. Defer until that React commit has completed so focus is
    // applied to the current DOM node instead of the element being replaced.
    const focusTarget = () => {
      const input = document.getElementById(
        `pricing-${targetField}-${target.proposalId}`,
      ) as HTMLInputElement | null;
      input?.focus();
      input?.select();
    };
    window.setTimeout(() => {
      // The first focus commits the field being left. That commit can replace the
      // row, so resolve and focus the target once more after React has painted.
      focusTarget();
      window.requestAnimationFrame(focusTarget);
    }, 0);
  }, [query.data?.items]);

  const rejectRows = useCallback(async (rows: PriceRevisionListItem[]) => {
    const candidates = rows.filter(isPublishable);
    if (!candidates.length) {
      toast.info("La selección no contiene precios pendientes.");
      return;
    }
    try {
      for (const row of candidates) {
        const draft = draftFor(row);
        await reject.mutateAsync({
          proposalId: draft.proposalId,
          concurrencyToken: draft.concurrencyToken,
          reason: "Descartada desde la lista de precios",
        });
        setHiddenProductIds((current) => new Set(current).add(row.productId));
      }
      toast.success(candidates.length === 1 ? "Propuesta descartada." : "Propuestas descartadas.");
    } catch {
      toast.error("No fue posible descartar toda la selección.");
    }
  }, [draftFor, reject]);

  const publishRows = useCallback(async (
    rows: PriceRevisionListItem[],
    selection: { allMatching: boolean; selectedCount: number },
  ) => {
    const candidates = rows.filter(isPublishable);
    if (!selection.allMatching && !candidates.length) {
      toast.info("La selección no contiene precios pendientes.");
      return;
    }
    try {
      await Promise.all(saveChainsRef.current.values());
      const selectedProductIds = new Set(candidates.map((row) => row.productId));
      const hasRelevantSaveError = selection.allMatching
        ? saveErrorsRef.current.size > 0
        : [...saveErrorsRef.current.keys()].some((productId) => selectedProductIds.has(productId));
      if (hasRelevantSaveError)
        throw new Error("Hay precios que no pudieron guardarse. Corrígelos antes de publicar.");
      const result = await publish.mutateAsync(buildPricePublicationRequest(
        candidates,
        selection.allMatching,
        {
          search: search.trim() || undefined,
          supplierId: supplierId === "all" ? undefined : supplierId,
          sourceDocumentId,
        },
      ));
      const publishedProductIds = new Set(result.items.map((item) => item.productId));
      setHiddenProductIds((current) => {
        const next = new Set(current);
        publishedProductIds.forEach((productId) => next.add(productId));
        return next;
      });
      toast.success(result.items.length === 1
        ? "Precio publicado y notificado al punto de venta."
        : `${result.items.length} precios publicados en una sola operación.`);
      return true;
    } catch (error) {
      toast.error(error instanceof Error
        ? error.message
        : "No fue posible publicar. Actualiza la lista y vuelve a intentar.");
      return false;
    }
  }, [publish, search, sourceDocumentId, supplierId]);

  const openReport = useCallback((rows: PriceRevisionListItem[]) => {
    const candidates = rows.filter(isPublishable).flatMap((row) => {
      const preparedSalePrice = draftFor(row).salePrice;
      return preparedSalePrice !== null && preparedSalePrice > 0
        ? [{ productCode: row.productCode, productName: row.productName, currentSalePrice: row.currentSalePrice, preparedSalePrice }]
        : [];
    });
    if (!candidates.length) {
      toast.info("La selección no contiene precios preparados válidos.");
      return;
    }
    setPriceReportRows(candidates.map((row, index) => ({
      id: `${row.productCode}-${index}`,
      code: row.productCode,
      product: row.productName,
      currentPrice: row.currentSalePrice,
      preparedPrice: row.preparedSalePrice,
    })));
  }, [draftFor]);

  const columns = useMemo<ColumnDef<PriceRevisionListItem>[]>(() => [
    {
      accessorKey: "productName",
      header: "Producto",
      cell: ({ row }) => <div className="flex min-h-16 min-w-80 flex-col justify-center">
        <p className="font-semibold">{row.original.productName}</p>
        <p className="text-xs text-muted-foreground">
          {row.original.productCode} · {row.original.origin === "Product"
            ? "Preparado desde producto"
            : row.original.supplierName}
        </p>
        <p className="mt-1 text-xs text-muted-foreground">
          IVA de venta {formatPercent(row.original.salesTaxRate)} · {formatDateTime(row.original.createdAt)}
        </p>
      </div>,
    },
    {
      accessorKey: "observedUnitCost",
      header: "Costo base",
      cell: ({ row }) => <div className="grid min-h-20 min-w-28 grid-rows-[2.5rem_1rem] content-center">
        <p className="flex items-center font-medium">{formatCurrency(row.original.observedUnitCost)}</p>
        <p className="text-xs text-muted-foreground">
          Base usada para preparar
        </p>
      </div>,
    },
    {
      id: "costReferences",
      header: "Costos de referencia",
      cell: ({ row }) => <div className="min-w-40 space-y-1 text-sm">
        <p>Promedio: <span className="font-medium">{row.original.averageUnitCost == null ? "Sin movimientos" : formatCurrency(row.original.averageUnitCost)}</span></p>
        <p>Último proveedor: <span className="font-medium">{row.original.latestUnitCost == null ? "Sin recepciones" : formatCurrency(row.original.latestUnitCost)}</span></p>
        <p>Último costo total: <span className="font-medium">{row.original.latestLandedUnitCost == null ? "Sin recepciones" : formatCurrency(row.original.latestLandedUnitCost)}</span></p>
      </div>,
    },
    {
      accessorKey: "currentSalePrice",
      header: "Precio público actual",
      cell: ({ row }) => <div className="grid min-h-20 min-w-28 grid-rows-[2.5rem_1rem] content-center">
        <p className="flex items-center font-medium">{formatCurrency(row.original.currentSalePrice)}</p>
        <p className="text-xs text-muted-foreground">{publicationLabel(row.original.currentPricePublishedAt)}</p>
      </div>,
    },
    {
      id: "margin",
      header: "Margen preparado",
      cell: ({ row }) => {
        const draft = draftFor(row.original);
        return <div className="grid min-h-20 min-w-32 grid-rows-[2.5rem_1rem] content-center" title={proposalDisabledReason(row.original)}>
          <FormattedNumberInput
            id={`pricing-margin-${row.original.proposalId}`}
            kind="percent"
            commitMode="blur"
            value={draft.margin ?? ""}
            disabled={!canReview || !isPublishable(row.original)}
            onValueChange={(value) => updateMargin(row.original, value)}
            onKeyDown={(event) => navigatePricingGrid(event, row.original, "margin")}
            className="h-10 bg-background text-right font-medium"
          />
          <p className="text-xs text-muted-foreground">Sobre precio antes de IVA</p>
        </div>;
      },
    },
    {
      id: "preparedSalePrice",
      header: "Precio a publicar",
      cell: ({ row }) => {
        const draft = draftFor(row.original);
        return <div className="grid min-h-20 min-w-40 grid-rows-[2.5rem_1rem] content-center" title={proposalDisabledReason(row.original)}>
          <FormattedNumberInput
            id={`pricing-price-${row.original.proposalId}`}
            kind="currency"
            commitMode="blur"
            value={draft.salePrice ?? ""}
            disabled={!canReview || !isPublishable(row.original)}
            onValueChange={(value) => updateSalePrice(row.original, value)}
            onKeyDown={(event) => navigatePricingGrid(event, row.original, "price")}
            className="h-10 border-primary/40 bg-primary/5 text-right font-semibold text-primary"
          />
          <p className={`text-xs ${saveErrors[row.original.productId] ? "text-destructive" : "text-muted-foreground"}`}>
            {savingProductIds.has(row.original.productId)
              ? "Guardando precio preparado…"
              : saveErrors[row.original.productId]
                ? "No se guardó. Corrige el valor e intenta de nuevo."
                : "Guardado en precio preparado · IVA incluido"}
          </p>
        </div>;
      },
    },
    {
      accessorKey: "status",
      header: "Estado",
      cell: ({ row }) => <div className="flex min-h-16 min-w-36 flex-col justify-center gap-1" title={proposalDisabledReason(row.original)}>
        <Badge className="w-fit" variant={
          row.original.status === "Published" ? "secondary" :
            row.original.status === "Rejected" ? "destructive" : "outline"
        }>{statuses[row.original.status]}</Badge>
        {row.original.status === "Superseded" && <p className="text-xs text-muted-foreground">
          Existe una propuesta más reciente.
        </p>}
      </div>,
    },
    {
      id: "actions",
      header: "",
      cell: ({ row }) => <div className="flex items-center justify-end gap-1">
        {canReadHistory && <Button type="button" variant="ghost" size="sm" onClick={() => setHistoryProduct({ id: row.original.productId, name: row.original.productName })} aria-label={`Ver historial de ${row.original.productName}`}><History className="mr-2 h-4 w-4" />Historial</Button>}
        {isPublishable(row.original) && canPublish && <Button type="button" variant="ghost" size="sm" disabled={publish.isPending} onClick={() => void publishRows([row.original], { allMatching: false, selectedCount: 1 })} aria-label={`Publicar precio de ${row.original.productName}`}><Send className="mr-2 h-4 w-4" />Publicar</Button>}
        {isPublishable(row.original) && canReview && <Button type="button" variant="ghost" size="sm" disabled={reject.isPending} onClick={() => void rejectRows([row.original])} aria-label={`Descartar propuesta de ${row.original.productName}`}><XCircle className="mr-2 h-4 w-4" />Descartar</Button>}
      </div>,
    },
  ], [canPublish, canReadHistory, canReview, draftFor, navigatePricingGrid, publish.isPending, publishRows, reject.isPending, rejectRows, saveErrors, savingProductIds, updateMargin, updateSalePrice]);

  const bulkActions = useMemo(() => {
    const actions = [] as Array<{
      label: string;
      onClick: (
        rows: PriceRevisionListItem[],
        selection: { allMatching: boolean; selectedCount: number },
      ) => void | boolean | Promise<void | boolean>;
      variant?: "default" | "destructive";
      disabled?: boolean;
      supportsAllMatching?: boolean;
    }>;
    if (canPublish && canBulk) actions.push({
      label: publish.isPending ? "Publicando..." : "Publicar selección",
      onClick: publishRows,
      disabled: publish.isPending,
      supportsAllMatching: true,
    });
    if (canPublish || canReview) actions.push({
      label: "Reporte para mostradores",
      onClick: openReport,
      supportsAllMatching: false,
    });
    if (canReview) actions.push({
      label: "Descartar selección",
      onClick: (rows) => void rejectRows(rows),
      variant: "destructive",
      supportsAllMatching: false,
    });
    return actions;
  }, [canBulk, canPublish, canReview, openReport, publish.isPending, publishRows, rejectRows]);

  const visibleItems = status === "Pending"
    ? (query.data?.items ?? []).filter((item) => !hiddenProductIds.has(item.productId))
    : (query.data?.items ?? []);

  if (priceReportRows) return <ReportViewer
    onClose={() => setPriceReportRows(null)}
    title="Actualización de precios para mostradores"
    description="Precio público actual frente al precio preparado pendiente de publicación · orden de la selección"
    fileName="actualizacion-precios-mostradores"
    rows={priceReportRows}
    columns={[
      { key: "code", label: "Código interno" },
      { key: "product", label: "Producto" },
      { key: "currentPrice", label: "Precio público actual", align: "right", format: (value) => formatCurrency(Number(value)) },
      { key: "preparedPrice", label: "Precio preparado", align: "right", format: (value) => formatCurrency(Number(value)) },
    ]}
  />;

  return <div className="space-y-6">
    <ProductPriceHistoryDialog
      productId={historyProduct?.id}
      productName={historyProduct?.name}
      open={Boolean(historyProduct)}
      onOpenChange={(open) => { if (!open) setHistoryProduct(undefined); }}
    />
    <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
      <div>
        <p className="text-sm font-medium text-primary">Productos</p>
        <h1 className="text-3xl font-semibold tracking-tight">Precios y rentabilidad</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">
          Ajusta margen o precio directamente en la lista, selecciona los productos y publícalos juntos.
          El precio preparado nunca se recalcula al publicar.
        </p>
      </div>
      <div className="rounded-2xl border bg-card px-5 py-3 text-sm shadow-sm">
        <p className="text-muted-foreground">Flujo de publicación</p>
        <p className="font-semibold">Editar en grilla → seleccionar → publicar</p>
      </div>
    </header>

    <section className="grid gap-3 md:grid-cols-3">
      <Summary icon={PackageCheck} label="Propuestas encontradas" value={String(query.data?.totalCount ?? 0)} />
      <Summary icon={TrendingUp} label="Cálculo reactivo" value="Costo + margen + IVA" />
      <Summary icon={Send} label="Publicación" value="Masiva y en una operación" />
    </section>

    <section className="grid gap-3 rounded-2xl border bg-card p-4 md:grid-cols-[minmax(0,1fr)_14rem_16rem]">
      <ServerSearchInput value={search} onSearch={(value) => { setSearch(value); setPage(1); }} isSearching={query.isFetching} placeholder="Producto, código o proveedor" />
      <Select value={status} onValueChange={(value) => {
        setStatus(value as PriceProposalStatus | "Pending" | "all");
        setPage(1);
      }}>
        <SelectTrigger><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="Pending">Pendientes por publicar</SelectItem>
          <SelectItem value="all">Todos los estados</SelectItem>
          {Object.entries(statuses).map(([value, label]) =>
            <SelectItem key={value} value={value}>{label}</SelectItem>)}
        </SelectContent>
      </Select>
      <PartyRoleSelect role="Supplier" value={supplierId}
        preload
        leadingOptions={[{value:"all",label:"Todos los proveedores"}]}
        placeholder="Buscar proveedor" onChange={(value) => { setSupplierId(value); setPage(1); }}/>
    </section>

    {query.isError ? <div className="rounded-2xl border border-destructive/30 p-8 text-center">
      No se pudieron cargar las propuestas.
      <Button variant="link" onClick={() => query.refetch()}>Reintentar</Button>
    </div> : <DataTable
      columns={columns}
      data={visibleItems}
      isLoading={query.isLoading}
      page={query.data?.page}
      pageSize={query.data?.pageSize}
      pageCount={query.data?.totalPages}
      totalItems={query.data?.totalCount}
      onPaginationChange={(nextPage, nextSize) => { setPage(nextPage); setPageSize(nextSize); }}
      enableRowSelection={bulkActions.length > 0}
      selectAllMatching={status === "Pending"}
      selectionScopeKey={publicationScopeKey}
      getRowId={(row) => row.proposalId}
      bulkActions={bulkActions}
    />}
  </div>;
}

function proposalDisabledReason(row: PriceRevisionListItem) {
  if (row.status === "Superseded") return "No se puede editar porque esta propuesta fue reemplazada por otra más reciente.";
  if (row.status === "Published") return "Este precio ya fue publicado y se conserva como historial.";
  if (row.status === "Rejected") return "Esta propuesta fue descartada y se conserva como historial.";
  return undefined;
}

function publicationLabel(value: string | null) {
  if (!value) return "Fecha de publicación no disponible";
  const relative = formatRelativeTime(value);
  return relative === "Ahora" ? "Publicado ahora" : `Publicado ${relative.toLocaleLowerCase("es-CO")}`;
}

function isPublishable(row: PriceRevisionListItem) {
  return row.status === "PendingReview" || row.status === "Approved";
}

function Summary({ icon: Icon, label, value }: {
  icon: typeof PackageCheck;
  label: string;
  value: string;
}) {
  return <Card><CardContent className="flex items-center gap-3 p-4">
    <span className="rounded-xl bg-primary/10 p-2 text-primary"><Icon className="h-5 w-5" /></span>
    <div><p className="text-xs text-muted-foreground">{label}</p><p className="font-semibold">{value}</p></div>
  </CardContent></Card>;
}

function formatPercent(value: number | null) {
  return value === null ? "Sin definir" : `${new Intl.NumberFormat("es-CO", {
    maximumFractionDigits: 4,
  }).format(value)} %`;
}
