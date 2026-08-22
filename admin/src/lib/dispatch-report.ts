import type { DispatchReportRow } from "@/services/api/dispatches";
import type { ReportColumn, ReportRow } from "@/lib/report-viewer";

export type DispatchReportGroup = "detail" | "product" | "customer" | "seller";

const money = new Intl.NumberFormat("es-CO", { style: "currency", currency: "COP", maximumFractionDigits: 0 });
const quantity = new Intl.NumberFormat("es-CO", { maximumFractionDigits: 3 });

export function dispatchReportColumns(includePrices: boolean, group: DispatchReportGroup = "detail"): ReportColumn[] {
  const dimensions: ReportColumn[] = group === "detail" || group === "product"
    ? [{ key: "productCode", label: "Código" }, { key: "productName", label: "Producto" }]
    : group === "customer" ? [{ key: "customerName", label: "Cliente" }]
    : [{ key: "sellerName", label: "Vendedor" }];
  return [
    ...dimensions,
    { key: "assignedQuantity", label: "Asignado", align: "right", format: (value) => quantity.format(Number(value ?? 0)) },
    { key: "verifiedQuantity", label: "Verificado", align: "right", format: (value) => quantity.format(Number(value ?? 0)) },
    { key: "shortageQuantity", label: "Faltante", align: "right", format: (value) => quantity.format(Number(value ?? 0)) },
    ...(includePrices ? [{ key: "lineTotal", label: "Total", align: "right" as const, format: (value: unknown) => money.format(Number(value ?? 0)) }] : []),
  ];
}

export function buildDispatchReportRows(rows: DispatchReportRow[], group: DispatchReportGroup): ReportRow[] {
  if (group === "detail") {
    const result: ReportRow[] = [];
    for (const [documentNumber, documentRows] of groupBy(rows, row => row.documentNumber)) {
      const first = documentRows[0];
      result.push({ id: `invoice-${documentNumber}`, __group: `Factura ${documentNumber} · ${first.customerName} · Vendedor ${first.sellerName}` });
      result.push(...documentRows.map((row, index) => ({ id: `${documentNumber}-${index}`, productCode: row.productCode, productName: row.productName, assignedQuantity: row.assignedQuantity, verifiedQuantity: row.verifiedQuantity, shortageQuantity: row.shortageQuantity, lineTotal: row.lineTotal })));
    }
    return result;
  }
  const grouped = new Map<string, ReportRow>();
  for (const row of rows) {
    const key = group === "product" ? row.productCode : group === "customer" ? row.customerName : row.sellerName;
    const current = grouped.get(key);
    if (current) {
      current.assignedQuantity = Number(current.assignedQuantity) + row.assignedQuantity;
      current.verifiedQuantity = Number(current.verifiedQuantity) + row.verifiedQuantity;
      current.shortageQuantity = Number(current.shortageQuantity) + row.shortageQuantity;
      current.lineTotal = Number(current.lineTotal ?? 0) + Number(row.lineTotal ?? 0);
    } else grouped.set(key, { id: key, ...(group === "product" ? { productCode: row.productCode, productName: row.productName } : group === "customer" ? { customerName: row.customerName } : { sellerName: row.sellerName }), assignedQuantity: row.assignedQuantity, verifiedQuantity: row.verifiedQuantity, shortageQuantity: row.shortageQuantity, lineTotal: row.lineTotal });
  }
  return [...grouped.values()];
}

function groupBy<T>(values:T[],key:(value:T)=>string){const result=new Map<string,T[]>();for(const value of values){const id=key(value);result.set(id,[...(result.get(id)??[]),value])}return result}
