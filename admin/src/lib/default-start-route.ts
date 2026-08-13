const sellerRoles = new Set(["seller", "vendedor"]);

export function defaultStartRoute(roles: readonly string[], permissions: readonly string[]): string {
  const normalizedRoles = roles.map((role) => role.trim().toLocaleLowerCase("es"));
  const isSellerOnly = normalizedRoles.length > 0 && normalizedRoles.every((role) => sellerRoles.has(role));
  return isSellerOnly && permissions.includes("orders.read")
    ? "/dashboard/orders?view=today-route"
    : "/dashboard";
}

export function shouldApplyDefaultStart(pathname: string): boolean {
  return pathname === "/dashboard" || pathname === "/dashboard/";
}
