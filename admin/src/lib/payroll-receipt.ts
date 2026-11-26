import { commercialReportMoney, type ReportColumn, type ReportRow } from "./report-viewer";

export const payrollReceiptColumns: ReportColumn[] = [
  { key: "concept", label: "Concepto" },
  { key: "quantity", label: "Cantidad", align: "right", format: value => value == null ? "" : Number(value).toLocaleString("es-CO") },
  ...["earnings", "deductions"].map(key => ({ key, label: key === "earnings" ? "Devengado" : "Deducción", align: "right" as const,
    format: (value: string | number | null | undefined) => value == null ? "" : commercialReportMoney.format(Number(value)) })),
];

// Presentation only: amounts, identity and totals come from the same frozen
// report dataset. No current employment/catalog lookup or new calculation.
export function payrollReceiptRows(source: ReportRow[], signature: boolean): ReportRow[] {
  const groups = new Map<string, ReportRow[]>();
  for (const row of source) {
    const id = row.payrollRunEmployeeId;
    if (typeof id !== "string" || !id) throw new Error("El desprendible no identifica el resultado del trabajador.");
    const rows = groups.get(id) ?? [];
    rows.push(row); groups.set(id, rows);
  }
  const result: ReportRow[] = [];
  for (const [id, lines] of groups) {
    const worker = lines[0];
    const header = (suffix: string, text: string, level = 2) => result.push({ id: `${id}-${suffix}`, __group: text, __groupLevel: level });
    header("worker", `${worker.employeeName} · ${worker.identification ?? "Sin identificación"}`, 1);
    header("contract", `Contrato: ${worker.contractNumber ?? "—"} · Período: ${worker.periodStart} a ${worker.periodEnd} · ${worker.paidDate?`Pago: ${worker.paidDate}`:`Pago previsto: ${worker.paymentDate}`}`);
    header("salary", `Salario mensual: ${worker.monthlySalary == null ? "—" : commercialReportMoney.format(Number(worker.monthlySalary))} · Días: ${worker.workedDays} · Cargo: ${worker.jobTitle ?? "—"}`);
    header("profile", `Perfil: ${worker.profileName ?? "—"} · Centro: ${worker.costCenterName ?? "—"}`);
    header("affiliations", `Salud: ${worker.healthName ?? "—"} · Pensión: ${worker.pensionName ?? "—"}`);
    for (const line of lines) {
      if (line.nature !== "Earning" && line.nature !== "Deduction") continue;
      result.push({ id: line.id, concept: `${line.conceptCode} · ${line.conceptName}${line.lineCostCenterName?` · ${line.lineCostCenterName}`:""}${line.notes?` · ${line.notes}`:""}`, quantity: line.quantity,
        earnings: line.nature === "Earning" ? line.amount : null, deductions: line.nature === "Deduction" ? line.amount : null });
    }
    result.push({ id: `${id}-total`, concept: "Totales", earnings: worker.earnings, deductions: worker.deductions });
    result.push({ id: `${id}-net`, concept: worker.paidDate?"Neto pagado":"Neto a pagar", earnings: worker.netPayable });
    if (signature) result.push({ id: `${id}-signature`, __group: "Firma del trabajador: _____________________________________", __groupLevel: 3 });
  }
  return result;
}
