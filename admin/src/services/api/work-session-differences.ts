import { apiClient } from "./client";

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
  list: (from: string, to: string) =>
    apiClient.get<WorkSessionCashDifference[]>(
      "/commerce/v1/work-sessions/cash-differences",
      { from, to },
    ),
  listClosures: (from: string, to: string, status?: string, page=1, pageSize=50) =>
    apiClient.get<WorkSessionClosurePage>("/commerce/v1/work-sessions/closures", { from, to, status, page, pageSize }),
  reconcile: (closureId: string, request: ReconcileClosureRequest) =>
    apiClient.postIdempotent<ClosureReconciliation>(`/commerce/v1/work-sessions/closures/${closureId}/reconcile`, request, crypto.randomUUID()),
};

export interface ClosurePaymentTotal { paymentMethodCode:string; salesAmount:number; refundAmount:number; otherAmount:number; netAmount:number; countedAmount:number|null; difference:number|null; requiresCount:boolean }
export interface WorkSessionClosure { workSessionClosureId:string; workSessionId:string; businessId:string; businessName:string; warehouseId:string; warehouseName:string; userId:string; userName:string; openedAt:string; closedAt:string; salesCount:number; creditSalesCount:number; returnCount:number; totalSales:number; totalRefunds:number; netAmount:number; reconciliationStatus:"Pending"|"Partial"|"Reconciled"|"ReconciledWithDifferences"; accountingStatus:string; paymentTotals:ClosurePaymentTotal[] }
export interface WorkSessionClosurePage { items:WorkSessionClosure[]; page:number; pageSize:number; totalItems:number }
export interface ReconcileClosureRequest { lines:Array<{paymentMethodCode:string;verifiedAmount:number;isConfirmed:boolean;reasonCode:string|null}>; reclassifications:Array<{fromPaymentMethodCode:string;toPaymentMethodCode:string;amount:number}>; note:string|null }
export interface ClosureReconciliation { reconciliationId:string; workSessionClosureId:string; businessId:string; status:string; accountingStatus:string }
