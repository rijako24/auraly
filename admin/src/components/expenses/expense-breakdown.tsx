"use client";

import type { ExpenseLine, ExpenseWithholding } from "@/services/api/expenses";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 4 });
export function ExpenseBreakdown({ lines, withholding, hideWithholdingLines = false }: { lines?: ExpenseLine[] | null; withholding?: ExpenseWithholding | null; hideWithholdingLines?: boolean }) {
  return <div className="space-y-4">
    {!!lines?.length && <div className="overflow-x-auto rounded-xl border"><table className="w-full text-sm">
      <caption className="sr-only">Distribución del gasto</caption>
      <thead className="bg-muted/50 text-left"><tr>{["Cuenta / descripción", "Centro", "Base", "IVA"].map(label => <th key={label} className="p-3">{label}</th>)}</tr></thead>
      <tbody>{lines.map(line => <tr key={line.lineNumber} className="border-t"><td className="p-3"><b>{line.accountCode} · {line.accountName}</b><p>{line.description}</p></td><td className="p-3">{line.costCenterName ?? "Predeterminado"}</td><td className="whitespace-nowrap p-3">{money.format(line.taxExclusiveAmount)}</td><td className="p-3"><span className="whitespace-nowrap">{money.format(line.vatAmount)}</span><small className="block text-muted-foreground">{line.vatAmount > 0 ? line.taxTreatment === "DeductibleInputVat" ? "Descontable" : "Mayor valor del gasto" : "Sin IVA"}</small></td></tr>)}</tbody>
    </table></div>}
    {withholding && <section className="space-y-3" aria-label="Retenciones y total">
      <h3 className="font-semibold">{hideWithholdingLines ? "Totales del gasto" : "Retenciones"}</h3>
      {!hideWithholdingLines && (withholding.lines.length > 0 ? <div className="overflow-x-auto rounded-xl border"><table className="w-full text-sm"><thead className="bg-muted/50 text-left"><tr>{["Regla", "Base", "Tarifa", "Retención"].map(label => <th key={label} className="p-3">{label}</th>)}</tr></thead><tbody>{withholding.lines.map(line => <tr key={line.ruleId} className="border-t"><td className="p-3">{line.name}<small className="block text-muted-foreground">{line.ruleCode} · v{line.ruleVersion}{line.jurisdictionCode ? ` · ${line.jurisdictionCode}` : ""}</small></td><td className="whitespace-nowrap p-3">{money.format(line.taxableBase)}</td><td className="p-3">{line.rate}%</td><td className="whitespace-nowrap p-3 font-medium">{money.format(line.amount)}</td></tr>)}</tbody></table></div> : <p className="text-sm text-muted-foreground">El cálculo no genera retenciones. Revisa el diagnóstico antes de confirmar.</p>)}
      <dl className="grid gap-3 rounded-xl border border-primary/20 bg-primary/5 p-4 sm:grid-cols-3">
        <div><dt className="text-sm text-muted-foreground">Total del documento</dt><dd className="font-semibold">{money.format(withholding.grossAmount)}</dd></div>
        <div><dt className="text-sm text-muted-foreground">Retenciones</dt><dd className="font-semibold">{money.format(withholding.withholdingTotal)}</dd></div>
        <div><dt className="text-sm text-muted-foreground">Neto por pagar</dt><dd className="text-xl font-bold text-primary">{money.format(withholding.netAmount)}</dd></div>
      </dl>
    </section>}
  </div>;
}
