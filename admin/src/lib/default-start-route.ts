import { authorizedNavigationItems } from "../components/layout/sidebar-nav-config";

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
    return "/dashboard/deliveries";
  if (isSellerOperationalProfile(roles, permissions))
    return "/dashboard/orders?view=today-route";
  return authorizedNavigationItems(permissions)[0]?.href ?? "/dashboard";
}

export function shouldRestoreOperationalStart(pathname: string, target: string): boolean {
  if (pathname === "/dashboard" || pathname === "/dashboard/") return false;
  if (target === "/dashboard/deliveries")
    return pathname.startsWith("/dashboard/orders");
  if (target.startsWith("/dashboard/orders"))
    return pathname.startsWith("/dashboard/deliveries");
  return false;
}
