import { apiClient, withPagedDefaults } from "./client";
import { dispatchOfflineScope, assertDispatchOfflineScope, loadDispatchSnapshot, loadPendingEvidence, queueDeliveryOperation, queueExpenseOperation, removePendingEvidence, saveDispatchSnapshot, savePendingEvidence } from "@/lib/dispatch-offline-store";

export type DispatchStatus = "Draft" | "Prepared" | "InVerification" | "Verified" | "Released" | "InDelivery" | "PendingSettlement" | "SettlementProcessing" | "SettlementAttention" | "Closed" | "Cancelled";
export type DeliveryStatus = "Pending" | "Delivered" | "PartiallyDelivered" | "NotDelivered";
export interface DispatchListItem { dispatchId:string;dispatchNumber:string;scheduledDate:string;driverName:string;vehiclePlate:string|null;status:DispatchStatus;documentCount:number;lineCount:number;expectedQuantity:number;verifiedQuantity:number;shortageQuantity:number;updatedAt:string;rowVersion:string; }
export interface DispatchPage { items:DispatchListItem[];page:number;pageSize:number;totalCount:number;totalPages:number; }
export interface DispatchCandidate { documentId:string;documentType:"SalesInvoice"|"SalesReceipt";documentNumber:string;issuedAt:string;warehouseId:string;warehouseName:string;customerId:string|null;customerName:string;deliveryAddress:string|null;sellerName:string;lineCount:number;pendingQuantity:number;documentTotal:number; }
export interface DispatchCandidatePage { items:DispatchCandidate[];page:number;pageSize:number;totalCount:number;totalPages:number; }
export interface DispatchDocument { dispatchSourceDocumentId:string;sourceDocumentId:string;documentType:string;documentNumber:string;customerId:string|null;customerName:string;deliveryAddress:string|null;sellerName:string;documentTotal:number;status:string;warehouseId:string;warehouseName:string; }
export interface DispatchLine { dispatchLineId:string;dispatchSourceDocumentId:string;sourceLineNumber:number;productId:string;productCode:string;description:string;assignedQuantity:number;verifiedQuantity:number;shortageQuantity:number;status:string;rowVersion:string; }
export interface DispatchDetailQuery { documentPage:number;documentPageSize:number;linePage:number;linePageSize:number; }
export interface DispatchDetail { dispatchId:string;businessId:string;dispatchNumber:string;scheduledDate:string;driverName:string;vehiclePlate:string|null;routeId:string|null;routeName:string|null;notes:string|null;status:DispatchStatus;documents:DispatchDocument[];lines:DispatchLine[];summary:DispatchMutation;pagination:DispatchDetailQuery;shortages:Array<{dispatchShortageId:string;dispatchLineId:string;productId:string;productCode:string;description:string;quantity:number;reason:string;notes:string|null;createdAt:string}>;createdAt:string;updatedAt:string;rowVersion:string; }
export interface DispatchMutation { dispatchId:string;dispatchNumber:string;status:DispatchStatus;documentCount:number;lineCount:number;expectedQuantity:number;verifiedQuantity:number;shortageQuantity:number;rowVersion:string;updatedLine?:DispatchLine|null;pendingLineCount:number; }
export interface DispatchReportRow { dispatchNumber:string;scheduledDate:string;status:string;driverName:string;vehiclePlate:string|null;documentType:string;documentNumber:string;customerName:string;deliveryAddress:string|null;sellerName:string;productCode:string;productName:string;assignedQuantity:number;verifiedQuantity:number;shortageQuantity:number;unitPrice:number|null;lineTotal:number|null; }
export interface DispatchReport { title:string;generatedAt:string;includesPrices:boolean;rows:DispatchReportRow[]; }
export interface DeliveryPayment { paymentId:string;applicationType:"InvoicePayment"|"CreditDocument"|"CreditAdvance";paymentMethod:"Cash"|"Deposit"|"Transfer"|"DebitCard"|"CreditCard"|null;amount:number;reference:string|null;evidenceUrl:string|null;evidenceReadUrl?:string|null;paymentMethodId?:string|null;paymentMethodVersion?:number|null;formName?:string|null;cardFranchiseCode?:string|null;approvalNumber?:string|null;verifiedBy?:string|null;verifiedAt?:string|null;originalPaymentMethod?:string|null;originalFormName?:string|null;correctionReason?:string|null;correctedBy?:string|null;correctedAt?:string|null; }
export interface DispatchReasonOption { dispatchReasonId:string;reasonType:"NotDelivered"|"DeliveryReturn";code:string;name:string; }
export interface DeliveryReturn { returnLineId:string;originalLineNumber:number;productId:string;productCode:string;description:string;quantity:number;inventoryDisposition:"Sellable"|"NotReturned";reasonCode:string;reasonDescription:string; }
export interface DeliveryProductLine { originalLineNumber:number;productId:string;productCode:string;description:string;quantity:number;unitPrice:number;lineTotal:number;returnedQuantity?:number;returnedLineTotal?:number; }
export interface DeliveryDocument { dispatchSourceDocumentId:string;sourceDocumentId:string;documentType:string;documentNumber:string;sequence:number;customerId:string|null;customerName:string;deliveryAddress:string|null;documentTotal:number;creditAmount:number;destinationLatitude:number|null;destinationLongitude:number|null;deliveryStatus:DeliveryStatus;reason:string|null;notes:string|null;latitude:number|null;longitude:number|null;deliveredAt:string|null;lines:DeliveryProductLine[];payments:DeliveryPayment[];returns:DeliveryReturn[];collectionMode?:"Session"|"Dispatch";returnTotal?:number;returnCalculation?:{originalUnrounded:number;originalRounding:number;unroundedOutstanding:number;remainingRounding:number}|null; }
export interface DispatchExpense { expenseId:string;category:string;amount:number;description:string|null;evidenceUrl:string|null;evidenceReadUrl?:string|null;approvalStatus:"Pending"|"Approved"|"Rejected";approvedAmount:number|null;conceptId?:string|null;acceptedExpenseId?:string|null;occurredAt?:string|null;acceptedExpenseStatus?:string|null; }
export interface SettlementSummary { grossCash:number;approvedCashExpenses:number;expectedCash:number;declaredCash:number;difference:number;depositTotal:number;remainingCreditTotal:number;creditAdvanceTotal:number;returnTotal:number;undeliveredTotal:number;dispatchTotal:number;balanceDifference:number;status:string;receivedBy:string|null;receivedAt:string|null;cashReceived?:number|null;cardTotal?:number;declaredCard?:number|null;declaredTransfer?:number|null;transporterClosedBy?:string|null;transporterClosedAt?:string|null;previouslyCollectedTotal?:number; }
export interface DispatchLinkedReturn {returnId:string;originalDocumentId:string;documentNumber:string;totalAmount:number;economicResolution:string;}
export interface DispatchExecution { dispatchId:string;dispatchNumber:string;scheduledDate:string;driverName:string;vehiclePlate:string|null;status:DispatchStatus;documents:DeliveryDocument[];expenses:DispatchExpense[];settlement:SettlementSummary|null;rowVersion?:string|null;linkedReturns?:DispatchLinkedReturn[]; }
export interface DeliveryResultInput { dispatchSourceDocumentId:string;deliveryStatus:Exclude<DeliveryStatus,"Pending">;reason:string|null;notes:string|null;latitude:number|null;longitude:number|null;occurredAt:string;idempotencyKey:string;payments:Array<{applicationType:"InvoicePayment"|"CreditDocument"|"CreditAdvance";paymentMethod:"Cash"|"Deposit"|"Transfer"|"DebitCard"|"CreditCard"|null;amount:number;reference:string|null;evidenceUrl:string|null;paymentMethodId?:string;paymentMethodVersion?:number;cardFranchiseCode?:string|null;approvalNumber?:string|null}>;returns:Array<{originalLineNumber:number;quantity:number;inventoryDisposition:"Sellable"|"NotReturned";reasonCode:string;reasonDescription:string}>; }

export interface DispatchPaymentReviewResult {dispatchId:string;dispatchSourceDocumentId:string;payment:DeliveryPayment;grossCash:number;expectedCash:number;cashDifference:number;transferTotal:number;cardTotal:number;}
export interface DispatchPaymentCorrection {paymentMethodId:string;paymentMethodVersion:number;methodCode:string;reference:string|null;cardFranchiseCode:string|null;approvalNumber:string|null;reason:string;expectedCorrectedAt:string|null;}

async function localEvidence(values:(string|null)[]) {
  return loadPendingEvidence([...new Set(values.filter((value):value is string=>Boolean(value?.startsWith("local:"))))]);
}
async function cachedExecution(id:string) {
  if(typeof navigator!=="undefined"&&!navigator.onLine) {
    const value=await loadDispatchSnapshot(id);
    if(value)return value;
    throw new Error("Prepara el despacho con conexión antes de trabajar sin señal.");
  }
  const scope=dispatchOfflineScope();
  const value=await apiClient.get<DispatchExecution>(`/commerce/v1/dispatches/${id}/execution`);
  assertDispatchOfflineScope(scope);
  await saveDispatchSnapshot(value);
  return value;
}
async function rememberedExecution(request:Promise<DispatchExecution>) {
  const scope=dispatchOfflineScope();
  const value=await request;
  assertDispatchOfflineScope(scope);
  await saveDispatchSnapshot(value);
  return value;
}
async function offlineEvidence(file:File) { const placeholder=`local:${crypto.randomUUID()}`; await savePendingEvidence(placeholder,file); return {url:placeholder}; }
export interface DispatchExpenseInput {category:string;amount:number;description:string|null;evidenceUrl:string|null;idempotencyKey:string;occurredAt:string;conceptId?:string|null;}

// Outbox replay must reach the server or fail. It cannot use the capture
// fallback, whose optimistic local result is not a server acknowledgement.
export function sendDispatchEvidence(id:string,file:File) {
  const body=new FormData();body.append("file",file);
  return apiClient.postForm<{url:string}>(`/commerce/v1/dispatches/${id}/evidence`,body);
}
export function sendDispatchDelivery(id:string,request:DeliveryResultInput) {
  return rememberedExecution(apiClient.put<DispatchExecution>(`/commerce/v1/dispatches/${id}/delivery-results`,request));
}
export function sendDispatchExpense(id:string,request:DispatchExpenseInput) {
  return rememberedExecution(apiClient.post<DispatchExecution>(`/commerce/v1/dispatches/${id}/expenses`,request));
}
async function offlineDelivery(id:string,request:DeliveryResultInput) { if(typeof navigator==="undefined"||navigator.onLine)return sendDispatchDelivery(id,request); const evidence=await localEvidence(request.payments.map(value=>value.evidenceUrl)); const updated=await queueDeliveryOperation(id,request,evidence); await Promise.all(evidence.map(value=>removePendingEvidence(value.placeholder))); return updated; }
async function offlineExpense(id:string,request:DispatchExpenseInput) { if(typeof navigator==="undefined"||navigator.onLine)return sendDispatchExpense(id,request); const evidence=await localEvidence([request.evidenceUrl]); const updated=await queueExpenseOperation(id,request,evidence); await Promise.all(evidence.map(value=>removePendingEvidence(value.placeholder))); return updated; }

export const dispatchesApi = {
  page: (params:{page?:number;pageSize?:number;search?:string;status?:string;from?:string;to?:string;mine?:boolean;orderBy?:string}) => apiClient.get<DispatchPage>("/commerce/v1/dispatches",withPagedDefaults(params)),
  routeOptions: (params:{page:number;pageSize:number;search?:string}) => apiClient.get<import("./routes").SalesRoutePage>("/commerce/v1/dispatches/options",params),
  reasons: (type:"NotDelivered"|"DeliveryReturn") => apiClient.get<DispatchReasonOption[]>("/commerce/v1/dispatches/delivery-reasons",{type}),
  candidates: (params:{page?:number;pageSize?:number;search?:string;documentType?:string;from?:string;to?:string;warehouseId?:string;dateRangeVersion?:number;routeId?:string}) => apiClient.get<DispatchCandidatePage>("/commerce/v1/dispatches/candidates",withPagedDefaults(params)),
  detail: (id:string,query:DispatchDetailQuery) => apiClient.get<DispatchDetail>(`/commerce/v1/dispatches/${id}`,{...query}),
  execution: cachedExecution,
  reviewExecution: (id:string) => apiClient.get<DispatchExecution>(`/commerce/v1/dispatches/${id}/execution`,{review:true},{cache:"no-store"}),
  report: (id:string,includePrices:boolean) => apiClient.get<DispatchReport>(`/commerce/v1/dispatches/${id}/report`,{includePrices}),
  create: (request:{businessId:string;scheduledDate:string;driverName:string;vehiclePlate:string|null;driverCarrierId:string|null;notes:string|null;sourceDocumentIds:string[]}) => apiClient.post<DispatchMutation>("/commerce/v1/dispatches",request),
  addDocuments: (id:string,sourceDocumentIds:string[],rowVersion:string) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/documents`,{sourceDocumentIds,rowVersion}),
  removeDocument: (id:string,sourceDocumentId:string,rowVersion:string) => apiClient.delete<DispatchMutation>(`/commerce/v1/dispatches/${id}/documents/${sourceDocumentId}?rowVersion=${encodeURIComponent(rowVersion)}`),
  transition: (id:string,action:string,rowVersion:string) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/${action}`,{rowVersion,idempotencyKey:crypto.randomUUID()}),
  verify: (id:string,dispatchLineId:string,quantityDelta:number,barcode:string|null,idempotencyKey=crypto.randomUUID()) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/verification-events`,{dispatchLineId,quantityDelta,barcode,idempotencyKey,occurredAt:new Date().toISOString()}),
  shortage: (id:string,request:{dispatchLineId:string;quantity:number;reason:string;notes:string|null;rowVersion:string}) => apiClient.post<DispatchMutation>(`/commerce/v1/dispatches/${id}/shortages`,{...request,idempotencyKey:crypto.randomUUID()}),
  uploadEvidence: (id:string,file:File) => typeof navigator!=="undefined"&&!navigator.onLine ? offlineEvidence(file) : sendDispatchEvidence(id,file),
  recordDelivery: offlineDelivery,
  reorder: (id:string,orderedDocumentIds:string[],rowVersion:string) => rememberedExecution(apiClient.put<DispatchExecution>(`/commerce/v1/dispatches/${id}/delivery-order`,{orderedDocumentIds,rowVersion})),
  addExpense: offlineExpense,
  reviewExpense: (id:string,expenseId:string,request:{decision:"Approved"|"Rejected";approvedAmount:number|null;notes:string|null;idempotencyKey:string}) => rememberedExecution(apiClient.put<DispatchExecution>(`/commerce/v1/dispatches/${id}/expenses/${expenseId}/review`,request)),
  verifyPayment: (id:string,paymentId:string,isVerified:boolean,expectedCorrectedAt:string|null) => apiClient.put<DispatchPaymentReviewResult>(`/commerce/v1/dispatches/${id}/payments/${paymentId}/verification`,{isVerified,expectedCorrectedAt}),
  correctPayment: (id:string,paymentId:string,request:DispatchPaymentCorrection) => apiClient.put<DispatchPaymentReviewResult>(`/commerce/v1/dispatches/${id}/payments/${paymentId}/correction`,request),
  closeRoute: (id:string,request:{declaredCash:number;declaredCard:number;declaredTransfer:number;idempotencyKey:string}) => rememberedExecution(apiClient.post<DispatchExecution>(`/commerce/v1/dispatches/${id}/close-route`,request)),
  closureReceipt: (id:string,companyName?:string) => apiClient.post<{html:string}>(`/commerce/v1/dispatches/${id}/closure-receipt`,{companyName,paperWidthMillimeters:80}),
  settle: (id:string,cashReceived:number,notes:string|null,idempotencyKey:string,cashDifferencePaymentMethodId?:string,returnIds?:string[]) =>
    rememberedExecution(apiClient.post<DispatchExecution>(`/commerce/v1/dispatches/${id}/settle`,{cashReceived,notes,idempotencyKey,cashDifferencePaymentMethodId,returnIds})),
};
