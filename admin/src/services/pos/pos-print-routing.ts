export type PosPrintRoute = "installed-app" | "browser";

export function resolvePosPrintRoute(
  edgeSessionToken: string | null,
): PosPrintRoute {
  return edgeSessionToken ? "installed-app" : "browser";
}

export function resolveSalePrintEffect(wasAlreadyIssued: boolean) {
  return {
    dispatchCopy: !wasAlreadyIssued,
    openCashDrawer: !wasAlreadyIssued,
  } as const;
}
