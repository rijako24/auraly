import { apiClient, withPagedDefaults } from "./client";

export type DispatchStatus = "Draft" | "Prepared" | "InVerification" | "Verified" | "Released" | "Cancelled";
export interface DispatchListItem { dispatchId:string;dispatchNumber:string;scheduledDate:string;driverName:string;vehiclePlate:string|null;status:DispatchStatus;documentCount:number;lineCount:number;expectedQuantity:number;verifiedQuantity:number;shortageQuantity:number;updatedAt:string;rowVersion:string; }
export interface DispatchPage { items:DispatchListItem[];page:number;pageSize:number;totalCount:number;totalPages:number; }
export interface DispatchOptions { warehouses:Array<{warehouseId:string;code:string;name:string}>;routes:Array<{routeId:string;code:string;name:string;sellerName:string}>; }
export interface DispatchCandidate { documentId:string;documentType:"SalesInvoice"|"SalesReceipt";documentNumber:string;issuedAt:string;warehouseId:string;warehouseName:string;customerId:string|null;customerName:string;deliveryAddress:string|null;sellerName:string;lineCount:number;pendingQuantity:number;documentTotal:number; }
export interface DispatchCandidatePage { items:DispatchCandidate[];page:number;pageSize:number;totalCount:number;totalPages:number; }
export interface DispatchDocument { dispatchSourceDocumentId:string;sourceDocumentId:string;documentType:string;documentNumber:string;customerId:string|null;customerName:string;deliveryAddress:string|null;sellerName:string;documentTotal:number;status:string; }
export interface DispatchLine { dispatchLineId:string;dispatchSourceDocumentId:string;sourceLineNumber:number;productId:string;productCode:string;description:string;assignedQuantity:number;verifiedQuantity:number;shortageQuantity:number;status:string;rowVersion:string; }
export interface DispatchDetail { dispatchId:string;businessId:string;warehouseId:string;warehouseName:string;dispatchNumber:string;scheduledDate:string;driverName:string;vehiclePlate:string|null;routeId:string|null;routeName:string|null;notes:string|null;status:DispatchStatus;documents:DispatchDocument[];lines:DispatchLine[];shortages:Array<{dispatchShortageId:string;dispatchLineId:string;productId:string;productCode:string;description:string;quantity:number;reason:string;notes:string|null;createdAt:string}>;createdAt:string;updatedAt:string;rowVersion:string; }
export interface DispatchMutation { dispatchId:string;dispatchNumber:string;status:DispatchStatus;documentCount:number;lineCount:number;expectedQuantity:number;verifiedQuantity:number;shortageQuantity:number;rowVersion:string; }
export interface DispatchReportRow { dispatchNumber:string;scheduledDate:string;status:string;driverName:string;vehiclePlate:string|null;documentType:string;documentNumber:string;customerName:string;deliveryAddress:string|null;sellerName:string;productCode:string;productName:string;assignedQuantity:number;verifiedQuantity:number;shortageQuantity:number;unitPrice:number|null;lineTotal:number|null; }
export interface DispatchReport { title:string;generatedAt:string;includesPrices:boolean;rows:DispatchReportRow[]; }

export const dispatchesApi = {
  page: (params:{page?:number;pageSize?:number;search?:string;status?:string;from?:string;to?:string}) =>
    apiClient.get<DispatchPage>("/commerce/v1/dispatches",withPagedDefaults(params)),
  options: () => apiClient.get<DispatchOptions>("/commerce/v1/dispatches/options"),
  candidates: (params:{page?:number;pageSize?:number;search?:string;documentType?:string;from?:string;to?:string;warehouseId?:string}) =>
    apiClient.get<DispatchCandidatePage>("/commerce/v1/dispatches/candidates",withPagedDefaults(params)),
  detail: (id:string) => apiClient.get<DispatchDetail>(`/commerce/v1/dispatches/${id}`),
  report: (id:string,includePrices:boolean) => apiClient.get<DispatchReport>(`/commerce/v1/dispatches/${id}/report`,{includePrices}),
  create: (request:{businessId:string;warehouseId:string;scheduledDate:string;driverName:string;vehiclePlate:string|null;routeId:string|null;notes:string|null;sourceDocumentIds:string[]}) =>
    apiClient.post<DispatchMutation>("/commerce/v1/dispatches",request),
  addDocuments: (id:string,sourceDocumentIds:string[],rowVersion:string) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/documents`,{sourceDocumentIds,rowVersion}),
  removeDocument: (id:string,sourceDocumentId:string,rowVersion:string) => apiClient.delete<DispatchMutation>(`/commerce/v1/dispatches/${id}/documents/${sourceDocumentId}?rowVersion=${encodeURIComponent(rowVersion)}`),
  transition: (id:string,action:string,rowVersion:string) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/${action}`,{rowVersion,idempotencyKey:crypto.randomUUID()}),
  verify: (id:string,dispatchLineId:string,quantityDelta:number,barcode:string|null,idempotencyKey=crypto.randomUUID()) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/verification-events`,{dispatchLineId,quantityDelta,barcode,idempotencyKey,occurredAt:new Date().toISOString()}),
  shortage: (id:string,request:{dispatchLineId:string;quantity:number;reason:string;notes:string|null;rowVersion:string}) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/shortages`,{...request,idempotencyKey:crypto.randomUUID()}),
};
