"use client";

import { useMemo, useState } from "react";
import { Download, Printer, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { filterReportRows, reportCellText, safeReportFileName, toReportCsv, type ReportColumn, type ReportRow } from "@/lib/report-viewer";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  rows: ReportRow[];
  columns: ReportColumn[];
  fileName?: string;
};

export function ReportViewer({ open, onOpenChange, title, description, rows, columns, fileName }: Props) {
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => filterReportRows(rows, columns, search), [columns, rows, search]);

  function exportCsv() {
    const url = URL.createObjectURL(new Blob([toReportCsv(filtered, columns)], { type: "text/csv;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = `${safeReportFileName(fileName ?? title)}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex h-[94vh] max-w-[96vw] flex-col overflow-hidden p-0 xl:max-w-7xl">
        <DialogHeader className="border-b px-5 py-4">
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description ?? `${filtered.length.toLocaleString("es-CO")} registros`}</DialogDescription>
        </DialogHeader>
        <div className="flex flex-col gap-3 border-b bg-muted/30 p-4 sm:flex-row sm:items-center">
          <label className="relative min-w-0 flex-1">
            <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input className="pl-9" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar dentro del reporte" />
          </label>
          <span className="text-sm tabular-nums text-muted-foreground">{filtered.length.toLocaleString("es-CO")} filas</span>
          <Button variant="outline" onClick={() => window.print()}><Printer className="mr-2 h-4 w-4" />Imprimir / PDF</Button>
          <Button variant="outline" onClick={exportCsv}><Download className="mr-2 h-4 w-4" />Exportar CSV</Button>
        </div>
        <section id="auraly-report-print-area" className="min-h-0 flex-1 overflow-auto bg-white p-5 text-slate-950">
          <header className="mb-5 hidden print:block">
            <h1 className="text-xl font-bold">{title}</h1>
            {description && <p className="text-sm">{description}</p>}
            <p className="text-xs">Generado: {new Date().toLocaleString("es-CO")}</p>
          </header>
          <table className="w-full min-w-max border-collapse text-xs sm:text-sm">
            <thead className="sticky top-0 bg-slate-100 print:static">
              <tr>{columns.map((column) => <th key={column.key} className={`border px-3 py-2 ${column.align === "right" ? "text-right" : "text-left"}`}>{column.label}</th>)}</tr>
            </thead>
            <tbody>{filtered.map((row, index) => <tr key={String(row.id ?? index)} className="even:bg-slate-50">
              {columns.map((column) => <td key={column.key} className={`border px-3 py-2 ${column.align === "right" ? "text-right tabular-nums" : "text-left"}`}>{reportCellText(column, row)}</td>)}
            </tr>)}</tbody>
          </table>
          {!filtered.length && <p className="py-16 text-center text-sm text-slate-500">No hay filas que coincidan con el filtro.</p>}
        </section>
        <DialogFooter className="border-t px-5 py-3"><Button variant="outline" onClick={() => onOpenChange(false)}>Cerrar</Button></DialogFooter>
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
      </DialogContent>
    </Dialog>
  );
}
