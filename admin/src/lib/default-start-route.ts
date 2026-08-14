const sellerRoles = new Set(["seller", "vendedor"]);
const transporterRoles = new Set(["transporter", "transportador", "conductor", "driver"]);

export function defaultStartRoute(roles: readonly string[], permissions: readonly string[]): string {
  const normalizedRoles = roles.map((role) => role.trim().toLocaleLowerCase("es"));
  const isSellerOnly = normalizedRoles.length > 0 && normalizedRoles.every((role) => sellerRoles.has(role));
  const isTransporterOnly = normalizedRoles.length > 0 && normalizedRoles.every((role) => transporterRoles.has(role));
  if (isTransporterOnly && permissions.includes("dispatches.delivery.execute"))
    return "/dashboard/dispatches?view=my-deliveries";
  return isSellerOnly && permissions.includes("orders.read")
    ? "/dashboard/orders?view=today-route"
    : "/dashboard";
}

export function shouldApplyDefaultStart(pathname: string): boolean {
  return pathname === "/dashboard" || pathname === "/dashboard/";
}
