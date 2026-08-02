"use client";

import { useEffect, useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import {
  ArrowRight, Calculator, PackageCheck, Search, Send,
  TrendingUp, XCircle,
} from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import {
  useCalculatePrice, usePriceProposals, usePublishPrices,
  useRejectPrice, useReviewPrice,
} from "@/hooks/use-pricing";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { useAuthStore } from "@/stores/auth-store";
import type {
  PriceInputMode, PriceProposalStatus, PriceRevisionListItem,
  PricingRoundingMode, PublishPriceItem,
} from "@/services/api/pricing";

const statuses: Record<PriceProposalStatus, string> = {
  PendingReview: "Pendiente",
  Approved: "Preparada",
  Published: "Publicada",
  Rejected: "Rechazada",
  Superseded: "Superada",
};

export default function PricingPage() {
  const permissions = useAuthStore((state) => new Set(state.user?.permissions ?? []));
  const canReview = permissions.has("pricing.proposals.review");
  const canPublish = permissions.has("pricing.prices.publish");
  const canBulk = permissions.has("pricing.bulk-publish");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState<PriceProposalStatus | "all">("PendingReview");
  const [selected, setSelected] = useState<PriceRevisionListItem>();

  const query = usePriceProposals({
    page, pageSize, search: search.trim() || undefined,
    status: status === "all" ? undefined : status,
  });
  const publish = usePublishPrices();

  const columns = useMemo<ColumnDef<PriceRevisionListItem>[]>(() => [
    {
      accessorKey: "productName",
      header: "Producto",
      cell: ({ row }) => <div>
        <p className="font-semibold">{row.original.productName}</p>
        <p className="text-xs text-muted-foreground">
          {row.original.productCode} - {row.original.supplierName}
        </p>
      </div>,
    },
    {
      accessorKey: "observedUnitCost",
      header: "Costo observado",
      cell: ({ row }) => <div>
        <p className="font-medium">{formatCurrency(row.original.observedUnitCost)}</p>
        {row.original.previousObservedUnitCost !== null && <p className="text-xs text-muted-foreground">
          Antes {formatCurrency(row.original.previousObservedUnitCost)}
        </p>}
      </div>,
    },
    {
      accessorKey: "currentSalePrice",
      header: "Precio actual",
      cell: ({ row }) => formatCurrency(row.original.currentSalePrice),
    },
    {
      id: "proposal",
      header: "Propuesta",
      cell: ({ row }) => <div>
        <p className="font-semibold text-primary">{formatCurrency(row.original.suggestedSalePrice)}</p>
        <p className="text-xs text-muted-foreground">
          Margen {formatPercent(row.original.effectiveMarginAfterRounding ?? row.original.targetMarginPercent)}
        </p>
      </div>,
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
      accessorKey: "createdAt",
      header: "Detectada",
      cell: ({ row }) => formatDateTime(row.original.createdAt),
    },
  ], []);

  const publishRows = async (rows: PriceRevisionListItem[]) => {
    const candidates = rows.filter((row) =>
      row.status === "PendingReview" || row.status === "Approved");
    if (!candidates.length) return;
    try {
      await publish.mutateAsync(candidates.map((row) => ({
        proposalId: row.proposalId,
        inputMode: "SalePrice" as const,
        targetMarginPercent: null,
        salePrice: row.suggestedSalePrice,
        roundingIncrement: 1,
        roundingMode: "Nearest" as const,
        concurrencyToken: row.concurrencyToken,
      })));
      toast.success(candidates.length === 1
        ? "El precio fue publicado y se notifico al POS."
        : `${candidates.length} precios fueron publicados y notificados.`);
    } catch {
      toast.error("No fue posible publicar. Actualiza las propuestas y vuelve a intentar.");
    }
  };

  return <div className="space-y-6">
    <header className="flex flex-col justify-between gap-4 lg:flex-row lg:items-end">
      <div>
        <p className="text-sm font-medium text-primary">Productos</p>
        <h1 className="text-3xl font-semibold tracking-tight">Precios y rentabilidad</h1>
        <p className="mt-1 max-w-3xl text-muted-foreground">
          Revisa cambios de costo, conserva el margen que necesitas y publica el precio
          vendido solo cuando estes listo.
        </p>
      </div>
      <div className="rounded-2xl border bg-card px-5 py-3 text-sm shadow-sm">
        <p className="text-muted-foreground">Regla activa</p>
        <p className="font-semibold">La entrada propone; nunca cambia el precio sola</p>
      </div>
    </header>

    <section className="grid gap-3 md:grid-cols-3">
      <Summary icon={PackageCheck} label="Pendientes encontradas"
        value={String(query.data?.totalCount ?? 0)} />
      <Summary icon={TrendingUp} label="Flujo de margen"
        value="Costo | margen | precio" />
      <Summary icon={Send} label="Sincronizacion"
        value="Push por cursor, sin polling" />
    </section>

    <section className="grid gap-3 rounded-2xl border bg-card p-4 md:grid-cols-[minmax(0,1fr)_14rem]">
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
        <Input className="pl-9" value={search}
          onChange={(event) => { setSearch(event.target.value); setPage(1); }}
          placeholder="Producto, codigo o proveedor" />
      </div>
      <Select value={status} onValueChange={(value) => {
        setStatus(value as PriceProposalStatus | "all"); setPage(1);
      }}>
        <SelectTrigger><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Todos los estados</SelectItem>
          {Object.entries(statuses).map(([value, label]) =>
            <SelectItem key={value} value={value}>{label}</SelectItem>)}
        </SelectContent>
      </Select>
    </section>

    {query.isError ? <div className="rounded-2xl border border-destructive/30 p-8 text-center">
      No se pudieron cargar las propuestas.
      <Button variant="link" onClick={() => query.refetch()}>Reintentar</Button>
    </div> : <DataTable columns={columns} data={query.data?.items ?? []}
      isLoading={query.isLoading} page={query.data?.page} pageSize={query.data?.pageSize}
      pageCount={query.data?.totalPages} totalItems={query.data?.totalCount}
      onPaginationChange={(nextPage, nextSize) => { setPage(nextPage); setPageSize(nextSize); }}
      onRowClick={setSelected} enableRowSelection={canBulk}
      bulkActions={canBulk ? [{
        label: "Publicar seleccionados",
        onClick: publishRows,
      }] : undefined} />}

    <PriceEditor open={!!selected} proposal={selected}
      canReview={canReview} canPublish={canPublish}
      onClose={() => setSelected(undefined)} />
  </div>;
}

function PriceEditor({
  open, proposal, canReview, canPublish, onClose,
}: {
  open: boolean;
  proposal?: PriceRevisionListItem;
  canReview: boolean;
  canPublish: boolean;
  onClose: () => void;
}) {
  const [inputMode, setInputMode] = useState<PriceInputMode>("Margin");
  const [margin, setMargin] = useState("");
  const [salePrice, setSalePrice] = useState("");
  const [increment, setIncrement] = useState("50");
  const [roundingMode, setRoundingMode] = useState<PricingRoundingMode>("Up");
  const calculate = useCalculatePrice();
  const review = useReviewPrice();
  const reject = useRejectPrice();
  const publish = usePublishPrices();

  useEffect(() => {
    if (!proposal) return;
    setMargin(String(proposal.targetMarginPercent ?? proposal.currentMarginPercent ?? ""));
    setSalePrice(String(proposal.suggestedSalePrice));
  }, [proposal]);


  const command = (): PublishPriceItem | null => {
    if (!proposal) return null;
    return {
      proposalId: proposal.proposalId,
      inputMode,
      targetMarginPercent: inputMode === "Margin" ? numberOrNull(margin) : null,
      salePrice: inputMode === "SalePrice" ? numberOrNull(salePrice) : null,
      roundingIncrement: Number(increment),
      roundingMode,
      concurrencyToken: proposal.concurrencyToken,
    };
  };

  const preview = async () => {
    const value = command();
    if (!value || !proposal) return;
    try {
      await calculate.mutateAsync({
        costBasisAmount: proposal.observedUnitCost,
        inputMode: value.inputMode,
        targetMarginPercent: value.targetMarginPercent,
        salePrice: value.salePrice,
        roundingIncrement: value.roundingIncrement,
        roundingMode: value.roundingMode,
      });
    } catch {
      toast.error("Revisa margen, precio y redondeo.");
    }
  };

  const save = async () => {
    const value = command(); if (!value) return;
    try {
      await review.mutateAsync(value);
      toast.success("Propuesta guardada sin cambiar el precio vendido.");
      onClose();
    } catch { toast.error("La propuesta cambio o contiene valores invalidos."); }
  };

  const publishNow = async () => {
    const value = command(); if (!value) return;
    try {
      await publish.mutateAsync([value]);
      toast.success("Precio publicado. Los equipos activos recibiran el cambio.");
      onClose();
    } catch { toast.error("No fue posible publicar el precio."); }
  };

  const rejectNow = async () => {
    if (!proposal) return;
    try {
      await reject.mutateAsync({
        proposalId: proposal.proposalId,
        concurrencyToken: proposal.concurrencyToken,
      });
      toast.success("Propuesta rechazada; el precio actual se conserva.");
      onClose();
    } catch { toast.error("No fue posible rechazar la propuesta."); }
  };

  return <Dialog open={open} onOpenChange={(value) => !value && onClose()}>
    <DialogContent className="max-h-[92dvh] overflow-y-auto sm:max-w-3xl">
      <DialogHeader>
        <DialogTitle>{proposal?.productName ?? "Revisar precio"}</DialogTitle>
        <DialogDescription>
          {proposal ? `${proposal.productCode} - Costo observado por ${proposal.supplierName}` : ""}
        </DialogDescription>
      </DialogHeader>
      {proposal && <div className="space-y-5">
        <div className="grid gap-3 rounded-2xl bg-muted/40 p-4 sm:grid-cols-3">
          <Metric label="Costo base" value={formatCurrency(proposal.observedUnitCost)} />
          <Metric label="Precio actual" value={formatCurrency(proposal.currentSalePrice)} />
          <Metric label="Margen actual" value={formatPercent(proposal.currentMarginPercent)} />
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-2">
            <Label>Quiero definir</Label>
            <Select value={inputMode} onValueChange={(value) => setInputMode(value as PriceInputMode)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="Margin">Margen sobre venta</SelectItem>
                <SelectItem value="SalePrice">Precio de venta</SelectItem>
              </SelectContent>
            </Select>
          </div>
          {inputMode === "Margin" ? <div className="space-y-2">
            <Label htmlFor="pricing-margin">Margen objetivo %</Label>
            <Input id="pricing-margin" inputMode="decimal" value={margin}
              onChange={(event) => setMargin(event.target.value)} />
          </div> : <div className="space-y-2">
            <Label htmlFor="pricing-sale">Precio de venta</Label>
            <Input id="pricing-sale" inputMode="decimal" value={salePrice}
              onChange={(event) => setSalePrice(event.target.value)} />
          </div>}
          <div className="space-y-2">
            <Label htmlFor="pricing-increment">Redondear a multiplos de</Label>
            <Input id="pricing-increment" inputMode="decimal" value={increment}
              onChange={(event) => setIncrement(event.target.value)} />
          </div>
          <div className="space-y-2">
            <Label>Direccion del redondeo</Label>
            <Select value={roundingMode}
              onValueChange={(value) => setRoundingMode(value as PricingRoundingMode)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="Up">Hacia arriba</SelectItem>
                <SelectItem value="Nearest">Al mas cercano</SelectItem>
                <SelectItem value="Down">Hacia abajo</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <Button variant="outline" onClick={preview} disabled={calculate.isPending}>
          <Calculator className="mr-2 h-4 w-4" />
          {calculate.isPending ? "Calculando..." : "Calcular resultado"}
        </Button>

        {calculate.data && <div className="grid items-center gap-3 rounded-2xl border border-primary/20 bg-primary/5 p-5 sm:grid-cols-[1fr_auto_1fr]">
          <Metric label="Precio sin redondear" value={formatCurrency(calculate.data.unroundedSalePrice)} />
          <ArrowRight className="hidden h-5 w-5 text-primary sm:block" />
          <div>
            <p className="text-xs uppercase tracking-wide text-muted-foreground">Precio a publicar</p>
            <p className="text-2xl font-semibold text-primary">{formatCurrency(calculate.data.roundedSalePrice)}</p>
            <p className="text-sm text-muted-foreground">Margen efectivo {formatPercent(calculate.data.effectiveMarginPercent)}</p>
          </div>
        </div>}
      </div>}
      <DialogFooter className="gap-2 sm:justify-between">
        <div>{canReview && proposal?.status !== "Published" &&
          <Button variant="ghost" className="text-destructive" onClick={rejectNow}>
            <XCircle className="mr-2 h-4 w-4" /> Rechazar
          </Button>}</div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={onClose}>Cerrar</Button>
          {canReview && <Button variant="secondary" onClick={save}>Guardar propuesta</Button>}
          {canPublish && <Button onClick={publishNow}>
            <Send className="mr-2 h-4 w-4" /> Publicar precio
          </Button>}
        </div>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

function Summary({ icon: Icon, label, value }: {
  icon: typeof PackageCheck; label: string; value: string;
}) {
  return <Card><CardContent className="flex items-center gap-4 p-5">
    <div className="rounded-xl bg-primary/10 p-3 text-primary"><Icon className="h-5 w-5" /></div>
    <div><p className="text-xs uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className="mt-1 font-semibold">{value}</p></div>
  </CardContent></Card>;
}

function Metric({ label, value }: { label: string; value: string }) {
  return <div><p className="text-xs uppercase tracking-wide text-muted-foreground">{label}</p>
    <p className="mt-1 text-lg font-semibold">{value}</p></div>;
}

function formatPercent(value: number | null | undefined) {
  return value === null || value === undefined ? "No calculable" :
    `${new Intl.NumberFormat("es-CO", { maximumFractionDigits: 2 }).format(value)} %`;
}

function numberOrNull(value: string) {
  const parsed = Number(value.replace(",", "."));
  return Number.isFinite(parsed) ? parsed : null;
}
