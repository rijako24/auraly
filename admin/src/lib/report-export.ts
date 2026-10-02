import { reportCellText, safeReportFileName, type ReportColumn, type ReportRow } from "./report-viewer";

export type ReportExport = {
  title: string;
  description?: string;
  brandName: string;
  fileName: string;
  rows: ReportRow[];
  columns: ReportColumn[];
};

export async function exportReportPdf(report: ReportExport) {
  const [{ jsPDF }, { autoTable }] = await Promise.all([import("jspdf"), import("jspdf-autotable")]);
  const pdf = new jsPDF({ orientation: "landscape", unit: "mm", format: "a4" });
  pdf.setFontSize(10);
  pdf.text(report.brandName, 12, 12);
  pdf.setFontSize(16);
  pdf.text(report.title, 12, 21);
  pdf.setFontSize(9);
  const description = pdf.splitTextToSize(report.description ?? "", 270);
  pdf.text(description, 12, 28);
  autoTable(pdf, {
    startY: 32 + description.length * 4,
    margin: { left: 12, right: 12, bottom: 15 },
    head: [report.columns.map(column => column.label)],
    body: report.rows.map(row => row.__group
      ? [{ content: String(row.__group), colSpan: report.columns.length, styles: { fillColor: [220, 245, 240] } }]
      : report.columns.map(column => reportCellText(column, row))),
    styles: { fontSize: report.columns.length > 10 ? 6 : 8, overflow: "linebreak" },
    headStyles: { fillColor: [15, 118, 110] },
    didDrawPage: () => {
      pdf.setFontSize(8);
      pdf.text(`${report.brandName} · Auraly · ${pdf.getNumberOfPages()}`, 12, 202);
    },
  });
  pdf.save(`${safeReportFileName(report.fileName)}.pdf`);
}

export async function exportReportExcel(report: ReportExport) {
  const { default: writeExcelFile } = await import("write-excel-file/browser");
  const rows = [
    [report.brandName], [report.title], [report.description ?? ""],
    report.columns.map(column => ({ value: column.label, fontWeight: "bold" as const, backgroundColor: "#0F766E", textColor: "#FFFFFF" })),
    ...report.rows.map(row => row.__group ? [String(row.__group)] : report.columns.map(column => {
      const value = row[column.key];
      // Strings stay strings, including identifiers and formula-looking user input.
      return typeof value === "number" ? { value, format: "#,##0.00" } : reportCellText(column, row);
    })),
  ];
  await writeExcelFile(rows, { sheet: "Reporte", stickyRowsCount: 4,
    columns: report.columns.map(column => ({ width: column.align === "right" ? 18 : 28 }))
  }).toFile(`${safeReportFileName(report.fileName)}.xlsx`);
}
