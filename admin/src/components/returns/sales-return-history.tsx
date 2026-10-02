"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Eye, Printer } from "lucide-react";
import { toast } from "sonner";
import { DataTable } from "@/components/tables/data-table";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { PartyRoleSelect } from "@/components/parties/party-role-select";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { salesReturnsApi, type SalesReturnDetail, type SalesReturnListItem } from "@/services/api/sales-returns";
import type { PosSalesReturnRuntime } from "@/hooks/use-sales-returns";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { fiscalStatusLabel } from "@/lib/accounting-labels";
import { tenantsApi } from "@/services/api/tenants";
import { localPrintLogoSource } from "@/services/pos/pos-local-print-logo";
import { closePrintPreview, openHalfLetterPrintPreview, renderReceiptsHalfLetter, renderReceiptsReceipt } from "@/services/pos/online-pos-client";
import { loadBrowserPrinterConfiguration, PosEdgeClient, readEdgeTokenFromLaunch, readEdgeUserSession } from "@/services/pos/pos-edge-client";
import { resolvePosExecutionMode } from "@/services/pos/pos-launch-session";

export function SalesReturnHistory({ active, businessId, runtime }: {
  active: boolean; businessId: string | null; runtime?: PosSalesReturnRuntime;
}) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [detail, setDetail] = useState<SalesReturnDetail | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const query = { page, pageSize, search: search || undefined, customerId: customerId || undefined, from: from || undefined, to: to || undefined };
  const list = useQuery({
    queryKey: ["sales-returns", businessId, runtime?.client.mode ?? "dashboard", query],
    queryFn: () => runtime ? runtime.client.searchServerSalesReturns(runtime.context, query)
      : salesReturnsApi.listReturns({ ...query, businessId: businessId! }),
    enabled: active && !!businessId,
  });

  async function open(item: SalesReturnListItem, print: boolean) {
    if (busy || !businessId) return;
    setBusy(item.returnId);
    try {
      const value = detail?.returnId === item.returnId ? detail : runtime
        ? await runtime.client.loadServerSalesReturn(runtime.context, item.returnId)
        : await salesReturnsApi.getReturn(item.returnId, businessId);
      if (!print) { setDetail(value); return; }
      if (runtime) { await runtime.client.printHistoricalReceipt(value.receipt); return; }
      const token = readEdgeTokenFromLaunch();
      if (token) {
        const edge = new PosEdgeClient(token, readEdgeUserSession());
        const health = await edge.health();
        const prepared = resolvePosExecutionMode(true, health) === "edge" && health.businessId === businessId;
        let branding = null;
        if (!prepared) {
          try { await tenantsApi.getPrintBranding(); branding = tenantsApi.readyLocalPrintBranding(); }
          catch (error) { console.warn("No se pudo preparar la marca de la devolución.", error); }
        }
        await edge.printHistoricalReceipt({ ...value.receipt,
          companyName: branding?.displayName ?? value.receipt.companyName,
          companyLogoSource: localPrintLogoSource(branding, prepared) });
        return;
      }
      const configuration = loadBrowserPrinterConfiguration();
      const preview = openHalfLetterPrintPreview();
      try {
        try { await tenantsApi.getPrintBranding(); }
        catch (error) { console.warn("No se pudo preparar la marca de la devolución.", error); }
        const context = { businessId, warehouseId: value.warehouseId, workSessionId: null, businessName: value.businessName };
        const format = configuration.posOutputFormat ?? "Receipt";
        if (format === "Receipt")
          await renderReceiptsReceipt(preview, [value.receipt], context, configuration.receiptPaperWidthMillimeters);
        else await renderReceiptsHalfLetter(preview, [value.receipt], context, format);
      } catch (error) { closePrintPreview(preview); throw error; }
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "No fue posible consultar o imprimir la devolución.");
    } finally { setBusy(null); }
  }

  const columns: ColumnDef<SalesReturnListItem>[] = [
    { accessorKey: "documentNumber", header: "Devolución" },
    { accessorKey: "originalDocumentNumber", header: "Documento original" },
    { accessorKey: "customerName", header: "Cliente" },
    { accessorKey: "returnedAt", header: "Fecha de devolución", cell: ({ row }) => formatDateTime(row.original.returnedAt) },
    { accessorKey: "totalAmount", header: "Total devuelto", cell: ({ row }) => formatCurrency(row.original.totalAmount) },
    { accessorKey: "status", header: "Estado", cell: ({ row }) => row.original.status === "Processed" ? "Procesada" : "Aceptada" },
    { accessorKey: "fiscalStatus", header: "Estado fiscal", cell: ({ row }) => fiscalStatusLabel(row.original.fiscalStatus) },
    { id: "actions", header: "Acciones", cell: ({ row }) => <div className="flex gap-1">
      <Button variant="ghost" size="sm" disabled={!!busy} onClick={() => void open(row.original, false)}><Eye className="mr-1 h-4 w-4" />Ver detalle</Button>
      <Button variant="outline" size="sm" disabled={!!busy} onClick={() => void open(row.original, true)}><Printer className="mr-1 h-4 w-4" />{busy === row.original.returnId ? "Preparando…" : "Reimprimir"}</Button>
    </div> },
  ];
  if (!active) return null;
  return <div className="space-y-4">
    <section className="grid gap-3 rounded-2xl border bg-card p-4 md:grid-cols-2 xl:grid-cols-4">
      <div className="space-y-2"><Label>Documento</Label><ServerSearchInput value={search} onSearch={value => { setSearch(value); setPage(1); }} isSearching={list.isFetching} placeholder="Devolución o documento original" /></div>
      <div className="space-y-2"><Label>Cliente</Label><PartyRoleSelect role="Customer" value={customerId} leadingOptions={[{ value: "", label: "Todos los clientes" }]} sourceKey={`returns-${runtime?.client.mode ?? "web"}-${businessId}`} loadPage={runtime ? (term, next, size) => runtime.client.searchServerReturnCustomers(runtime.context, term, next, size) : undefined} onChange={value => { setCustomerId(value); setPage(1); }} /></div>
      <div className="space-y-2"><Label>Devoluciones desde</Label><DatePicker value={from} onChange={value => { setFrom(value); setPage(1); }} /></div>
      <div className="space-y-2"><Label>Hasta</Label><DatePicker value={to} onChange={value => { setTo(value); setPage(1); }} /></div>
    </section>
    {list.isError && <div role="alert" className="rounded-xl border p-4"><p>{list.error.message}</p><Button variant="outline" onClick={() => void list.refetch()}>Reintentar</Button></div>}
    <DataTable columns={columns} data={list.data?.items ?? []} isLoading={list.isLoading} page={page} pageSize={pageSize} pageCount={list.data?.totalPages} totalItems={list.data?.totalCount} enableRowSelection={false} onPaginationChange={(next, size) => { setPage(next); setPageSize(size); }} />
    <Dialog open={!!detail} onOpenChange={open => { if (!open) setDetail(null); }}><DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-3xl">
      <DialogHeader><DialogTitle>Devolución {detail?.documentNumber}</DialogTitle><DialogDescription>Documento original {detail?.originalDocumentNumber}</DialogDescription></DialogHeader>
      {detail && <div className="space-y-4">
        <p>{detail.customerName} · {detail.customerIdentification}<br />{formatDateTime(detail.returnedAt)} · {detail.warehouseName}</p>
        <p><strong>Motivo:</strong> {detail.reasonDescription}</p>
        <p>{detail.economicResolution === "CustomerCredit" ? "Aplicación a cartera / saldo a favor" : "Reembolso"} · Estado fiscal: {fiscalStatusLabel(detail.fiscalStatus)}</p>
        <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr className="border-b text-left"><th className="py-2">Producto / cargo</th><th>Cantidad</th><th className="text-right">Total</th></tr></thead><tbody>{detail.receipt.lines.map((line, index) => <tr key={index} className="border-b"><td className="py-2">{line.description}</td><td>{line.quantity}</td><td className="text-right tabular-nums">{formatCurrency(line.total)}</td></tr>)}</tbody></table></div>
        <p className="text-right">Base {formatCurrency(detail.untaxedAmount)} · Impuestos {formatCurrency(detail.taxAmount)}<br />Ajuste al peso {formatCurrency(detail.roundingAmount)}<br /><strong>Total devuelto {formatCurrency(detail.totalAmount)}</strong></p>
        {detail.notes && <p>{detail.notes}</p>}
        <Button disabled={!!busy} onClick={() => void open(detail, true)}><Printer className="mr-2 h-4 w-4" />Reimprimir</Button>
      </div>}
    </DialogContent></Dialog>
  </div>;
}
