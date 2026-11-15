import type { ReceivablesReportPage } from "../services/api/receivables";
import type { PayablesReportPage } from "../services/api/payables";
import type { ReportExport } from "./report-export";
import type { ReportColumn, ReportRow } from "./report-viewer";

const amounts = new Intl.NumberFormat("es-CO", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
export const portfolioAgingBands = [
  ["notDueAmount", "Sin vencer", "NotDue", "#DDF4E8", "#166443"],
  ["overdue1To30Amount", "1–30 días", "Overdue1To30", "#FFF1C2", "#805B00"],
  ["overdue31To60Amount", "31–60 días", "Overdue31To60", "#FFE2CC", "#934317"],
  ["overdue61To90Amount", "61–90 días", "Overdue61To90", "#F9DCE5", "#933657"],
  ["overdueOver90Amount", "Más de 90 días", "OverdueOver90", "#E8DFF7", "#65428B"],
] as const;
const ages = portfolioAgingBands;

type PortfolioRow = ReceivablesReportPage["items"][number] | PayablesReportPage["items"][number];

// Group only the already selected page (or the explicitly requested export).
// Totals remain the authoritative customer/site totals returned by the owner.
export function groupPortfolioReportRows(items: readonly PortfolioRow[]) {
  const parties = new Map<string, Map<string, PortfolioRow[]>>();
  for (const item of items) {
    const partyId = "customerId" in item ? item.customerId ?? item.partyId : item.supplierId;
    const partyKey = String(partyId);
    const sites = parties.get(partyKey) ?? new Map<string, PortfolioRow[]>();
    const siteKey = JSON.stringify([item.partySiteId, item.currencyCode]);
    const rows = sites.get(siteKey) ?? [];
    rows.push(item); sites.set(siteKey, rows); parties.set(partyKey, sites);
  }
  return parties;
}

// Presentation of the owner's closed band codes; aging is calculated by the server.
export function portfolioAgingBandLabel(code: string | null | undefined): string {
  return code === "NotDue" ? "Sin vencer" : ages.find(item => item[2] === code)?.[1] ?? "—";
}

export function portfolioAgingBandStyle(code: string | null | undefined) {
  const band = ages.find(item => item[2] === code);
  return band ? {backgroundColor:band[3],textColor:band[4]} : undefined;
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
      ...(customerReport && options.format !== "pdf" ? [{key:"internalDocumentNumber",label:"Referencia interna"}] : []),
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
    ...(options.consolidated ? ages.map(([key,label,code]) => ({...moneyColumn(key,label),cellStyle:() => portfolioAgingBandStyle(code)}))
      : [{ key: "age", label: "Edad de cartera",cellStyle:(row:ReportRow) => portfolioAgingBandStyle(typeof row.ageBandCode === "string" ? row.ageBandCode : null) }]),
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
  let previousParty: string | undefined;
  let previousCustomer: ReceivablesReportPage["items"][number] | undefined;
  function closeCustomerGroup() {
    if (!previousCustomer?.groupTotals) return;
    const item = previousCustomer;
    rows.push({...item.groupTotals, name:"SUBTOTAL cliente / sede", documentNumber:"Subtotal cliente / sede",
      identification:item.identification,site:item.partySiteName,currencyCode:item.currencyCode});
    rows.push({__group:`Edades del cliente / sede: ${ages.map(([key,label]) => `${label}: ${amounts.format(item.groupTotals![key])}`).join(" · ")}`});
    if (withCredits && compactCustomerPdf) rows.push({__group:`Anticipos disponibles: ${amounts.format(item.groupTotals!.availableAdvances!)} · Otros saldos a favor: ${amounts.format(item.groupTotals!.availableCredits!)} · Neto comercial: ${amounts.format(item.groupTotals!.netAmount!)}`});
  }
  const orderedItems = customerReport && !options.consolidated
    ? Array.from(groupPortfolioReportRows(data.items).values()).flatMap(sites => Array.from(sites.values()).flat())
    : data.items;
  for (const item of orderedItems) {
    if ("customerId" in item && !options.consolidated) {
      const group = JSON.stringify([item.customerId ?? item.partyId,item.partySiteId,item.currencyCode]);
      if (group !== previousGroup) {
        closeCustomerGroup();
        const party = String(item.customerId ?? item.partyId);
        if (party !== previousParty) {
          rows.push({__group:item.customerName,__groupLevel:1});
          rows.push({__group:[`Identificación: ${item.identification}`,
            item.legalName !== item.customerName ? item.legalName : null,
            item.address,item.city,item.phone,item.email].filter(Boolean).join(" · "),__groupLevel:2});
          previousParty = party;
        }
        rows.push({__group:[item.partySiteName ?? "Sin sede identificada",item.currencyCode,
          `Cupo vigente: ${item.creditLimit == null ? "Sin cupo definido" : amounts.format(item.creditLimit)}`].join(" · ")});
        previousGroup = group;
      }
      previousCustomer = item;
      if (item.isCreditBalance) continue;
    }
    const identity = { name: "customerName" in item ? item.customerName : item.supplierName,
      identification: item.identification, site: item.partySiteName ?? "Sin sede identificada", currencyCode: item.currencyCode };
    rows.push({ ...identity, invoiceCount: item.invoiceCount, documentNumber: item.documentNumber,
      issuedAt: item.issuedAt, dueDate: item.dueDate,
      ...("customerId" in item ? {internalDocumentNumber:item.internalDocumentNumber} : {}),
      ...("customerId" in item ? {termDays:item.termDays,ageDays:item.ageDays,daysPastDue:item.daysPastDue,
        legalName:item.legalName,address:item.address,city:item.city,phone:item.phone,email:item.email,creditLimit:item.creditLimit,
        ...(options.consolidated ? {availableAdvances:item.availableAdvances,availableCredits:item.availableCredits,netAmount:item.netAmount} : {})} : {}),
      originalAmount: item.originalAmount, paidAmount: item.paidAmount, otherImpact: item.otherImpact,
      outstandingAmount: item.outstandingAmount, overdueAmount: item.overdueAmount,
      notDueAmount: item.notDueAmount, overdue1To30Amount: item.overdue1To30Amount,
      overdue31To60Amount: item.overdue31To60Amount, overdue61To90Amount: item.overdue61To90Amount,
      overdueOver90Amount: item.overdueOver90Amount,
      ageBandCode: ages.find(([key]) => item[key] > 0)?.[2],
      age: item.outstandingAmount > 0 ? portfolioAgingBandLabel(ages.find(([key]) => item[key] > 0)?.[2]) : "Sin saldo" });
    if (compactCustomerPdf && "customerId" in item && item.internalDocumentNumber)
      rows.push({__group:`Referencia interna de ${item.documentNumber}: ${item.internalDocumentNumber}`});
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
    if (customerReport && !options.consolidated) rows.push({__group:`TOTAL GENERAL ${total.currencyCode}`,__groupLevel:1});
    rows.push({ ...total, name: `TOTAL ${total.currencyCode}`, documentNumber: `TOTAL ${total.currencyCode}` });
    if (!options.consolidated)
      rows.push({ __group: `Edades ${total.currencyCode}: ${ages.map(([key,label]) => `${label}: ${amounts.format(total[key])}`).join(" · ")}` });
    if (withCredits && compactCustomerPdf && "netAmount" in total)
      rows.push({__group:`Anticipos disponibles: ${amounts.format(total.availableAdvances!)} · Otros saldos a favor: ${amounts.format(total.availableCredits!)} · Neto comercial: ${amounts.format(total.netAmount!)}`});
  }
  return { title: options.title, brandName: options.brandName, logoSource: options.logoSource,
    keepRowsTogether: compactCustomerPdf,
    groupedHeaders: customerReport && !options.consolidated,
    description: `Corte: ${portfolioReportDate(options.cutoff)} · ${options.filterSummary}${customerReport ? " · Edad desde emisión; mora desde vencimiento (negativa: sin vencer). Contacto y cupo vigentes." : ""}`,
    fileName: `${options.title}-${options.cutoff}`, rows, columns };
}
