import { apiClient } from "./client";
import type {
  PosWorkSessionClosure,
  PosWorkSessionClosurePreview,
  PosWorkSessionPaymentCount,
} from "@/services/pos/pos-edge-client";

export interface WorkSessionCashDifference {
  workSessionClosureId: string;
  workSessionId: string;
  businessId: string;
  businessName: string;
  warehouseId: string;
  warehouseName: string;
  userId: string;
  userName: string;
  closedAt: string;
  expectedCash: number;
  countedCash: number;
  difference: number;
  treatment: "SurplusIncome" | "ShortageExpense";
  accountingStatus: "Pending" | "AccountingPendingConfiguration" | "Posted" | "AccountingDisabled";
  accountingEntryId: string | null;
  accountingEntryNumber: string | null;
}

export const workSessionDifferencesApi = {
  current: () =>
    apiClient.get<ActiveWorkSession | undefined>(
      "/commerce/v1/work-sessions/current",
      undefined,
      { cache: "no-store" },
    ),
  previewCurrent: (workSessionId: string) =>
    apiClient.get<PosWorkSessionClosurePreview>(
      `/commerce/v1/work-sessions/${encodeURIComponent(workSessionId)}/closure-preview`,
      undefined,
      { cache: "no-store" },
    ),
  closeCurrent: (
    workSessionId: string,
    paymentCounts: PosWorkSessionPaymentCount[],
    note: string | null,
    idempotencyKey: string,
  ) => apiClient.postIdempotent<PosWorkSessionClosure>(
    `/commerce/v1/work-sessions/${encodeURIComponent(workSessionId)}/close`,
    {
      countedCash: paymentCounts.find(item => item.paymentMethodCode === "Cash")?.countedAmount ?? 0,
      paymentCounts,
      note,
    },
    idempotencyKey,
  ),
  list: (from: string, to: string) =>
    apiClient.get<WorkSessionCashDifference[]>(
      "/commerce/v1/work-sessions/cash-differences",
      { from, to },
    ),
  listClosures: (from: string, to: string, status?: string, page=1, pageSize=50) =>
    apiClient.get<WorkSessionClosurePage>("/commerce/v1/work-sessions/closures", { from, to, status, page, pageSize }),
  listPaymentVerifications: (closureId: string) =>
    apiClient.get<ClosurePaymentVerification[]>(`/commerce/v1/work-sessions/closures/${closureId}/payment-verifications`),
  reconcile: (closureId: string, request: ReconcileClosureRequest) =>
    apiClient.postIdempotent<ClosureReconciliation>(`/commerce/v1/work-sessions/closures/${closureId}/reconcile`, request, crypto.randomUUID()),
};

export interface ActiveWorkSession {
  workSessionId: string;
  businessId: string;
  businessName: string;
  warehouseId: string | null;
  warehouseName: string | null;
  userId: string;
  userName: string;
  deviceId: string | null;
  openedAt: string;
  lastActivityAt: string;
  status: "Open";
  tenantId: string;
}

export interface ClosurePaymentTotal { paymentMethodCode:string; salesAmount:number; refundAmount:number; otherAmount:number; netAmount:number; countedAmount:number|null; difference:number|null; requiresCount:boolean }
export interface ClosurePaymentVerification { verificationKey:string; paymentMethodCode:string; movementType:"Sale"|"Refund"|"CashIn"|"CashOut"|"SalePayment"; sourceDocumentType:"SalesInvoice"|"SalesReceipt"|"ServiceInvoice"|"SalesReturn"|"CashMovement"; sourceId:string; documentNumber:string; sourceNumber:number; amount:number; reference:string|null; cardFranchiseCode:string|null; approvalNumber:string|null; occurredAt:string; status:"Verified"|"Missing"|null }
export interface WorkSessionClosure { workSessionClosureId:string; workSessionId:string; businessId:string; businessName:string; warehouseId:string|null; warehouseName:string|null; userId:string; userName:string; openedAt:string; closedAt:string; salesCount:number; creditSalesCount:number; returnCount:number; totalSales:number; totalRefunds:number; netAmount:number; reconciliationStatus:"Pending"|"Partial"|"Reconciled"|"ReconciledWithDifferences"; accountingStatus:string; paymentTotals:ClosurePaymentTotal[] }
export interface WorkSessionClosurePage { items:WorkSessionClosure[]; page:number; pageSize:number; totalItems:number }
export interface ReconcileClosureRequest { lines:Array<{paymentMethodCode:string;verifiedAmount:number;isConfirmed:boolean;reasonCode:string|null}>; reclassifications:Array<{fromPaymentMethodCode:string;toPaymentMethodCode:string;amount:number}>; note:string|null; paymentVerifications:Array<{verificationKey:string;status:"Verified"|"Missing"}> }
export interface ClosureReconciliation { reconciliationId:string; workSessionClosureId:string; businessId:string; status:string; accountingStatus:string }
