import type { ReceivablesReportPage } from "../services/api/receivables";
import type { PayablesReportPage } from "../services/api/payables";
import type { ReportExport } from "./report-export";
import type { ReportColumn, ReportRow } from "./report-viewer";

const amounts = new Intl.NumberFormat("es-CO", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
const ages = [
  ["notDueAmount", "Al día", "NotDue"], ["overdue1To30Amount", "1–30 días", "Overdue1To30"],
  ["overdue31To60Amount", "31–60 días", "Overdue31To60"], ["overdue61To90Amount", "61–90 días", "Overdue61To90"],
  ["overdueOver90Amount", "Más de 90 días", "OverdueOver90"],
] as const;

// Presentation of the owner's closed band codes; aging is calculated by the server.
export function portfolioAgingBandLabel(code: string | null | undefined): string {
  return ages.find(item => item[2] === code)?.[1] ?? "—";
}

// Report instants carry the business-zone offset supplied by their owner.
// Keep that calendar date in the screen, PDF and workbook regardless of browser zone.
export function portfolioReportDate(value: string | null | undefined): string {
  return value ? value.slice(0, 10).split("-").reverse().join("/") : "—";
}

export function buildPortfolioReportExport(data: ReceivablesReportPage | PayablesReportPage,
  options: { title: string; brandName: string; cutoff: string; filterSummary: string;
    consolidated: boolean; includeApplications: boolean; logoSource?: string | null; format?: "pdf" | "xlsx" }): ReportExport {
  const customerReport = !("currencyTotals" in data);
  const withCredits = "totalNetAmount" in data && data.totalNetAmount !== undefined;
  const compactCustomerPdf = customerReport && !options.consolidated && options.format === "pdf";
  const moneyColumn = (key: string, label: string): ReportColumn => ({ key, label, align: "right",
    numberFormat: "#,##0.00##", format: value => typeof value === "number" ? amounts.format(value) : "" });
  const dateColumn = (key: string, label: string): ReportColumn => ({ key, label, excelType: "date",
    format: value => portfolioReportDate(typeof value === "string" ? value : null) });
  const columns: ReportColumn[] = [
    ...(compactCustomerPdf ? [] : [
      { key: "name", label: "Tercero" }, { key: "identification", label: "Identificación" },
      { key: "site", label: "Sede" }, { key: "currencyCode", label: "Moneda" },
    ]),
    ...(options.consolidated ? [{ key: "invoiceCount", label: "Documentos", numberFormat: "0", align: "right" as const }] : [
      { key: "documentNumber", label: "Documento" },
      dateColumn("issuedAt", "Emisión"), dateColumn("dueDate", "Vencimiento"),
      ...(customerReport ? [
        {key:"termDays",label:"Plazo (días)",numberFormat:"0",align:"right" as const},
        {key:"ageDays",label:"Edad (días)",numberFormat:"0",align:"right" as const},
        {key:"daysPastDue",label:"Mora (días)",numberFormat:"0",align:"right" as const},
      ] : []),
    ]),
    moneyColumn("originalAmount", "Original"), moneyColumn("paidAmount", "Abonado / pagado"),
    moneyColumn("otherImpact", "Notas y ajustes"), moneyColumn("outstandingAmount", "Saldo pendiente"),
    moneyColumn("overdueAmount", "Vencido"),
    ...(withCredits && !compactCustomerPdf ? [moneyColumn("availableAdvances","Anticipos disponibles"),
      moneyColumn("availableCredits","Otros saldos a favor"),moneyColumn("netAmount","Neto comercial")] : []),
    ...(options.consolidated ? ages.map(([key,label]) => moneyColumn(key,label)) : [{ key: "age", label: "Tramo" }]),
    ...(customerReport && options.format !== "pdf" ? [
      {key:"legalName",label:"Razón social"},{key:"address",label:"Dirección"},{key:"city",label:"Ciudad"},
      {key:"phone",label:"Teléfono"},{key:"email",label:"Correo"},moneyColumn("creditLimit","Cupo vigente"),
    ] : []),
    ...(!options.consolidated && options.includeApplications && !compactCustomerPdf ? [
      { key: "applicationDocument", label: "Recibo / comprobante" },
      dateColumn("appliedAt", "Fecha de aplicación"),
      moneyColumn("applicationAmount", "Importe aplicado"),
    ] : []),
  ];
  const rows: ReportRow[] = [];
  let previousGroup: string | undefined;
  let previousCustomer: ReceivablesReportPage["items"][number] | undefined;
  function closeCustomerGroup() {
    if (!previousCustomer?.groupTotals) return;
    const item = previousCustomer;
    rows.push({...item.groupTotals, name:"SUBTOTAL cliente / sede", documentNumber:"Subtotal cliente / sede",
      identification:item.identification,site:item.partySiteName,currencyCode:item.currencyCode});
    rows.push({__group:`Edades del cliente / sede: ${ages.map(([key,label]) => `${label}: ${amounts.format(item.groupTotals![key])}`).join(" · ")}`});
    if (withCredits && compactCustomerPdf) rows.push({__group:`Anticipos disponibles: ${amounts.format(item.groupTotals!.availableAdvances!)} · Otros saldos a favor: ${amounts.format(item.groupTotals!.availableCredits!)} · Neto comercial: ${amounts.format(item.groupTotals!.netAmount!)}`});
  }
  for (const item of data.items) {
    if ("customerId" in item && !options.consolidated) {
      const group = JSON.stringify([item.customerId ?? item.partyId,item.partySiteId,item.currencyCode]);
      if (group !== previousGroup) {
        closeCustomerGroup();
        rows.push({__group:[item.customerName,item.identification,item.partySiteName,item.currencyCode,
          item.legalName,item.address,item.city,item.phone,item.email,
          `Cupo vigente: ${item.creditLimit == null ? "Sin cupo definido" : amounts.format(item.creditLimit)}`].filter(Boolean).join(" · ")});
        previousGroup = group;
      }
      previousCustomer = item;
    }
    const identity = { name: "customerName" in item ? item.customerName : item.supplierName,
      identification: item.identification, site: item.partySiteName ?? "Sin sede identificada", currencyCode: item.currencyCode };
    rows.push({ ...identity, invoiceCount: item.invoiceCount, documentNumber: item.documentNumber,
      issuedAt: item.issuedAt, dueDate: item.dueDate,
      ...("customerId" in item ? {termDays:item.termDays,ageDays:item.ageDays,daysPastDue:item.daysPastDue,
        legalName:item.legalName,address:item.address,city:item.city,phone:item.phone,email:item.email,creditLimit:item.creditLimit,
        availableAdvances:item.availableAdvances,availableCredits:item.availableCredits,netAmount:item.netAmount} : {}),
      originalAmount: item.originalAmount, paidAmount: item.paidAmount, otherImpact: item.otherImpact,
      outstandingAmount: item.outstandingAmount, overdueAmount: item.overdueAmount,
      notDueAmount: item.notDueAmount, overdue1To30Amount: item.overdue1To30Amount,
      overdue31To60Amount: item.overdue31To60Amount, overdue61To90Amount: item.overdue61To90Amount,
      overdueOver90Amount: item.overdueOver90Amount,
      age: ages.find(([key]) => item[key] > 0)?.[1] ?? "Sin saldo" });
    if (compactCustomerPdf && "isCreditBalance" in item && item.isCreditBalance)
      rows.push({__group:`Anticipos disponibles: ${amounts.format(item.availableAdvances!)} · Otros saldos a favor: ${amounts.format(item.availableCredits!)} · Neto comercial: ${amounts.format(item.netAmount!)}`});
    if (!options.consolidated && options.includeApplications && !("isCreditBalance" in item && item.isCreditBalance))
      for (const application of item.applications ?? [])
        rows.push({ ...identity, documentNumber: item.documentNumber,
          ...(compactCustomerPdf ? {__group:`Abono ${application.documentNumber} · ${portfolioReportDate(application.appliedAt)} · ${amounts.format(application.amount)} ${item.currencyCode}`} : {}),
          applicationDocument: application.documentNumber, appliedAt: application.appliedAt, applicationAmount: application.amount });
  }
  closeCustomerGroup();
  const totals = "currencyTotals" in data ? data.currencyTotals : [{ currencyCode: "COP",
    invoiceCount: data.totalInvoiceCount, originalAmount: data.totalOriginal, paidAmount: data.totalPaid,
    otherImpact: data.totalOtherImpact, outstandingAmount: data.totalOutstanding, overdueAmount: data.totalOverdue,
    availableAdvances:data.totalAvailableAdvances,availableCredits:data.totalAvailableCredits,netAmount:data.totalNetAmount,
    notDueAmount: data.totalNotDue, overdue1To30Amount: data.totalOverdue1To30,
    overdue31To60Amount: data.totalOverdue31To60, overdue61To90Amount: data.totalOverdue61To90,
    overdueOver90Amount: data.totalOverdueOver90 }];
  for (const total of totals) {
    rows.push({ ...total, name: `TOTAL ${total.currencyCode}`, documentNumber: `TOTAL ${total.currencyCode}` });
    if (!options.consolidated)
      rows.push({ __group: `Edades ${total.currencyCode}: ${ages.map(([key,label]) => `${label}: ${amounts.format(total[key])}`).join(" · ")}` });
    if (withCredits && compactCustomerPdf && "netAmount" in total)
      rows.push({__group:`Anticipos disponibles: ${amounts.format(total.availableAdvances!)} · Otros saldos a favor: ${amounts.format(total.availableCredits!)} · Neto comercial: ${amounts.format(total.netAmount!)}`});
  }
  return { title: options.title, brandName: options.brandName, logoSource: options.logoSource,
    keepRowsTogether: compactCustomerPdf,
    description: `Corte: ${portfolioReportDate(options.cutoff)} · ${options.filterSummary}${customerReport ? " · Edad desde emisión; mora desde vencimiento (negativa: sin vencer). Contacto y cupo vigentes." : ""}`,
    fileName: `${options.title}-${options.cutoff}`, rows, columns };
}
