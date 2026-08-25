export type PosOrderPrintRoute = "installed-pos" | "browser";

export function resolvePosOrderPrintRoute(
  edgeSessionToken: string | null,
): PosOrderPrintRoute {
  return edgeSessionToken ? "installed-pos" : "browser";
}
