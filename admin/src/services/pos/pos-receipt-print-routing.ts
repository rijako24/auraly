export type PosReceiptPrintRoute = "installed-app" | "browser";

export function resolvePosReceiptPrintRoute(
  edgeSessionToken: string | null,
): PosReceiptPrintRoute {
  return edgeSessionToken ? "installed-app" : "browser";
}
