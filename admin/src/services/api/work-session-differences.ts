import { apiClient } from "./client";
import type {
  PosWorkSessionClosure,
  PosWorkSessionClosurePreview,
  PosWorkSessionPaymentCount,
  WorkSessionInvoiceCharge,
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
  invoiceCharges: (closureId: string, page = 1, paymentMethodCode?: string) =>
    apiClient.get<ClosureInvoiceChargePage>(`/commerce/v1/work-sessions/closures/${closureId}/invoice-charges/page`,
      { page, pageSize: 50, paymentMethodCode }),
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
  closureReceipt: (
    workSessionId: string,
    companyName?: string | null,
    companyLogoSource?: string | null,
  ) => apiClient.post<{ html: string }>(
    `/commerce/v1/work-sessions/${encodeURIComponent(workSessionId)}/closure-receipt`,
    { companyName, companyLogoSource, paperWidthMillimeters: 80 },
  ),
  list: (from: string, to: string) =>
    apiClient.get<WorkSessionCashDifference[]>(
      "/commerce/v1/work-sessions/cash-differences",
      { from, to },
    ),
  listClosures: (from: string, to: string, status?: string, page=1, pageSize=50) =>
    apiClient.get<WorkSessionClosurePage>("/commerce/v1/work-sessions/closures", { from, to, status, page, pageSize }),
  listPaymentVerifications: (closureId: string, page = 1, pageSize = 100,
    paymentMethodCode?: string, movementType?: string) =>
    apiClient.get<ClosurePaymentVerificationPage>(`/commerce/v1/work-sessions/closures/${closureId}/payment-verifications/page`,
      { page, pageSize, paymentMethodCode, movementType }),
  reconcile: (closureId: string, request: ReconcileClosureRequest) =>
    apiClient.postIdempotent<ClosureReconciliation>(`/commerce/v1/work-sessions/closures/${closureId}/reconcile`, request, crypto.randomUUID()),
  validatePaymentCorrections: (closureId: string, corrections: ClosurePaymentCorrection[]) =>
    apiClient.post<{ valid: boolean }>(`/commerce/v1/work-sessions/closures/${closureId}/payment-corrections/validate`, corrections),
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

export interface ClosurePaymentTotal { paymentMethodCode:string; salesAmount:number; refundAmount:number; otherAmount:number; netAmount:number; countedAmount:number|null; difference:number|null; reconciledDifference?:number|null; requiresCount:boolean }
export interface ClosureInvoiceChargePage {
  items: WorkSessionInvoiceCharge[]; page: number; pageSize: number; totalItems: number; totalAmount: number;
  closureSummary: { count: number; invoicedAmount: number; expenseAmount: number;
    paymentMethods: Array<{ paymentMethodCode: string; count: number; amount: number }> };
}
export interface ClosureManualMovement { movementId:string; paymentMethodCode:string; movementType:"Sale"|"Refund"|"ReceivablePayment"|"PayablePayment"|"CashIn"|"CashOut"; amount:number; observation:string }
export interface ClosureCashShortageClaim { receivableId:string; employeePartyId:string; amount:number }
export interface ClosurePaymentVerification { verificationKey:string; paymentMethodCode:string; movementType:"Sale"|"Refund"|"CashIn"|"CashOut"|"SalePayment"|"CreditSale"|"ReceivablePayment"|"PayablePayment"; sourceDocumentType:"SalesInvoice"|"SalesReceipt"|"ServiceInvoice"|"SalesReturn"|"CashMovement"|"ManualAccountingVoucher"; sourceId:string; documentNumber:string; sourceNumber:number; amount:number; reference:string|null; cardFranchiseCode:string|null; approvalNumber:string|null; occurredAt:string; customerName:string|null; counterpartyName?:string|null; reasonName?:string|null; notes?:string|null; status:"Verified"|"Missing"|null; correctedPaymentMethodCode?:string|null; correctedAmount?:number|null; correctionReason?:string|null; tenderMethodCode?:string|null; correctedTenderMethodCode?:string|null; correctedCardFranchiseCode?:string|null; correctedApprovalNumber?:string|null; correctedReference?:string|null }
export interface ClosurePaymentVerificationGroup { paymentMethodCode:string; movementType:string; count:number; totalAmount:number; reviewedCount?:number; reviewedAmount?:number; effectiveCount?:number; effectiveTotalAmount?:number; effectiveReviewedCount?:number; effectiveReviewedAmount?:number }
export interface ClosurePaymentVerificationPage { items:ClosurePaymentVerification[]; groups:ClosurePaymentVerificationGroup[]; page:number; pageSize:number; totalItems:number; manualMovements?:ClosureManualMovement[]; cashShortageClaim?:ClosureCashShortageClaim|null; reconciliationNote?:string|null }
export interface WorkSessionClosure { workSessionClosureId:string; workSessionId:string; businessId:string; businessName:string; warehouseId:string|null; warehouseName:string|null; userId:string; userName:string; openedAt:string; closedAt:string; salesCount:number; creditSalesCount:number; returnCount:number; totalSales:number; totalRefunds:number; netAmount:number; reconciliationStatus:"Pending"|"Partial"|"Reconciled"|"ReconciledWithDifferences"; accountingStatus:string; paymentTotals:ClosurePaymentTotal[]; expectedCash?:number }
export interface WorkSessionClosurePage { items:WorkSessionClosure[]; page:number; pageSize:number; totalItems:number }
export interface ClosurePaymentCorrection { verificationKey:string; paymentMethodCode:string; amount:number; reason:string; tenderMethodCode?:string|null; cardFranchiseCode?:string|null; approvalNumber?:string|null; reference?:string|null }
export interface ReconcileClosureRequest { lines:Array<{paymentMethodCode:string;verifiedAmount:number;isConfirmed:boolean;reasonCode:string|null}>; reclassifications:Array<{fromPaymentMethodCode:string;toPaymentMethodCode:string;amount:number}>; note:string|null; paymentVerifications:Array<{verificationKey:string;status:"Verified"|"Missing"}>; paymentCorrections?:ClosurePaymentCorrection[]; manualMovements?:ClosureManualMovement[]; chargeCashShortageToEmployee?:boolean }
export interface ClosureReconciliation { reconciliationId:string; workSessionClosureId:string; businessId:string; status:string; accountingStatus:string }
