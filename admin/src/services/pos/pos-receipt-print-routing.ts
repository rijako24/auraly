export type PosReceiptPrintRoute = "installed-app" | "browser" | "none";

export function resolvePosReceiptPrintRoute(
  edgeSessionToken: string | null,
  fiscalHabilitationOnly: boolean,
): PosReceiptPrintRoute {
  if (fiscalHabilitationOnly) return "none";
  return edgeSessionToken ? "installed-app" : "browser";
}
