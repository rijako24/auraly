"use client";

import type { ExpenseLine, ExpenseWithholding } from "@/services/api/expenses";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });
export function ExpenseBreakdown({ lines, withholding, hideWithholdingLines = false }: { lines?: ExpenseLine[] | null; withholding?: ExpenseWithholding | null; hideWithholdingLines?: boolean }) {
  return <div className="space-y-4">
    {!!lines?.length && <div className="overflow-x-auto rounded-xl border"><table className="block w-full text-sm sm:table">
      <caption className="sr-only">Distribución del gasto</caption>
      <thead className="hidden bg-muted/50 text-left sm:table-header-group"><tr>{["Cuenta / descripción", "Centro", "Base", "IVA"].map(label => <th key={label} className="p-3">{label}</th>)}</tr></thead>
      <tbody className="block sm:table-row-group">{lines.map(line => <tr key={line.lineNumber} className="grid min-w-0 grid-cols-2 gap-2 border-t p-3 sm:table-row sm:p-0"><td className="col-span-2 min-w-0 break-words sm:p-3"><span className="block text-xs text-muted-foreground sm:hidden">Cuenta / descripción</span><b>{line.accountCode} · {line.accountName}</b><p>{line.description}</p></td><td className="col-span-2 min-w-0 sm:p-3"><span className="block text-xs text-muted-foreground sm:hidden">Centro</span>{line.costCenterName ?? "Predeterminado"}</td><td className="min-w-0 sm:whitespace-nowrap sm:p-3"><span className="block text-xs text-muted-foreground sm:hidden">Base</span>{money.format(line.taxExclusiveAmount)}</td><td className="min-w-0 sm:p-3"><span className="block text-xs text-muted-foreground sm:hidden">IVA</span><span className="sm:whitespace-nowrap">{money.format(line.vatAmount)}</span><small className="block text-muted-foreground">{line.vatAmount > 0 ? line.taxTreatment === "DeductibleInputVat" ? "Descontable" : "Mayor valor del gasto" : "Sin IVA"}</small></td></tr>)}</tbody>
    </table></div>}
    {withholding && <section className="space-y-3" aria-label="Retenciones y total">
      <h3 className="font-semibold">{hideWithholdingLines ? "Totales del gasto" : "Retenciones"}</h3>
      {!hideWithholdingLines && (withholding.lines.length > 0 ? <div className="overflow-x-auto rounded-xl border"><table className="block w-full text-sm sm:table"><thead className="hidden bg-muted/50 text-left sm:table-header-group"><tr>{["Regla", "Base", "Tarifa", "Retención"].map(label => <th key={label} className="p-3">{label}</th>)}</tr></thead><tbody className="block sm:table-row-group">{withholding.lines.map(line => <tr key={line.ruleId} className="grid min-w-0 grid-cols-2 gap-2 border-t p-3 sm:table-row sm:p-0"><td className="col-span-2 min-w-0 break-words sm:p-3"><span className="block text-xs text-muted-foreground sm:hidden">Regla</span>{line.name}<small className="block text-muted-foreground">{line.ruleCode} · v{line.ruleVersion}{line.jurisdictionCode ? ` · ${line.jurisdictionCode}` : ""}</small></td><td className="min-w-0 sm:whitespace-nowrap sm:p-3"><span className="block text-xs text-muted-foreground sm:hidden">Base</span>{money.format(line.taxableBase)}</td><td className="min-w-0 sm:p-3"><span className="block text-xs text-muted-foreground sm:hidden">Tarifa</span>{line.rate}%</td><td className="col-span-2 min-w-0 font-medium sm:whitespace-nowrap sm:p-3"><span className="block text-xs font-normal text-muted-foreground sm:hidden">Retención</span>{money.format(line.amount)}</td></tr>)}</tbody></table></div> : <p className="text-sm text-muted-foreground">El cálculo no genera retenciones. Revisa el diagnóstico antes de confirmar.</p>)}
      <dl className="grid gap-3 rounded-xl border border-primary/20 bg-primary/5 p-4 sm:grid-cols-3">
        <div><dt className="text-sm text-muted-foreground">Total del documento</dt><dd className="font-semibold">{money.format(withholding.grossAmount)}</dd></div>
        <div><dt className="text-sm text-muted-foreground">Retenciones</dt><dd className="font-semibold">{money.format(withholding.withholdingTotal)}</dd></div>
        <div><dt className="text-sm text-muted-foreground">Neto por pagar</dt><dd className="text-xl font-bold text-primary">{money.format(withholding.netAmount)}</dd></div>
      </dl>
    </section>}
  </div>;
}
