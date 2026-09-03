export type PosReceiptPrintRoute = "installed-pos" | "browser" | "none";

export function resolvePosReceiptPrintRoute(
  edgeSessionToken: string | null,
  fiscalHabilitationOnly: boolean,
): PosReceiptPrintRoute {
  if (fiscalHabilitationOnly) return "none";
  return edgeSessionToken ? "installed-pos" : "browser";
}
