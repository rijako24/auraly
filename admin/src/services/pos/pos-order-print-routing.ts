export type PosOrderPrintRoute = "installed-app" | "browser";

export function resolvePosOrderPrintRoute(
  edgeSessionToken: string | null,
): PosOrderPrintRoute {
  return edgeSessionToken ? "installed-app" : "browser";
}

export function orderReceiptsFromEmission<T>(
  results: ReadonlyArray<{ receipt?: T | null }>,
): T[] {
  return results.flatMap((result) => result.receipt ? [result.receipt] : []);
}
