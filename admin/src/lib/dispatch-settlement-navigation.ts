import type { DispatchStatus } from "@/services/api/dispatches";

export function shouldCloseDispatchAfterSettlement(status: DispatchStatus) {
  return status === "SettlementProcessing"
    || status === "SettlementAttention"
    || status === "Closed";
}
