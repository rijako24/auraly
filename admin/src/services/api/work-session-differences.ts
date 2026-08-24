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
};
