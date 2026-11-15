import assert from "node:assert/strict";
import { test } from "node:test";
import type { PayablesReportPage } from "../services/api/payables";
import type { ReceivablesReportPage } from "../services/api/receivables";
import { buildPortfolioReportExport, groupPortfolioReportRows, portfolioReportDate, portfolioAgingBandLabel, portfolioAgingBandStyle, portfolioAgingBands } from "./portfolio-report-export";
import { reportExcelCell } from "./report-export";

test("credit rows preserve server balances once and export a negative commercial net as a number", () => {
  const bands={notDueAmount:0,overdue1To30Amount:1_000_000,overdue31To60Amount:0,overdue61To90Amount:0,overdueOver90Amount:0};
  const totals={invoiceCount:2,originalAmount:1_000_000,paidAmount:0,otherImpact:0,outstandingAmount:1_000_000,
    availableAdvances:2_000_000,availableCredits:0,netAmount:-1_000_000,...bands};
  const invoice={customerId:"customer",partyId:"party",customerName:"Cliente",identification:"123",
    partySiteId:"site",partySiteName:"Centro",currencyCode:"COP",invoiceCount:1,
    originalAmount:500_000,paidAmount:0,outstandingAmount:500_000,overdueAmount:500_000,otherImpact:0,
    receivableId:"a",documentNumber:"FAC-A",issuedAt:null,dueDate:null,applications:[],
    availableAdvances:0,availableCredits:0,netAmount:500_000,groupTotals:totals,...bands};
  const data:ReceivablesReportPage={items:[invoice,{...invoice,receivableId:"b",documentNumber:"FAC-B"},
    {...invoice,receivableId:null,documentNumber:"Anticipos y otros saldos a favor",invoiceCount:0,
      originalAmount:0,outstandingAmount:0,overdueAmount:0,overdue1To30Amount:0,
      availableAdvances:2_000_000,netAmount:-2_000_000,isCreditBalance:true}],
    page:1,pageSize:50,totalCount:3,totalPages:1,totalInvoiceCount:2,totalOriginal:1_000_000,totalPaid:0,
    totalOutstanding:1_000_000,totalOverdue:1_000_000,totalOtherImpact:0,totalNotDue:0,
    totalOverdue1To30:1_000_000,totalOverdue31To60:0,totalOverdue61To90:0,totalOverdueOver90:0,
    totalAvailableAdvances:2_000_000,totalAvailableCredits:0,totalNetAmount:-1_000_000};
  const options={title:"Cartera",brandName:"Empresa",cutoff:"2026-11-09",filterSummary:"Todos",consolidated:false,includeApplications:false};
  const excel=buildPortfolioReportExport(data,{...options,format:"xlsx"});
  const total=excel.rows.find(row=>row.documentNumber==="TOTAL COP")!;
  assert.deepEqual(reportExcelCell(excel.columns.find(column=>column.key==="netAmount")!,total),
    {value:-1_000_000,format:"#,##0.00##"});
  assert.ok(excel.rows.filter(row=>row.documentNumber?.toString().startsWith("FAC-")).every(row=>row.availableAdvances === undefined));
  assert.equal(excel.rows.some(row=>row.documentNumber === "Anticipos y otros saldos a favor"),false);
  assert.equal(excel.rows.filter(row=>row.__group === "Cliente" && row.__groupLevel === 1).length,1);
  assert.equal(excel.groupedHeaders,true);
  assert.equal(excel.rows.find(row=>row.documentNumber==="Subtotal cliente / sede")?.availableAdvances,2_000_000);
  const pdf=buildPortfolioReportExport(data,{...options,format:"pdf"});
  assert.ok(pdf.rows.some(row=>String(row.__group).includes("Neto comercial: -1.000.000")));
});

test("aging presentation uses the owner's band code and never invents a current band", () => {
  assert.equal(portfolioAgingBandLabel("Overdue31To60"),"31–60 días");
  assert.equal(portfolioAgingBandLabel("NotDue"),"Sin vencer");
  assert.equal(portfolioAgingBandLabel(undefined),"—");
  assert.equal(portfolioAgingBandLabel("unknown"),"—");
});

test("the report keeps business-calendar dates instead of converting them to the browser zone", () => {
  assert.equal(portfolioReportDate("2026-11-09T23:30:00-05:00"), "09/11/2026");
  const cell = reportExcelCell({key:"date",label:"Fecha",excelType:"date"},{date:"2026-11-09T23:30:00-05:00"});
  assert.equal(typeof cell,"object");
  if (typeof cell !== "object") throw new Error("Expected a native spreadsheet date.");
  assert.equal(cell.value instanceof Date,true);
  assert.equal((cell.value as Date).toISOString(),"2026-11-09T00:00:00.000Z");
  assert.equal(reportExcelCell({key:"id",label:"Documento"},{id:"0000123"}),"0000123");
  assert.equal(reportExcelCell({key:"id",label:"Documento"},{id:"=0000123"}),"=0000123");
});

test("exports preserve server currency totals and put applications on rows that do not repeat invoice amounts", () => {
  const band = {notDueAmount:0,overdue1To30Amount:75.1234,overdue31To60Amount:0,overdue61To90Amount:0,overdueOver90Amount:0};
  const invoice = {supplierId:"supplier",supplierName:"Proveedor",identification:"0000123",partySiteId:"site",partySiteName:"Centro",
    currencyCode:"USD",invoiceCount:1,originalAmount:100.1234,paidAmount:25,otherImpact:0,outstandingAmount:75.1234,
    overdueAmount:75.1234,payableId:"invoice",documentNumber:"=00001",issuedAt:"2026-10-01T12:00:00-05:00",
    dueDate:"2026-10-09T12:00:00-05:00",applications:[{documentNumber:"EG-1",appliedAt:"2026-11-09T12:00:00-05:00",amount:25}],...band};
  const data = {items:[invoice],page:1,pageSize:5001,totalCount:1,totalPages:1,currencyTotals:[
    {currencyCode:"USD",invoiceCount:1,originalAmount:100.1234,paidAmount:25,otherImpact:0,
      outstandingAmount:75.1234,overdueAmount:75.1234,...band},
    {currencyCode:"COP",invoiceCount:4,originalAmount:20000,paidAmount:5000,otherImpact:0,
      outstandingAmount:15000,overdueAmount:15000,...band,overdue1To30Amount:15000}]} satisfies PayablesReportPage;
  const options = {title:"Cartera",brandName:"Empresa",cutoff:"2026-11-09",filterSummary:"Proveedor",consolidated:false,includeApplications:true};
  const exported=buildPortfolioReportExport(data,options);
  assert.equal(exported.rows[0].originalAmount,100.1234);
  assert.equal(exported.rows[0].identification,"0000123");
  assert.equal(exported.rows[0].documentNumber,"=00001");
  assert.equal(exported.rows[1].applicationAmount,25);
  assert.equal(exported.rows[1].originalAmount,undefined);
  assert.equal(exported.rows.find(row=>row.name==="TOTAL COP")?.outstandingAmount,15000);
  assert.equal(exported.rows.find(row=>row.name==="TOTAL USD")?.outstandingAmount,75.1234);
  const compact=buildPortfolioReportExport(data,{...options,includeApplications:false});
  assert.equal(compact.rows.some(row=>row.applicationDocument),false);
  assert.equal(compact.columns.some(column=>column.key==="applicationAmount"),false);
});

test("customer detail exports numeric terms and signed maturity, contact and authoritative group totals", () => {
  const bands={notDueAmount:75,overdue1To30Amount:0,overdue31To60Amount:0,overdue61To90Amount:0,overdueOver90Amount:0};
  const data:ReceivablesReportPage={items:[{customerId:"client",customerName:"Cliente",identification:"0001",partySiteId:"site",partySiteName:"Centro",
    currencyCode:"COP",receivableId:"invoice",documentNumber:"FAC-01",issuedAt:"2026-11-04T23:30:00-05:00",dueDate:"2026-11-12T00:00:00-05:00",
    termDays:8,ageDays:5,daysPastDue:-3,address:"Calle 1",phone:"3000000000",creditLimit:5000,
    invoiceCount:1,originalAmount:100,paidAmount:25,otherImpact:0,outstandingAmount:75,overdueAmount:0,...bands,
    groupTotals:{invoiceCount:4,originalAmount:400,paidAmount:100,otherImpact:0,outstandingAmount:300,...bands,notDueAmount:300},
    applications:[{documentNumber:"RC-01",appliedAt:"2026-11-05T12:00:00-05:00",amount:25}]}],
    page:1,pageSize:50,totalCount:4,totalPages:1,totalInvoiceCount:4,totalOriginal:400,totalPaid:100,totalOtherImpact:0,
    totalOutstanding:300,totalOverdue:0,totalNotDue:300,totalOverdue1To30:0,totalOverdue31To60:0,totalOverdue61To90:0,totalOverdueOver90:0};
  const options={title:"Cartera",brandName:"Empresa",cutoff:"2026-11-09",filterSummary:"Todos",consolidated:false,includeApplications:true};
  for (const format of ["pdf","xlsx"] as const) {
    const report=buildPortfolioReportExport(data,{...options,format});
    const invoice=report.rows.find(row=>row.documentNumber==="FAC-01" && row.originalAmount !== undefined)!;
    assert.equal(invoice.termDays,8); assert.equal(invoice.ageDays,5); assert.equal(invoice.daysPastDue,-3);
    const column=report.columns.find(column=>column.key==="daysPastDue")!;
    assert.deepEqual(reportExcelCell(column,invoice),{value:-3,format:"0"});
    assert.equal(report.rows[0].__group,"Cliente");
    assert.ok(report.rows.some(row=>/Calle 1.*3000000000/.test(String(row.__group))));
    assert.ok(report.rows.some(row=>/Cupo vigente: 5\.000/.test(String(row.__group))));
    assert.equal(report.rows.find(row=>row.documentNumber==="Subtotal cliente / sede")?.outstandingAmount,300);
    assert.equal(report.rows.find(row=>row.documentNumber==="TOTAL COP")?.outstandingAmount,300);
    assert.equal(report.rows.find(row=>row.applicationDocument==="RC-01")?.originalAmount,undefined);
    if (format === "xlsx") assert.deepEqual(reportExcelCell(report.columns.find(column=>column.key==="creditLimit")!,invoice),{value:5000,format:"#,##0.00##"});
  }
});

test("groups interleaved rows by customer before site without repeating a customer heading", () => {
  const bands={notDueAmount:10,overdue1To30Amount:0,overdue31To60Amount:0,overdue61To90Amount:0,overdueOver90Amount:0};
  const first:ReceivablesReportPage["items"][number]={customerId:"a",customerName:"Cliente A",identification:"1",partySiteId:"north",partySiteName:"Norte",
    currencyCode:"COP",invoiceCount:1,originalAmount:10,paidAmount:0,outstandingAmount:10,overdueAmount:0,otherImpact:0,
    receivableId:"a-1",documentNumber:"FE-1",issuedAt:null,dueDate:null,applications:[],...bands};
  const second={...first,customerId:"b",customerName:"Cliente B",identification:"2",receivableId:"b-1",documentNumber:"FE-2"};
  const rows=[first,second,{...first,partySiteId:"south",partySiteName:"Sur",receivableId:"a-2",documentNumber:"ASI-1"},
    {...first,receivableId:"a-3",documentNumber:"BTAX-3"}];
  const grouped=groupPortfolioReportRows(rows);
  assert.equal(grouped.size,2);
  assert.deepEqual(Array.from(grouped.get("a")!.values()).flat().map(row=>row.documentNumber),["FE-1","BTAX-3","ASI-1"]);
  assert.deepEqual(Array.from(grouped.get("b")!.values()).flat().map(row=>row.documentNumber),["FE-2"]);
  assert.equal(grouped.get("a")!.size,2);
});

test("aging colors stay distinct and only style classified values without changing numeric exports", () => {
  assert.equal(new Set(portfolioAgingBands.map(band=>band[3])).size,5);
  assert.equal(portfolioAgingBandStyle("unknown"),undefined);
  const style=portfolioAgingBandStyle("Overdue31To60")!;
  assert.equal(style.backgroundColor,"#FFE2CC");
  assert.deepEqual(reportExcelCell({key:"age",label:"Edad de cartera",cellStyle:()=>style},{age:"31–60 días"}),
    {value:"31–60 días",...style});
  assert.deepEqual(reportExcelCell({key:"amount",label:"Saldo"},{amount:100.1234}),{value:100.1234,format:"#,##0.00"});
});
