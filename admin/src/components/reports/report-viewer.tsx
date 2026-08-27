"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Download, Printer, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { TenantBrand } from "@/components/brand/tenant-brand";
import { filterReportRows, reportCellText, safeReportFileName, toReportCsv, type ReportColumn, type ReportRow } from "@/lib/report-viewer";
import { tenantsApi } from "@/services/api/tenants";
import { useTenantContextStore } from "@/stores/tenant-context-store";

type Props = {
  onClose: () => void;
  title: string;
  description?: string;
  rows: ReportRow[];
  columns: ReportColumn[];
  fileName?: string;
};

export function ReportViewer({ onClose, title, description, rows, columns, fileName }: Props) {
  const [search, setSearch] = useState("");
  const selectedTenantId = useTenantContextStore(state => state.selectedTenantId);
  const selectedTenantName = useTenantContextStore(state => state.tenants.find(item => item.tenantId === state.selectedTenantId)?.name ?? "Organización");
  const branding = useQuery({
    queryKey: ["tenant-branding", selectedTenantId],
    queryFn: tenantsApi.getBranding,
    enabled: Boolean(selectedTenantId),
    staleTime: 10 * 60 * 1000,
  });
  const brandName = branding.data?.legalName ?? branding.data?.displayName ?? selectedTenantName;
  const generatedAt = useMemo(() => new Date(), []);
  const filtered = useMemo(() => filterReportRows(rows, columns, search), [columns, rows, search]);
  const recordCount = useMemo(() => filtered.filter(row => !row.__group).length, [filtered]);

  function exportCsv() {
    const url = URL.createObjectURL(new Blob([toReportCsv(filtered, columns)], { type: "text/csv;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `${safeReportFileName(fileName ?? title)}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return <div className="flex min-h-[calc(100dvh-9rem)] flex-col overflow-hidden rounded-2xl border bg-background">
        <header className="flex flex-wrap items-center gap-4 border-b px-5 py-4"><Button type="button" size="sm" variant="ghost" onClick={onClose}><ArrowLeft className="mr-2 h-4 w-4"/>Volver</Button><TenantBrand displayName={brandName} logoUrl={branding.data?.logoUrl}/><div><h1 className="text-lg font-semibold">{title}</h1><p className="text-sm text-muted-foreground">{description ?? `${recordCount.toLocaleString("es-CO")} registros`}</p></div></header>
        <div className="flex flex-col gap-3 border-b bg-muted/30 p-4 sm:flex-row sm:items-center">
          <label className="relative min-w-0 flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input className="pl-9" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar dentro del reporte" />
          </label>
          <span className="text-sm tabular-nums text-muted-foreground">{recordCount.toLocaleString("es-CO")} filas</span>
          <Button variant="outline" onClick={() => window.print()}><Printer className="mr-2 h-4 w-4" />Imprimir / PDF</Button>
          <Button variant="outline" onClick={exportCsv}><Download className="mr-2 h-4 w-4" />Exportar CSV</Button>
        </div>
        <section id="auraly-report-print-area" className="min-h-0 flex-1 overflow-auto bg-white p-5 text-slate-950">
          <header className="mb-6 border-b-2 border-teal-700 pb-4">
            <div className="flex flex-wrap items-start justify-between gap-4"><div><TenantBrand className="mb-4" displayName={brandName} logoUrl={branding.data?.logoUrl}/><p className="text-[10px] font-bold uppercase tracking-[.24em] text-teal-700">Reporte corporativo</p><h1 className="mt-1 text-2xl font-bold tracking-tight">{title}</h1>{description && <p className="mt-1 text-sm text-slate-600">{description}</p>}</div><dl className="grid min-w-56 gap-1 rounded-lg border bg-slate-50 p-3 text-xs"><div className="flex justify-between gap-4"><dt className="text-slate-500">Generado</dt><dd className="font-medium">{generatedAt.toLocaleString("es-CO")}</dd></div><div className="flex justify-between gap-4"><dt className="text-slate-500">Registros</dt><dd className="font-medium">{recordCount.toLocaleString("es-CO")}</dd></div><div className="flex justify-between gap-4"><dt className="text-slate-500">Sistema</dt><dd className="font-medium">Auraly</dd></div></dl></div>
          </header>
          <table className="w-full min-w-max border-collapse text-xs sm:text-sm">
            <thead className="sticky top-0 bg-slate-100 print:static">
              <tr>{columns.map((column) => <th key={column.key} className={`border px-3 py-2 ${column.align === "right" ? "text-right" : "text-left"}`}>{column.label}</th>)}</tr>
            </thead>
            <tbody>{filtered.map((row, index) => row.__group ? <tr key={String(row.id ?? index)}><td colSpan={columns.length} className="border border-teal-200 bg-teal-50 px-3 py-2 font-bold text-teal-950">{String(row.__group)}</td></tr> : <tr key={String(row.id ?? index)} className="even:bg-slate-50">
              {columns.map((column) => <td key={column.key} className={`border px-3 py-2 ${column.align === "right" ? "text-right tabular-nums" : "text-left"}`}>{reportCellText(column, row)}</td>)}
            </tr>)}</tbody>
          </table>
          {!filtered.length && <p className="py-16 text-center text-sm text-slate-500">No hay filas que coincidan con el filtro.</p>}
          <footer className="mt-6 flex items-center justify-between gap-4 border-t pt-3 text-[10px] text-slate-500"><span>{brandName} · Información generada desde Auraly</span><span>{title} · {recordCount.toLocaleString("es-CO")} registros</span></footer>
        </section>
        <footer className="flex justify-end border-t px-5 py-3"><Button variant="outline" onClick={onClose}>Cerrar</Button></footer>
        <style jsx global>{`
          @media print {
            body * { visibility: hidden !important; }
            #auraly-report-print-area, #auraly-report-print-area * { visibility: visible !important; }
            #auraly-report-print-area { position: fixed; inset: 0; overflow: visible !important; padding: 0 !important; }
            #auraly-report-print-area table { font-size: 9pt; }
            #auraly-report-print-area thead { display: table-header-group; }
            #auraly-report-print-area tr { break-inside: avoid; }
            @page { size: landscape; margin: 10mm; }
          }
        `}</style>
  </div>;
}
