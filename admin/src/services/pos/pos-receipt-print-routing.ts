import { resolvePosPrintRoute, type PosPrintRoute } from "./pos-print-routing";

export type PosReceiptPrintRoute = PosPrintRoute;

export function resolvePosReceiptPrintRoute(
  edgeSessionToken: string | null,
): PosReceiptPrintRoute {
  return resolvePosPrintRoute(edgeSessionToken);
}
