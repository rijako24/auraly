import type { InvoiceChargeHistoryPage } from "../services/api/invoice-charges";
import type { ReportColumn, ReportRow } from "./report-viewer";
import { businessStatusLabel } from "./accounting-labels";

type Item = InvoiceChargeHistoryPage["items"][number];
const amounts = new Intl.NumberFormat("es-CO", { minimumFractionDigits: 0, maximumFractionDigits: 4 });
const amountColumn = (key:string,label:string):ReportColumn => ({key,label,align:"right",numberFormat:"#,##0.00##",
  format:value=>typeof value==="number"?amounts.format(value):""});

// Presentation of one explicitly requested, server-filtered report. The ordinary
// history remains paginated; this function never fetches or paginates documents.
export function buildInvoiceChargeReport(data:InvoiceChargeHistoryPage, options:{
  consolidated:boolean;description:string;formatDateTime:(value:string)=>string;
}) {
  const groups = new Map<string,Item[]>();
  for(const item of data.items){const group=groups.get(item.documentId);if(group)group.push(item);else groups.set(item.documentId,[item]);}
  const totals:ReportColumn[]=[amountColumn("customerAmount","Asumido cliente"),amountColumn("companyAmount","Asumido empresa"),
    amountColumn("expenseAmount","Gastos generados")];
  const columns:ReportColumn[]=options.consolidated?[
    {key:"invoiceNumber",label:"Número interno"},{key:"fiscalNumber",label:"Número DIAN"},{key:"customerName",label:"Cliente"},
    {key:"issuedAt",label:"Fecha"},amountColumn("invoiceTotal","Total factura"),...totals,
  ]:[{key:"expenseNumber",label:"Gasto / cargo"},{key:"supplier",label:"Proveedor"},{key:"createdBy",label:"Generado por"},
    {key:"createdAt",label:"Registro"},...totals,{key:"status",label:"Estado"}];
  const rows:ReportRow[]=[];
  for(const items of groups.values()){
    const invoice=items[0];
    if(options.consolidated){
      rows.push({invoiceNumber:invoice.documentNumber,fiscalNumber:invoice.fiscalNumber,customerName:invoice.customerName,
        issuedAt:options.formatDateTime(invoice.issuedAt),invoiceTotal:invoice.invoiceTotal,
        customerAmount:items.reduce((sum,item)=>sum+item.charge.invoicedAmount,0),
        companyAmount:items.reduce((sum,item)=>sum+item.charge.expenseAmount,0),
        expenseAmount:items.reduce((sum,item)=>sum+(item.expenseGrossAmount??0),0)});
    }else{
      rows.push({__group:`Factura ${invoice.documentNumber}${invoice.fiscalNumber?` · DIAN ${invoice.fiscalNumber}`:""}`,__groupLevel:1});
      rows.push({__group:`${invoice.customerName} · ${options.formatDateTime(invoice.issuedAt)} · Total factura: ${amounts.format(invoice.invoiceTotal)}`,__groupLevel:2});
      for(const item of items)rows.push({expenseNumber:[item.expenseDocumentNumber,item.expenseFiscalNumber?`DIAN ${item.expenseFiscalNumber}`:null,item.charge.name].filter(Boolean).join(" · "),
        supplier:item.charge.supplier.name,createdBy:item.expenseCreatedByName,
        createdAt:item.expenseAcceptedAt?options.formatDateTime(item.expenseAcceptedAt):null,
        customerAmount:item.charge.invoicedAmount,companyAmount:item.charge.expenseAmount,expenseAmount:item.expenseGrossAmount,
        status:item.expenseStatus?businessStatusLabel(item.expenseStatus):"Sin gasto vinculado"});
    }
  }
  rows.push({__group:"Totales del filtro",__groupLevel:1});
  rows.push({invoiceNumber:"TOTAL",expenseNumber:"TOTAL",customerAmount:data.invoicedTotal,companyAmount:data.expenseTotal,
    expenseAmount:data.generatedExpenseTotal});
  return {title:options.consolidated?"Cargos por factura — consolidado":"Cargos por factura — detallado",
    description:options.description,fileName:options.consolidated?"cargos-consolidado":"cargos-detallado",
    rows,columns,groupedHeaders:false};
}
