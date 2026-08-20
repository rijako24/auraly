const sellerRoles = new Set(["seller", "vendedor"]);
const transporterRoles = new Set(["transporter", "transportador", "conductor", "driver"]);

export function isSellerOperationalProfile(roles: readonly string[], permissions: readonly string[]): boolean {
  const normalizedRoles = roles.map((role) => role.trim().toLocaleLowerCase("es"));
  return normalizedRoles.length > 0
    && normalizedRoles.every((role) => sellerRoles.has(role))
    && permissions.includes("orders.read");
}

export function ordersLandingView(
  search: string,
  roles: readonly string[],
  permissions: readonly string[],
): "today-route" | "all" {
  const requested = new URLSearchParams(search).get("view");
  if (requested === "all") return "all";
  return requested === "today-route" || isSellerOperationalProfile(roles, permissions)
    ? "today-route"
    : "all";
}

export function defaultStartRoute(roles: readonly string[], permissions: readonly string[]): string {
  const normalizedRoles = roles.map((role) => role.trim().toLocaleLowerCase("es"));
  const isTransporterOnly = normalizedRoles.length > 0 && normalizedRoles.every((role) => transporterRoles.has(role));
  if (isTransporterOnly && permissions.includes("dispatches.delivery.execute"))
    return "/dashboard/dispatches?view=my-deliveries";
  return isSellerOperationalProfile(roles, permissions)
    ? "/dashboard/orders?view=today-route"
    : "/dashboard";
}

export function shouldApplyDefaultStart(pathname: string): boolean {
  return pathname === "/dashboard" || pathname === "/dashboard/";
}
