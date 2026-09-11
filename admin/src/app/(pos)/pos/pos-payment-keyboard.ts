import type { PosSaleDocumentType } from "@/services/pos/pos-edge-client";

export function nextPaymentAmountIndex(
  current: number,
  count: number,
  key: string,
): number | null {
  if (key !== "ArrowUp" && key !== "ArrowDown") return null;
  const next = key === "ArrowUp" ? current - 1 : current + 1;
  return next >= 0 && next < count ? next : current;
}

export function isChangeDocumentShortcut(key: string, locked: boolean): boolean {
  return key === "F6" && !locked;
}

export function documentTypeForShortcut(key: string): PosSaleDocumentType | null {
  if (key === "F1") return "SalesInvoice";
  if (key === "F2") return "SalesReceipt";
  return null;
}
