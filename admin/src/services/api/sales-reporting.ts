import { apiClient } from "./client";

export type SalesDimension="customer"|"seller"|"supplier"|"product"|"category"|"warehouse"|"day"|"hour"|"month"|"payment-method"|"tax";
export interface SalesReportFilter {from:string;to:string;customerId?:string;sellerId?:string;supplierId?:string;productId?:string;categoryId?:string;warehouseId?:string;documentType?:"SalesInvoice"|"SalesReceipt";}
export interface SalesReportTotals {documentCount:number;unitsSold:number;unitsReturned:number;grossSales:number;discounts:number;returns:number;netUntaxedSales:number;netTax:number;netTotalSales:number;netRecognizedCost:number;grossProfit:number;grossMarginPercent:number;creditSales:number;collected:number;refunded:number;}
export interface SalesTrendPoint {period:string;documentCount:number;grossSales:number;returns:number;netSales:number;grossProfit:number;}
export interface SalesReportSummary {current:SalesReportTotals;comparison:SalesReportTotals|null;netSalesChangePercent:number|null;trend:SalesTrendPoint[];projectedThrough:string|null;}
export interface SalesTodayOverview {businessDate:string;totals:SalesReportTotals;customerCount:number;averageTicket:number;returnRatePercent:number;projectedThrough:string|null;}
export interface SalesBreakdownRow {key:string;label:string;documentCount:number;quantity:number;grossSales:number;discounts:number;returns:number;netUntaxedSales:number;tax:number;netSales:number;recognizedCost:number;grossProfit:number;grossMarginPercent:number;participationPercent:number;}
export interface SalesDocumentRow {documentId:string;documentType:string;documentNumber:string;fiscalNumber:string|null;issuedAt:string;customerName:string;sellerName:string;warehouseName:string;grossAmount:number;discountAmount:number;untaxedAmount:number;taxAmount:number;totalAmount:number;returnedTotalAmount:number;netTotalAmount:number;grossProfit:number;fiscalStatus:string|null;}
export interface SalesDocumentPage {items:SalesDocumentRow[];page:number;pageSize:number;totalCount:number;}
export interface SalesLineRow {factId:string;movementType:string;occurredAt:string;productCode:string;productName:string;categoryName:string|null;quantity:number;grossAmount:number;discountAmount:number;untaxedAmount:number;taxAmount:number;totalAmount:number;recognizedCostAmount:number;returnReasonCode:string|null;returnDisposition:string|null;}
export interface SalesDocumentDetail {document:SalesDocumentRow;lines:SalesLineRow[];}
export interface CommercialVisitRow {routeVisitId:string;visitDate:string;occurredAt:string;sellerId:string;sellerName:string;routeId:string;routeName:string;zoneName:string|null;customerId:string;customerName:string;status:"Visited"|"Skipped";hasOrder:boolean;orderId:string|null;skipReason:string|null;visitObservation:string|null;}
export interface CommercialVisitPage {items:CommercialVisitRow[];page:number;pageSize:number;totalCount:number;visitedCount:number;orderedCount:number;effectivenessPercent:number;}
export interface SellerOrderReportRow {sellerId:string;sellerName:string;orderCount:number;customerCount:number;orderAmount:number;confirmedCount:number;reviewCount:number;invoicedCount:number;}
export interface SellerPerformanceRow {sellerId:string;sellerName:string;plannedVisits:number;visitedCount:number;skippedCount:number;orderCount:number;invoicedCount:number;customerCount:number;orderAmount:number;netSales:number;grossProfit:number;visitCoveragePercent:number;visitToOrderPercent:number;orderToInvoicePercent:number;}
export interface SellerPerformanceOverview {plannedVisits:number;visitedCount:number;skippedCount:number;orderCount:number;invoicedCount:number;netSales:number;grossProfit:number;visitCoveragePercent:number;visitToOrderPercent:number;orderToInvoicePercent:number;sellers:SellerPerformanceRow[];projectedThrough:string|null;}
export interface CommercialCoverageRow {sellerId:string;sellerName:string;routeId:string;routeName:string;zoneId:string|null;zoneName:string|null;plannedVisits:number;visitedCount:number;skippedCount:number;missingCount:number;orderedCount:number;operationalCoveragePercent:number;visitCoveragePercent:number;effectiveCoveragePercent:number;}
export interface CommercialCoverageOverview {plannedVisits:number;visitedCount:number;skippedCount:number;missingCount:number;orderedCount:number;operationalCoveragePercent:number;visitCoveragePercent:number;effectiveCoveragePercent:number;coverageAvailableFrom:string|null;rows:CommercialCoverageRow[];projectedThrough:string|null;}
export interface SupplierImpactRow {supplierId:string;supplierName:string;coveredCustomers:number;impactedCustomers:number;penetrationPercent:number;netSales:number;grossProfit:number;salesParticipationPercent:number;netPurchases:number;comparableNetSales:number;comparableNetPurchases:number;salesGrowthPercent:number|null;purchaseGrowthPercent:number|null;}
export interface SupplierImpactOverview {coveredCustomers:number;impactedCustomers:number;netSales:number;netPurchases:number;suppliers:SupplierImpactRow[];projectedThrough:string|null;}

const params=(filter:SalesReportFilter)=>({...filter});
export const salesReportingApi={
  today:()=>apiClient.get<SalesTodayOverview>("/commerce/v1/sales-reports/today"),
  summary:(filter:SalesReportFilter,comparison?:{from:string;to:string})=>apiClient.get<SalesReportSummary>("/commerce/v1/sales-reports/summary",{...params(filter),comparisonFrom:comparison?.from,comparisonTo:comparison?.to}),
  breakdown:(filter:SalesReportFilter,dimension:SalesDimension,limit=50)=>apiClient.get<SalesBreakdownRow[]>("/commerce/v1/sales-reports/breakdown",{...params(filter),dimension,limit}),
  documents:(filter:SalesReportFilter,page=1,pageSize=50,search?:string)=>apiClient.get<SalesDocumentPage>("/commerce/v1/sales-reports/documents",{...params(filter),page,pageSize,search}),
  document:(id:string)=>apiClient.get<SalesDocumentDetail>(`/commerce/v1/sales-reports/documents/${id}`),
  visits:(filter:{from:string;to:string;sellerId?:string;routeId?:string;status?:"Visited"|"Skipped";hasOrder?:boolean;page?:number;pageSize?:number})=>apiClient.get<CommercialVisitPage>("/commerce/v1/sales-reports/visits",filter),
  sellerOrders:(from:string,to:string)=>apiClient.get<SellerOrderReportRow[]>("/commerce/v1/sales-reports/seller-orders",{from,to}),
  sellerPerformance:(from:string,to:string)=>apiClient.get<SellerPerformanceOverview>("/commerce/v1/sales-reports/seller-performance",{from,to}),
  coverage:(from:string,to:string)=>apiClient.get<CommercialCoverageOverview>("/commerce/v1/sales-reports/coverage",{from,to}),
  supplierImpact:(from:string,to:string)=>apiClient.get<SupplierImpactOverview>("/commerce/v1/sales-reports/supplier-impact",{from,to}),
};
