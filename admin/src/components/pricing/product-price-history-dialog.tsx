"use client";

import { History, PackageOpen, Send } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { useProductPriceHistory } from "@/hooks/use-pricing";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import type { ProductPriceHistoryItem } from "@/services/api/pricing";

export function ProductPriceHistoryDialog({ productId, productName, open, onOpenChange }: {
  productId?: string;
  productName?: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const history = useProductPriceHistory(productId, open);
  return <Dialog open={open} onOpenChange={onOpenChange}>
    <DialogContent className="max-h-[85vh] max-w-4xl overflow-y-auto">
      <DialogHeader>
        <DialogTitle className="flex items-center gap-2"><History className="h-5 w-5" /> Kardex de precio y rentabilidad</DialogTitle>
        <DialogDescription>{productName ?? "Producto"} · preparaciones y publicaciones, empezando por la más reciente.</DialogDescription>
      </DialogHeader>
      {history.isLoading ? <p className="py-10 text-center text-sm text-muted-foreground">Cargando historial…</p>
        : history.isError ? <p className="rounded-xl border border-destructive/30 p-5 text-sm text-destructive">No fue posible cargar el historial.</p>
        : !history.data?.length ? <p className="py-10 text-center text-sm text-muted-foreground">Este producto todavía no tiene movimientos de preparación o publicación.</p>
        : <div className="space-y-3">
          {history.data.map((item) => <HistoryRow key={item.activityId} item={item} />)}
        </div>}
    </DialogContent>
  </Dialog>;
}

function HistoryRow({ item }: { item: ProductPriceHistoryItem }) {
  const published = item.activityType === "Publication";
  return <article className="rounded-2xl border bg-card p-4">
    <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-start">
      <div className="flex items-start gap-3">
        <span className={`rounded-xl p-2 ${published ? "bg-primary/10 text-primary" : "bg-amber-500/10 text-amber-700"}`}>
          {published ? <Send className="h-4 w-4" /> : <PackageOpen className="h-4 w-4" />}
        </span>
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <p className="font-semibold">{published ? "Conjunto publicado" : "Conjunto preparado"}</p>
            <Badge variant={item.status === "Pending" ? "default" : "outline"}>{statusLabel(item.status)}</Badge>
          </div>
          <p className="text-xs text-muted-foreground">{originLabel(item.origin)} · {item.userName} · {formatDateTime(item.occurredAt)}</p>
        </div>
      </div>
      <div className="text-left sm:text-right">
        <p className="text-xs text-muted-foreground">{published ? "Precio publicado" : "Precio preparado"}</p>
        <p className="text-lg font-semibold">{formatCurrency(item.preparedAmount)}</p>
      </div>
    </div>
    <div className="mt-4 grid gap-3 rounded-xl bg-muted/30 p-3 text-sm sm:grid-cols-4">
      <Value label="Público anterior" value={formatCurrency(item.publicAmount)} />
      <Value label="Costo base" value={item.costBasisAmount == null ? "Sin costo" : formatCurrency(item.costBasisAmount)} />
      <Value label="Margen objetivo" value={percent(item.targetMarginPercent)} />
      <Value label="Margen resultante" value={percent(item.effectiveMarginPercent)} />
    </div>
    {item.sourceDocumentId && <p className="mt-3 text-xs text-muted-foreground">
      Documento de origen {item.sourceDocumentId}{item.sourceLineNumber ? ` · línea ${item.sourceLineNumber}` : ""}
    </p>}
  </article>;
}

function Value({ label, value }: { label: string; value: string }) {
  return <div><p className="text-xs text-muted-foreground">{label}</p><p className="font-medium">{value}</p></div>;
}

function percent(value: number | null) {
  return value == null ? "Sin calcular" : `${new Intl.NumberFormat("es-CO", { maximumFractionDigits: 4 }).format(value)} %`;
}

function statusLabel(status: ProductPriceHistoryItem["status"]) {
  return ({ Pending: "Vigente", Published: "Publicada", Superseded: "Reemplazada", Discarded: "Descartada" })[status];
}

function originLabel(origin: ProductPriceHistoryItem["origin"]) {
  return ({
    GoodsReceipt: "Recepción de compra",
    ProposalReview: "Revisión de recepción",
    Product: "Ficha del producto",
    LinkedProduct: "Producto vinculado",
    Migration: "Preparación anterior",
    ReceiptProposal: "Publicación de recepción",
    Manual: "Publicación desde producto",
  })[origin] ?? origin;
}
