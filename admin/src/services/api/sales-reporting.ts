import { apiClient } from "./client";

export type SalesDimension="customer"|"seller"|"supplier"|"product"|"category"|"warehouse"|"day"|"month"|"payment-method"|"tax";
export interface SalesReportFilter {from:string;to:string;customerId?:string;sellerId?:string;supplierId?:string;productId?:string;categoryId?:string;warehouseId?:string;documentType?:"SalesInvoice"|"SalesReceipt";}
export interface SalesReportTotals {documentCount:number;unitsSold:number;unitsReturned:number;grossSales:number;discounts:number;returns:number;netUntaxedSales:number;netTax:number;netTotalSales:number;netRecognizedCost:number;grossProfit:number;grossMarginPercent:number;creditSales:number;collected:number;refunded:number;}
export interface SalesTrendPoint {period:string;documentCount:number;grossSales:number;returns:number;netSales:number;grossProfit:number;}
export interface SalesReportSummary {current:SalesReportTotals;comparison:SalesReportTotals|null;netSalesChangePercent:number|null;trend:SalesTrendPoint[];projectedThrough:string|null;}
export interface SalesBreakdownRow {key:string;label:string;documentCount:number;quantity:number;grossSales:number;discounts:number;returns:number;netUntaxedSales:number;tax:number;netSales:number;recognizedCost:number;grossProfit:number;grossMarginPercent:number;participationPercent:number;}
export interface SalesDocumentRow {documentId:string;documentType:string;documentNumber:string;fiscalNumber:string|null;issuedAt:string;customerName:string;sellerName:string;warehouseName:string;grossAmount:number;discountAmount:number;untaxedAmount:number;taxAmount:number;totalAmount:number;returnedTotalAmount:number;netTotalAmount:number;grossProfit:number;fiscalStatus:string|null;}
export interface SalesDocumentPage {items:SalesDocumentRow[];page:number;pageSize:number;totalCount:number;}
export interface SalesLineRow {factId:string;movementType:string;occurredAt:string;productCode:string;productName:string;categoryName:string|null;quantity:number;grossAmount:number;discountAmount:number;untaxedAmount:number;taxAmount:number;totalAmount:number;recognizedCostAmount:number;returnReasonCode:string|null;returnDisposition:string|null;}
export interface SalesDocumentDetail {document:SalesDocumentRow;lines:SalesLineRow[];}

const params=(filter:SalesReportFilter)=>({...filter});
export const salesReportingApi={
  summary:(filter:SalesReportFilter,comparison?:{from:string;to:string})=>apiClient.get<SalesReportSummary>("/commerce/v1/sales-reports/summary",{...params(filter),comparisonFrom:comparison?.from,comparisonTo:comparison?.to}),
  breakdown:(filter:SalesReportFilter,dimension:SalesDimension,limit=50)=>apiClient.get<SalesBreakdownRow[]>("/commerce/v1/sales-reports/breakdown",{...params(filter),dimension,limit}),
  documents:(filter:SalesReportFilter,page=1,pageSize=50,search?:string)=>apiClient.get<SalesDocumentPage>("/commerce/v1/sales-reports/documents",{...params(filter),page,pageSize,search}),
  document:(id:string)=>apiClient.get<SalesDocumentDetail>(`/commerce/v1/sales-reports/documents/${id}`),
};
