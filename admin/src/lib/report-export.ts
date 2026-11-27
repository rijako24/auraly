import { reportCellText, safeReportFileName, type ReportColumn, type ReportRow } from "./report-viewer";
import type { SheetData } from "write-excel-file/browser";
import type { Styles } from "jspdf-autotable";

export type ReportExport = {
  title: string;
  description?: string;
  brandName: string;
  brandIdentification?: string;
  generatedAt?: Date;
  fileName: string;
  rows: ReportRow[];
  columns: ReportColumn[];
  logoSource?: string | null;
  keepRowsTogether?: boolean;
  groupedHeaders?: boolean;
  pdfLayout?: "portrait-document";
};

export async function exportReportPdf(report: ReportExport) {
  const [{ jsPDF }, { autoTable }] = await Promise.all([import("jspdf"), import("jspdf-autotable")]);
  const portrait = report.pdfLayout === "portrait-document";
  const pdf = new jsPDF({ orientation: portrait ? "portrait" : "landscape", unit: "mm", format: "a4" });
  const pageWidth = pdf.internal.pageSize.getWidth();
  const pageHeight = pdf.internal.pageSize.getHeight();
  const right = pageWidth - 12;
  const brandX = report.logoSource ? 36 : 12;
  if (report.logoSource) {
    const image = pdf.getImageProperties(report.logoSource);
    const scale = Math.min(18 / image.width, 18 / image.height);
    const width = image.width * scale, height = image.height * scale;
    pdf.setDrawColor(220, 225, 230);
    pdf.roundedRect(12, 8, 20, 20, 2, 2);
    pdf.addImage(report.logoSource, image.fileType, 13 + (18 - width) / 2, 9 + (18 - height) / 2, width, height);
  }
  pdf.setFontSize(10);
  pdf.setFont("helvetica", "bold");
  const brandLines = pdf.splitTextToSize(report.brandName, (portrait ? right : 210) - brandX);
  pdf.text(brandLines, brandX, 13);
  pdf.setFont("helvetica", "normal");
  pdf.setFontSize(8);
  if (!portrait) {
    const generated = (report.generatedAt ?? new Date()).toLocaleString("es-CO");
    pdf.text([`Generado: ${generated}`, `Registros: ${report.rows.filter(row => !row.__group).length}`, "Sistema: Auraly"], 220, 12);
  }
  let headerY = Math.max(portrait && !report.logoSource ? 20 : 31, 13 + brandLines.length * 4.5);
  if (report.brandIdentification) { pdf.text(report.brandIdentification, 12, headerY); headerY += 6; }
  if (!portrait) {
    pdf.setFontSize(7); pdf.setTextColor(15, 118, 110);
    pdf.text("REPORTE CORPORATIVO", 12, headerY); headerY += 7;
  }
  pdf.setTextColor(2, 6, 23); pdf.setFont("helvetica", "bold");
  pdf.setFontSize(16);
  const titleLines = pdf.splitTextToSize(report.title, right - 12);
  pdf.text(titleLines, 12, headerY); headerY += titleLines.length * 6;
  pdf.setFont("helvetica", "normal");
  pdf.setFontSize(9);
  const description = pdf.splitTextToSize(report.description ?? "", right - 12);
  pdf.text(description, 12, headerY); headerY += description.length * 4 + 3;
  if (portrait) pdf.setDrawColor(148, 163, 184);
  else pdf.setDrawColor(15, 118, 110);
  pdf.setLineWidth(.4); pdf.line(12, headerY, right, headerY);
  pdf.setFontSize(8);
  const footerBrand = pdf.splitTextToSize([report.brandName, report.brandIdentification].filter(Boolean).join(" · "), right - 35);
  const footerExtraHeight = Math.max(0, footerBrand.length - 1) * 3.5;
  const sections: Array<{heading?:ReportRow;metadata:ReportRow[];rows:ReportRow[]}> = [];
  const documentHeading: Partial<Styles> = { fontSize: 12, fontStyle: "bold", fillColor: [226, 232, 240], textColor: [15, 23, 42], cellPadding: 3 };
  const documentMetadata: Partial<Styles> = { fontSize: 9, fontStyle: "normal", fillColor: [248, 250, 252], textColor: [51, 65, 85] };
  const documentTotal: Partial<Styles> = { fillColor: [226, 232, 240], textColor: [15, 23, 42], fontStyle: "bold", fontSize: 11 };
  for (const row of report.rows) {
    if (report.groupedHeaders && row.__groupLevel === 1) sections.push({heading:row,metadata:[],rows:[]});
    else {
      if (!sections.length) sections.push({metadata:[],rows:[]});
      const section=sections[sections.length-1];
      if (report.groupedHeaders && row.__groupLevel === 2) section.metadata.push(row);
      else section.rows.push(row);
    }
  }
  let nextY = headerY + 6;
  const pagesWithFooter = new Set<number>();
  for (const section of sections) {
    autoTable(pdf, {
    startY: nextY,
    margin: { left: 12, right: 12, bottom: 15 + footerExtraHeight },
    head: [...(section.heading ? [[{content:String(section.heading.__group),colSpan:report.columns.length,
      styles: portrait ? documentHeading : { fontSize: 10, fontStyle: "bold" as const }}],
      ...section.metadata.map(row=>[{content:String(row.__group),colSpan:report.columns.length,
        styles: portrait ? documentMetadata : { fontSize: 7, fontStyle: "normal" as const }}])] : []),report.columns.map(column => column.label)],
    body: section.rows.map(row => row.__group
      ? [{ content: String(row.__group), colSpan: report.columns.length, styles: row.__groupLevel === 1
          ? { fillColor: [15, 118, 110], textColor: [255, 255, 255], fontStyle: "bold", fontSize: 10 }
          : { fillColor: portrait ? [248, 250, 252] : [220, 245, 240] } }]
      : portrait && row.__emphasis === "total" ? [
          { content: reportCellText(report.columns[0], row), styles: documentTotal },
          { content: report.columns.slice(1).map(column => reportCellText(column, row)).filter(Boolean).join(" · "),
            colSpan: report.columns.length - 1, styles: { ...documentTotal, halign: "right" } },
        ] : report.columns.map(column => {
          if (portrait && row.__emphasis === "subtotal") return { content: reportCellText(column, row),
            styles: { fillColor: [241, 245, 249], textColor: [15, 23, 42], fontStyle: "bold" } };
          const color=column.cellStyle?.(row);
          return color ? {content:reportCellText(column,row),styles:{fillColor:color.backgroundColor,textColor:color.textColor}}
            : reportCellText(column,row);
        })),
    styles: { fontSize: portrait ? 9 : report.columns.length > 10 ? 6 : 8, overflow: "linebreak" },
    columnStyles: Object.fromEntries(report.columns.map((column,index) => [index, { halign: column.align ?? "left" }])),
    headStyles: portrait ? { fillColor: [51, 65, 85], cellPadding: 2.5 } : { fillColor: [15, 118, 110] },
    rowPageBreak: report.keepRowsTogether ? "avoid" : "auto",
    didDrawPage: data => {
      nextY=(data.cursor?.y ?? nextY)+8;
      const page = pdf.getNumberOfPages();
      if (pagesWithFooter.has(page)) return;
      pagesWithFooter.add(page);
      pdf.setFontSize(8);
      pdf.text(footerBrand, 12, pageHeight - 8 - footerExtraHeight);
      pdf.text(`Auraly · ${page}`, right, pageHeight - 8, { align: "right" });
    },
  });
  }
  pdf.save(`${safeReportFileName(report.fileName)}.pdf`);
}

export async function exportReportExcel(report: ReportExport) {
  const { default: writeExcelFile } = await import("write-excel-file/browser");
  const columnHeaders = report.columns.map(column => ({ value: column.label, fontWeight: "bold" as const, backgroundColor: "#0F766E", textColor: "#FFFFFF" }));
  const rows: SheetData = [
    [[report.brandName, report.brandIdentification].filter(Boolean).join(" · ")], [report.title], [report.description ?? ""],
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
