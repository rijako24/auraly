export type ReportCell = string | number | null | undefined;
export type ReportRow = Record<string, ReportCell>;

export interface ReportColumn {
  key: string;
  label: string;
  align?: "left" | "right";
  format?: (value: ReportCell, row: ReportRow) => string;
}

export function reportCellText(column: ReportColumn, row: ReportRow): string {
  const value = row[column.key];
  if (column.format) return column.format(value, row);
  return value == null ? "" : String(value);
}

export function filterReportRows(rows: ReportRow[], columns: ReportColumn[], search: string): ReportRow[] {
  const term = search.trim().toLocaleLowerCase("es");
  if (!term) return rows;
  return rows.filter((row) => columns.some((column) =>
    reportCellText(column, row).toLocaleLowerCase("es").includes(term)));
}

export function toReportCsv(rows: ReportRow[], columns: ReportColumn[]): string {
  const quote = (value: string) => `"${value.replaceAll('"', '""')}"`;
  return "\uFEFF" + [
    columns.map((column) => quote(column.label)).join(","),
    ...rows.map((row) => columns.map((column) => quote(reportCellText(column, row))).join(",")),
  ].join("\r\n");
}

export function safeReportFileName(value: string): string {
  const normalized = value.trim().replace(/[\\/:*?"<>|\s]+/g, "-").replace(/-+/g, "-").replace(/^-|-$/g, "");
  return normalized || "reporte";
}
