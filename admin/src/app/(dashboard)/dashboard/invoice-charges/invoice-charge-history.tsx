"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { DataTablePagination } from "@/components/tables/data-table-pagination";
import { ServerSearchInput } from "@/components/tables/server-search-input";
import { invoiceChargesApi } from "@/services/api/invoice-charges";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 2 });
const today = () => new Date().toLocaleDateString("en-CA");

export function InvoiceChargeHistory({ businessId }: { businessId: string }) {
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const valid = Boolean(from && to && from <= to &&
    (new Date(to).valueOf() - new Date(from).valueOf()) / 86_400_000 <= 30);
  const query = useQuery({ queryKey: ["invoice-charge-history", businessId, from, to, page, pageSize, search],
    queryFn: () => invoiceChargesApi.history({ from, to, page, pageSize, search }), enabled: valid,
    refetchOnWindowFocus: false });
  return <div className="space-y-4">
    <Card><CardContent className="flex flex-wrap items-end gap-4 p-4">
      <div className="space-y-2"><Label htmlFor="charges-from">Desde</Label><Input id="charges-from" type="date" value={from} onChange={event => { setFrom(event.target.value); setPage(1); }}/></div>
      <div className="space-y-2"><Label htmlFor="charges-to">Hasta</Label><Input id="charges-to" type="date" value={to} onChange={event => { setTo(event.target.value); setPage(1); }}/></div>
      <div className="min-w-64 flex-1"><ServerSearchInput placeholder="Cargo, proveedor o factura" value={search} onSearch={value => { setSearch(value); setPage(1); }}/></div>
      <Button variant="outline" disabled={!valid || query.isFetching} onClick={() => void query.refetch()}>Actualizar</Button>
      {!valid && <p role="alert" className="w-full text-sm text-destructive">Selecciona un periodo de hasta 31 días.</p>}
    </CardContent></Card>
    {query.isError && <p role="alert" className="rounded-xl border border-destructive/30 p-4 text-sm text-destructive">No fue posible consultar los cargos. Usa Actualizar para reintentar.</p>}
    {valid && query.isPending && <p role="status" className="p-6 text-center text-muted-foreground">Consultando cargos…</p>}
    {valid && query.isSuccess && <>
      <div className="grid gap-4 sm:grid-cols-3">{[
        ["Cargos en el periodo", String(query.data.totalCount)],
        ["Incluido en facturas", money.format(query.data.invoicedTotal)],
        ["Registrado como gasto", money.format(query.data.expenseTotal)],
      ].map(([label, value]) => <Card key={label}><CardContent className="p-5"><p className="text-sm text-muted-foreground">{label}</p><strong className="mt-2 block text-2xl tabular-nums">{value}</strong></CardContent></Card>)}</div>
      <Card><CardContent className="p-0"><div className="overflow-x-auto"><table className="w-full text-left text-sm">
        <thead className="bg-muted/50 text-xs uppercase text-muted-foreground"><tr>{["Factura", "Cargo / proveedor", "En factura", "Como gasto", "Cuenta por pagar"].map(label => <th key={label} className="p-4">{label}</th>)}</tr></thead>
        <tbody>{query.data.items.map(item => <tr key={item.charge.appliedChargeId} className="border-t align-top hover:bg-muted/20">
          <td className="p-4 font-medium">{item.documentNumber}<small className="mt-1 block font-normal text-muted-foreground">{new Date(item.issuedAt).toLocaleString("es-CO")}</small></td>
          <td className="p-4"><strong>{item.charge.name}</strong><p className="text-muted-foreground">{item.charge.supplier.name}</p></td>
          <td className="p-4 tabular-nums">{money.format(item.charge.invoicedAmount)}</td>
          <td className="p-4 tabular-nums">{money.format(item.charge.expenseAmount)}</td>
          <td className="p-4"><strong className="tabular-nums">{item.payableBalance === null ? "—" : money.format(item.payableBalance)}</strong><small className="mt-1 block text-muted-foreground">{item.expenseDocumentNumber ?? (item.charge.amount > 0 ? "Pendiente de registro" : "Sin obligación")}</small></td>
        </tr>)}</tbody>
      </table>{query.data.items.length === 0 && <p className="p-10 text-center text-muted-foreground">No hay cargos en este periodo.</p>}</div>
      <DataTablePagination pageIndex={page - 1} pageSize={pageSize} pageCount={Math.ceil(query.data.totalCount / pageSize)} totalItems={query.data.totalCount}
        onPageChange={index => setPage(index + 1)} onPageSizeChange={size => { setPageSize(size); setPage(1); }}/>
      </CardContent></Card>
    </>}
  </div>;
}
