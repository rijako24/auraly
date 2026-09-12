import { authorizedNavigationItems } from "../components/layout/sidebar-nav-config";

const sellerRoles = new Set(["seller", "vendedor"]);
const transporterRoles = new Set(["transporter", "transportador", "conductor", "driver"]);

export function isSellerOperationalProfile(roles: readonly string[], permissions: readonly string[]): boolean {
  const normalizedRoles = roles.map((role) => role.trim().toLocaleLowerCase("es"));
  return normalizedRoles.length > 0
    && normalizedRoles.every((role) => sellerRoles.has(role))
    && permissions.includes("routes.read");
}

export function defaultStartRoute(roles: readonly string[], permissions: readonly string[]): string {
  const normalizedRoles = roles.map((role) => role.trim().toLocaleLowerCase("es"));
  const isTransporterOnly = normalizedRoles.length > 0 && normalizedRoles.every((role) => transporterRoles.has(role));
  if (isTransporterOnly && permissions.includes("dispatches.delivery.execute"))
    return "/dashboard/deliveries";
  if (isSellerOperationalProfile(roles, permissions))
    return "/dashboard/my-routes";
  return authorizedNavigationItems(permissions)[0]?.href ?? "/dashboard";
}

export function requiresCloudWorkspace(permissions: readonly string[]): boolean {
  return authorizedNavigationItems(permissions).some((item) => item.href !== "/pos");
}

export function canOpenPosAdministrativeMenu(
  cloudAuthenticated: boolean,
  permissions: readonly string[],
): boolean {
  return cloudAuthenticated && requiresCloudWorkspace(permissions);
}

export function shouldRestoreOperationalStart(pathname: string, target: string): boolean {
  if (pathname === "/dashboard" || pathname === "/dashboard/") return false;
  if (target === "/dashboard/deliveries")
    return pathname.startsWith("/dashboard/my-routes");
  if (target === "/dashboard/my-routes")
    return pathname.startsWith("/dashboard/deliveries");
  return false;
}
