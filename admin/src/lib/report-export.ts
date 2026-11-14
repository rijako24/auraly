import { reportCellText, safeReportFileName, type ReportColumn, type ReportRow } from "./report-viewer";
import type { SheetData } from "write-excel-file/browser";

export type ReportExport = {
  title: string;
  description?: string;
  brandName: string;
  fileName: string;
  rows: ReportRow[];
  columns: ReportColumn[];
  logoSource?: string | null;
  keepRowsTogether?: boolean;
  groupedHeaders?: boolean;
};

export async function exportReportPdf(report: ReportExport) {
  const [{ jsPDF }, { autoTable }] = await Promise.all([import("jspdf"), import("jspdf-autotable")]);
  const pdf = new jsPDF({ orientation: "landscape", unit: "mm", format: "a4" });
  const headerX = report.logoSource ? 90 : 12;
  let brandBottom = 14;
  if (report.logoSource) {
    const image = pdf.getImageProperties(report.logoSource);
    const scale = Math.min(28 / image.width, 16 / image.height);
    pdf.addImage(report.logoSource, image.fileType, 12, 8, image.width * scale, image.height * scale);
    pdf.setFontSize(10);
    const brandLines = pdf.splitTextToSize(report.brandName, 70);
    pdf.text(brandLines, 12, 29);
    brandBottom = 29 + brandLines.length * 4;
  }
  pdf.setFontSize(10);
  if (!report.logoSource) pdf.text(report.brandName, 12, 12);
  pdf.setFontSize(16);
  pdf.text(report.title, headerX, 21);
  pdf.setFontSize(9);
  const description = pdf.splitTextToSize(report.description ?? "", report.logoSource ? 195 : 270);
  pdf.text(description, headerX, 28);
  pdf.setFontSize(8);
  const footerBrand = pdf.splitTextToSize(report.brandName, 250);
  const footerExtraHeight = Math.max(0, footerBrand.length - 1) * 3.5;
  const sections: Array<{heading?:ReportRow;metadata:ReportRow[];rows:ReportRow[]}> = [];
  for (const row of report.rows) {
    if (report.groupedHeaders && row.__groupLevel === 1) sections.push({heading:row,metadata:[],rows:[]});
    else {
      if (!sections.length) sections.push({metadata:[],rows:[]});
      const section=sections[sections.length-1];
      if (report.groupedHeaders && row.__groupLevel === 2) section.metadata.push(row);
      else section.rows.push(row);
    }
  }
  let nextY = Math.max(32 + description.length * 4, brandBottom + 4);
  for (const section of sections) {
    autoTable(pdf, {
    startY: nextY,
    margin: { left: 12, right: 12, bottom: 15 + footerExtraHeight },
    head: [...(section.heading ? [[{content:String(section.heading.__group),colSpan:report.columns.length,
      styles:{fontSize:10,fontStyle:"bold" as const}}],
      ...section.metadata.map(row=>[{content:String(row.__group),colSpan:report.columns.length,
        styles:{fontSize:7,fontStyle:"normal" as const}}])] : []),report.columns.map(column => column.label)],
    body: section.rows.map(row => row.__group
      ? [{ content: String(row.__group), colSpan: report.columns.length, styles: row.__groupLevel === 1
          ? { fillColor: [15, 118, 110], textColor: [255, 255, 255], fontStyle: "bold", fontSize: 10 }
          : { fillColor: [220, 245, 240] } }]
      : report.columns.map(column => {
          const color=column.cellStyle?.(row);
          return color ? {content:reportCellText(column,row),styles:{fillColor:color.backgroundColor,textColor:color.textColor}}
            : reportCellText(column,row);
        })),
    styles: { fontSize: report.columns.length > 10 ? 6 : 8, overflow: "linebreak" },
    columnStyles: Object.fromEntries(report.columns.map((column,index) => [index, { halign: column.align ?? "left" }])),
    headStyles: { fillColor: [15, 118, 110] },
    rowPageBreak: report.keepRowsTogether ? "avoid" : "auto",
    didDrawPage: data => {
      nextY=(data.cursor?.y ?? nextY)+8;
      pdf.setFontSize(8);
      pdf.text(footerBrand, 12, 202 - footerExtraHeight);
      pdf.text(`Auraly · ${pdf.getNumberOfPages()}`, 285, 202, { align: "right" });
    },
  });
  }
  pdf.save(`${safeReportFileName(report.fileName)}.pdf`);
}

export async function exportReportExcel(report: ReportExport) {
  const { default: writeExcelFile } = await import("write-excel-file/browser");
  const columnHeaders = report.columns.map(column => ({ value: column.label, fontWeight: "bold" as const, backgroundColor: "#0F766E", textColor: "#FFFFFF" }));
  const rows: SheetData = [
    [report.brandName], [report.title], [report.description ?? ""],
    ...(report.groupedHeaders ? [] : [columnHeaders]),
  ];
  let needsHeaders = false;
  for (const row of report.rows) {
    if (row.__group) {
      if (report.groupedHeaders && row.__groupLevel === 1) needsHeaders=true;
      rows.push(row.__groupLevel === 1 ? [{value:String(row.__group),columnSpan:report.columns.length,
      fontWeight:"bold" as const,backgroundColor:"#0F766E",textColor:"#FFFFFF",wrap:true},
      ...Array.from({length:report.columns.length-1},()=>null)] : [String(row.__group)]);
    } else {
      if (needsHeaders) { rows.push(columnHeaders); needsHeaders=false; }
      rows.push(report.columns.map(column => reportExcelCell(column,row)));
    }
  }
  await writeExcelFile(rows, { sheet: "Reporte", stickyRowsCount: report.groupedHeaders ? 3 : 4,
    columns: report.columns.map(column => ({ width: column.align === "right" ? 18 : 28 }))
  }).toFile(`${safeReportFileName(report.fileName)}.xlsx`);
}

export function reportExcelCell(column: ReportColumn, row: ReportRow) {
  const value = row[column.key];
  const style=column.cellStyle?.(row);
  if (column.excelType === "date" && typeof value === "string") {
    const day = value.slice(0,10);
    const date = new Date(`${day}T00:00:00.000Z`);
    if (!/^\d{4}-\d{2}-\d{2}$/.test(day) || !Number.isFinite(date.getTime()) || date.toISOString().slice(0,10) !== day)
      throw new Error(`La fecha de la columna ${column.label} no es válida para exportar.`);
    return { value: date, type: Date, format: "dd/mm/yyyy",...style };
  }
  // Only explicitly typed dates are converted. Identifiers and formula-looking
  // user input stay strings; monetary values remain numeric with their scale.
  return typeof value === "number" ? { value, format: column.numberFormat ?? "#,##0.00",...style }
    : style ? {value:reportCellText(column,row),...style} : reportCellText(column,row);
}
