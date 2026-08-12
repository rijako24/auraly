"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "next/navigation";
import type { ColumnDef } from "@tanstack/react-table";
import { PackageCheck, Search, Send, TrendingUp, XCircle } from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Input } from "@/components/ui/input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { usePriceProposals, usePublishPrices, useRejectPrice } from "@/hooks/use-pricing";
import {
  buildPricePublicationItem,
  changeDraftMargin,
  changeDraftSalePrice,
  createPricePublicationDraft,
  type PricePublicationDraft,
} from "@/lib/pricing-publication-draft";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { goodsReceiptsApi } from "@/services/api/goods-receipts";
import type { PriceProposalStatus, PriceRevisionListItem } from "@/services/api/pricing";
import { useAuthStore } from "@/stores/auth-store";

const statuses: Record<PriceProposalStatus, string> = {
  PendingReview: "Pendiente",
  Approved: "Preparada",
  Published: "Publicada",
  Rejected: "Rechazada",
  Superseded: "Superada",
};

export default function PricingPage() {
  const searchParams = useSearchParams();
  const sourceDocumentId = searchParams.get("sourceDocumentId") ?? undefined;
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canReview = permissions.has("pricing.proposals.review");
  const canPublish = permissions.has("pricing.prices.publish");
  const canBulk = permissions.has("pricing.bulk-publish");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<PriceProposalStatus | "all">("all");
  const [supplierId, setSupplierId] = useState("all");
  const [drafts, setDrafts] = useState<Record<string, PricePublicationDraft>>({});
  const draftsRef = useRef(drafts);
  draftsRef.current = drafts;
  const receiptOptions = useQuery({
    queryKey: ["goods-receipt-options"],
    queryFn: goodsReceiptsApi.options,
  });
  const query = usePriceProposals({
    page,
    pageSize,
    search: search.trim() || undefined,
    status: status === "all" ? undefined : status,
    supplierId: supplierId === "all" ? undefined : supplierId,
    sourceDocumentId,
  });
  const publish = usePublishPrices();
  const reject = useRejectPrice();

  useEffect(() => {
    const rows = query.data?.items ?? [];
    if (!rows.length) return;
    setDrafts((current) => {
      const next = { ...current };
      for (const row of rows) {
        const existing = next[row.proposalId];
        if (!existing || existing.concurrencyToken !== row.concurrencyToken)
          next[row.proposalId] = createPricePublicationDraft(row);
      }
      return next;
    });
  }, [query.data?.items]);

  const draftFor = useCallback((row: PriceRevisionListItem) =>
    draftsRef.current[row.proposalId] ?? createPricePublicationDraft(row), []);

  const updateMargin = useCallback((row: PriceRevisionListItem, margin: number | null) => {
    setDrafts((current) => {
      const draft = current[row.proposalId] ?? createPricePublicationDraft(row);
      try {
        return { ...current, [row.proposalId]: changeDraftMargin(row, draft, margin) };
      } catch {
        return { ...current, [row.proposalId]: { ...draft, margin, salePrice: null } };
      }
    });
  }, []);

  const updateSalePrice = useCallback((row: PriceRevisionListItem, salePrice: number | null) => {
    setDrafts((current) => {
      const draft = current[row.proposalId] ?? createPricePublicationDraft(row);
      try {
        return { ...current, [row.proposalId]: changeDraftSalePrice(row, draft, salePrice) };
      } catch {
        return { ...current, [row.proposalId]: { ...draft, salePrice } };
      }
    });
  }, []);

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
        await reject.mutateAsync({
          proposalId: row.proposalId,
          concurrencyToken: row.concurrencyToken,
          reason: "Descartada desde la lista de precios",
        });
      }
      toast.success(candidates.length === 1 ? "Propuesta descartada." : "Propuestas descartadas.");
    } catch {
      toast.error("No fue posible descartar toda la selección.");
    }
  }, [reject]);

  const publishRows = useCallback(async (rows: PriceRevisionListItem[]) => {
    const candidates = rows.filter(isPublishable);
    if (!candidates.length) {
      toast.info("La selección no contiene precios pendientes.");
      return;
    }
    try {
      const items = candidates.map((row) =>
        buildPricePublicationItem(row, drafts[row.proposalId] ?? createPricePublicationDraft(row)));
      await publish.mutateAsync(items);
      toast.success(candidates.length === 1
        ? "Precio publicado y notificado al punto de venta."
        : `${candidates.length} precios publicados en una sola operación.`);
    } catch (error) {
      toast.error(error instanceof Error
        ? error.message
        : "No fue posible publicar. Actualiza la lista y vuelve a intentar.");
    }
  }, [drafts, publish]);

  const columns = useMemo<ColumnDef<PriceRevisionListItem>[]>(() => [
    {
      accessorKey: "productName",
      header: "Producto",
      cell: ({ row }) => <div className="min-w-52">
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
      cell: ({ row }) => <div className="min-w-28">
        <p className="font-medium">{formatCurrency(row.original.observedUnitCost)}</p>
        {row.original.previousObservedUnitCost !== null && <p className="text-xs text-muted-foreground">
          Antes {formatCurrency(row.original.previousObservedUnitCost)}
        </p>}
      </div>,
    },
    {
      accessorKey: "currentSalePrice",
      header: "Precio público actual",
      cell: ({ row }) => <div className="min-w-28">
        <p className="font-medium">{formatCurrency(row.original.currentSalePrice)}</p>
        <p className="text-xs text-muted-foreground">Publicado hoy</p>
      </div>,
    },
    {
      id: "margin",
      header: "Margen preparado",
      cell: ({ row }) => {
        const draft = draftFor(row.original);
        return <div className="min-w-32">
          <FormattedNumberInput
            id={`pricing-margin-${row.original.proposalId}`}
            kind="percent"
            commitMode="blur"
            value={draft.margin ?? ""}
            disabled={!canPublish || !isPublishable(row.original)}
            onValueChange={(value) => updateMargin(row.original, value)}
            onKeyDown={(event) => navigatePricingGrid(event, row.original, "margin")}
            className="h-10 bg-background text-right font-medium"
          />
          <p className="mt-1 text-xs text-muted-foreground">Sobre precio antes de IVA</p>
        </div>;
      },
    },
    {
      id: "preparedSalePrice",
      header: "Precio a publicar",
      cell: ({ row }) => {
        const draft = draftFor(row.original);
        return <div className="min-w-40">
          <FormattedNumberInput
            id={`pricing-price-${row.original.proposalId}`}
            kind="currency"
            commitMode="blur"
            value={draft.salePrice ?? ""}
            disabled={!canPublish || !isPublishable(row.original)}
            onValueChange={(value) => updateSalePrice(row.original, value)}
            onKeyDown={(event) => navigatePricingGrid(event, row.original, "price")}
            className="h-10 border-primary/40 bg-primary/5 text-right font-semibold text-primary"
          />
          <p className="mt-1 text-xs text-muted-foreground">Valor final con IVA incluido</p>
        </div>;
      },
    },
    {
      accessorKey: "status",
      header: "Estado",
      cell: ({ row }) => <Badge variant={
        row.original.status === "Published" ? "secondary" :
          row.original.status === "Rejected" ? "destructive" : "outline"
      }>{statuses[row.original.status]}</Badge>,
    },
    {
      id: "actions",
      header: "",
      cell: ({ row }) => canReview && isPublishable(row.original) ? <Button
        type="button"
        variant="ghost"
        size="sm"
        disabled={reject.isPending}
        onClick={() => void rejectRows([row.original])}
        aria-label={`Descartar propuesta de ${row.original.productName}`}
      >
        <XCircle className="mr-2 h-4 w-4" />
        Descartar
      </Button> : null,
    },
  ], [canPublish, canReview, draftFor, navigatePricingGrid, reject.isPending, rejectRows, updateMargin, updateSalePrice]);

  const bulkActions = useMemo(() => {
    const actions = [] as Array<{
      label: string;
      onClick: (rows: PriceRevisionListItem[]) => void;
      variant?: "default" | "destructive";
    }>;
    if (canPublish && canBulk) actions.push({
      label: publish.isPending ? "Publicando..." : "Publicar selección",
      onClick: (rows) => void publishRows(rows),
    });
    if (canReview) actions.push({
      label: "Descartar selección",
      onClick: (rows) => void rejectRows(rows),
      variant: "destructive",
    });
    return actions;
  }, [canBulk, canPublish, canReview, publish.isPending, publishRows, rejectRows]);

  return <div className="space-y-6">
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
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input
          className="pl-9"
          value={search}
          onChange={(event) => { setSearch(event.target.value); setPage(1); }}
          placeholder="Producto, código o proveedor"
        />
      </div>
      <Select value={status} onValueChange={(value) => {
        setStatus(value as PriceProposalStatus | "all");
        setPage(1);
      }}>
        <SelectTrigger><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos los estados</SelectItem>
          {Object.entries(statuses).map(([value, label]) =>
            <SelectItem key={value} value={value}>{label}</SelectItem>)}
        </SelectContent>
      </Select>
      <Select value={supplierId} onValueChange={(value) => {
        setSupplierId(value);
        setPage(1);
      }}>
        <SelectTrigger><SelectValue placeholder="Todos los proveedores" /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos los proveedores</SelectItem>
          {(receiptOptions.data?.suppliers ?? []).map((supplier) =>
            <SelectItem key={supplier.supplierId} value={supplier.supplierId}>{supplier.name}</SelectItem>)}
        </SelectContent>
      </Select>
    </section>

    {query.isError ? <div className="rounded-2xl border border-destructive/30 p-8 text-center">
      No se pudieron cargar las propuestas.
      <Button variant="link" onClick={() => query.refetch()}>Reintentar</Button>
    </div> : <DataTable
      columns={columns}
      data={query.data?.items ?? []}
      isLoading={query.isLoading}
      page={query.data?.page}
      pageSize={query.data?.pageSize}
      pageCount={query.data?.totalPages}
      totalItems={query.data?.totalCount}
      onPaginationChange={(nextPage, nextSize) => { setPage(nextPage); setPageSize(nextSize); }}
      enableRowSelection={bulkActions.length > 0}
      bulkActions={bulkActions}
    />}
  </div>;
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
