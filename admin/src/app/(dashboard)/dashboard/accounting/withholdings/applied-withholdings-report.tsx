"use client";

import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { taxationApi } from "@/services/api/taxation";
import { useBusinessContextStore } from "@/stores/business-context-store";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const kindLabels: Record<string, string> = {
  IncomeTax: "Retención en la fuente", Vat: "ReteIVA", IndustryCommerce: "ReteICA",
};
const sourceLabels: Record<string, string> = {
  Expense: "Gasto", GoodsReceipt: "Recepción de compra",
  GoodsReceiptCostDocument: "Factura adicional de compra",
};

export function AppliedWithholdingsReport() {
  const businessId = useBusinessContextStore(state => state.selectedBusinessId);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);
  useEffect(() => {
    const today = new Date();
    const year = today.getFullYear();
    const month = String(today.getMonth() + 1).padStart(2, "0");
    setFrom(`${year}-${month}-01`);
    setTo(`${year}-${month}-${String(new Date(year, today.getMonth() + 1, 0).getDate()).padStart(2, "0")}`);
  }, []);
  const validRange = Boolean(from && to && from <= to &&
    (Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) <= 365 * 86400000);
  const report = useQuery({
    queryKey: ["applied-withholdings", businessId, from, to, page],
    queryFn: () => taxationApi.listApplied(from, to, page),
    enabled: Boolean(businessId && validRange),
  });
  const data = report.data;
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return <Card className="overflow-hidden rounded-3xl">
    <CardHeader className="border-b bg-muted/30">
      <CardTitle>Retenciones practicadas</CardTitle>
      <p className="text-sm text-muted-foreground">Consolidado de gastos y compras causados. Renta y ReteIVA son soporte para revisar la declaración mensual ante la DIAN; ReteICA corresponde al municipio. Este informe no presenta ni paga declaraciones.</p>
      <div className="grid gap-3 pt-3 sm:grid-cols-2 lg:max-w-2xl">
        <label className="space-y-1 text-sm font-medium">Desde<DatePicker value={from} onChange={value => { setFrom(value); setPage(1); }} max={to || undefined}/></label>
        <label className="space-y-1 text-sm font-medium">Hasta<DatePicker value={to} onChange={value => { setTo(value); setPage(1); }} min={from || undefined}/></label>
      </div>
      {!validRange && from && to && <p className="text-sm text-destructive">Selecciona un período válido de máximo un año.</p>}
    </CardHeader>
    <CardContent className="p-0">
      {report.isError && <p className="p-5 text-sm text-destructive">No fue posible consultar las retenciones practicadas.</p>}
      {report.isLoading && validRange && <p className="p-5 text-sm text-muted-foreground">Cargando consolidado…</p>}
      {data && <>
        <div className="grid gap-3 p-5 sm:grid-cols-3">
          <Metric label="Retención en la fuente" value={data.incomeTaxTotal}/>
          <Metric label="ReteIVA" value={data.vatTotal}/>
          <Metric label="ReteICA · municipal" value={data.industryCommerceTotal}/>
        </div>
        <div className="overflow-x-auto border-t">
          <table className="w-full min-w-[900px] text-sm">
            <thead className="bg-muted/50 text-xs uppercase text-muted-foreground"><tr>
              <th className="p-3 text-left">Fecha</th><th className="p-3 text-left">Documento</th>
              <th className="p-3 text-left">Proveedor</th><th className="p-3 text-left">Retención</th>
              <th className="p-3 text-right">Base</th><th className="p-3 text-right">Tarifa</th>
              <th className="p-3 text-right">Valor</th>
            </tr></thead>
            <tbody>{data.items.map(item => <tr key={`${item.documentType}-${item.documentId}-${item.lineNumber}`} className="border-t align-top">
              <td className="p-3 whitespace-nowrap">{new Date(item.recognizedAt).toLocaleDateString("es-CO", { timeZone: "America/Bogota" })}</td>
              <td className="p-3 font-medium">{item.documentNumber}<small className="block font-normal text-muted-foreground">{sourceLabels[item.documentType] ?? item.documentType}</small></td>
              <td className="p-3">{item.supplierName}<small className="block text-muted-foreground">{item.supplierIdentification}</small></td>
              <td className="p-3">{item.name}<small className="block text-muted-foreground">{kindLabels[item.kind] ?? item.kind}{item.jurisdictionCode ? ` · ${item.jurisdictionCode}` : ""} · {item.isManual ? "Puntual" : item.ruleCode}</small></td>
              <td className="p-3 text-right tabular-nums">{money.format(item.taxableBase)}</td>
              <td className="p-3 text-right tabular-nums">{item.rate}%</td>
              <td className="p-3 text-right font-semibold tabular-nums">{money.format(item.amount)}</td>
            </tr>)}</tbody>
          </table>
          {data.totalCount === 0 && <p className="p-8 text-center text-sm text-muted-foreground">No hay retenciones practicadas en este período.</p>}
        </div>
        <div className="flex items-center justify-between gap-3 border-t p-4 text-sm">
          <span className="text-muted-foreground">{data.totalCount} registros · Página {page} de {totalPages}</span>
          <div className="flex gap-2"><Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Anterior</Button><Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage(value => value + 1)}>Siguiente</Button></div>
        </div>
      </>}
    </CardContent>
  </Card>;
}

function Metric({ label, value }: { label: string; value: number }) {
  return <div className="rounded-2xl border bg-background p-4"><p className="text-xs text-muted-foreground">{label}</p><p className="mt-1 text-xl font-semibold tabular-nums">{money.format(value)}</p></div>;
}
