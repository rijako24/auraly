import type { DispatchReportRow } from "@/services/api/dispatches";
import type { ReportColumn, ReportRow } from "@/lib/report-viewer";

export type DispatchReportGroup = "detail" | "product" | "customer" | "seller";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const quantity = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 3 });

export function dispatchReportColumns(includePrices: boolean): ReportColumn[] {
  return [
    { key: "documentNumber", label: "Documento" },
    { key: "customerName", label: "Cliente" },
    { key: "sellerName", label: "Vendedor" },
    { key: "productCode", label: "Producto" },
    { key: "productName", label: "Nombre" },
    { key: "assignedQuantity", label: "Asignado", align: "right", format: (value) => quantity.format(Number(value ?? 0)) },
    { key: "verifiedQuantity", label: "Verificado", align: "right", format: (value) => quantity.format(Number(value ?? 0)) },
    { key: "shortageQuantity", label: "Faltante", align: "right", format: (value) => quantity.format(Number(value ?? 0)) },
    ...(includePrices ? [{ key: "lineTotal", label: "Total", align: "right" as const, format: (value: unknown) => money.format(Number(value ?? 0)) }] : []),
  ];
}

export function buildDispatchReportRows(rows: DispatchReportRow[], group: DispatchReportGroup): ReportRow[] {
  if (group === "detail") return rows.map((row, index) => ({ id: index, ...row }));
  const grouped = new Map<string, ReportRow>();
  for (const row of rows) {
    const key = group === "product" ? row.productCode : group === "customer" ? row.customerName : row.sellerName;
    const current = grouped.get(key);
    if (current) {
      current.assignedQuantity = Number(current.assignedQuantity) + row.assignedQuantity;
      current.verifiedQuantity = Number(current.verifiedQuantity) + row.verifiedQuantity;
      current.shortageQuantity = Number(current.shortageQuantity) + row.shortageQuantity;
      current.lineTotal = Number(current.lineTotal ?? 0) + Number(row.lineTotal ?? 0);
      current.documentNumber = "Varios";
      if (group !== "customer") current.customerName = "Varios";
      if (group !== "seller") current.sellerName = "Varios";
      if (group !== "product") { current.productCode = "Varios"; current.productName = "Varios"; }
    } else grouped.set(key, { id: key, ...row });
  }
  return [...grouped.values()];
}
