const sellerRoles = new Set(["seller", "vendedor"]);
const transporterRoles = new Set(["transporter", "transportador", "conductor", "driver"]);

const authorizedStartRoutes: ReadonlyArray<readonly [permission: string, route: string]> = [
  ["sales.reports.read", "/dashboard"],
  ["services.read", "/dashboard/services"],
  ["catalog.read", "/dashboard/products"],
  ["parties.read", "/dashboard/parties"],
  ["routes.read", "/dashboard/routes"],
  ["pricing.read", "/dashboard/products/pricing"],
  ["pricing.segments.read", "/dashboard/products/price-segments"],
  ["promotions.read", "/dashboard/promotions"],
  ["sales.create", "/pos"],
  ["inventory.read", "/dashboard/inventory"],
  ["purchasing.goods-receipts.read", "/dashboard/purchasing/goods-receipts"],
  ["purchasing.purchase-returns.read", "/dashboard/purchasing/purchase-returns"],
  ["dispatches.read", "/dashboard/dispatches"],
  ["dispatches.delivery.execute", "/dashboard/deliveries"],
  ["reservations.read", "/dashboard/reservations"],
  ["agents.read", "/dashboard/agents"],
  ["business_config.read", "/dashboard/channels"],
  ["conversations.read", "/dashboard/conversations"],
  ["leads.read", "/dashboard/leads"],
  ["campaigns.read", "/dashboard/campaigns"],
  ["accounting.read", "/dashboard/accounting"],
  ["sales.returns.read", "/dashboard/sales-returns"],
  ["sales.debit-notes.read", "/dashboard/sales-debit-notes"],
  ["work-sessions.differences.read", "/dashboard/cash-differences"],
  ["commerce.taxation.withholdings.view", "/dashboard/accounting/withholdings"],
  ["dashboard.read", "/dashboard/subscription"],
  ["orders.read", "/dashboard/orders"],
  ["payments.read", "/dashboard/payments"],
  ["payables.read", "/dashboard/payables"],
  ["receivables.read", "/dashboard/receivables"],
  ["expenses.read", "/dashboard/expenses"],
  ["tenants.read", "/dashboard/tenants"],
  ["businesses.read", "/dashboard/businesses"],
  ["roles.read", "/dashboard/roles"],
  ["audit_logs.read", "/dashboard/audit-logs"],
  ["masters.geography.read", "/dashboard/settings/masters"],
  ["fiscal.configuration.read", "/dashboard/settings/fiscal"],
];

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
  return authorizedStartRoutes.find(([permission]) => permissions.includes(permission))?.[1]
    ?? "/dashboard";
}

export function shouldApplyDefaultStart(pathname: string): boolean {
  return pathname === "/dashboard" || pathname === "/dashboard/";
}

export function shouldRestoreOperationalStart(pathname: string, target: string): boolean {
  if (shouldApplyDefaultStart(pathname)) return target !== "/dashboard";
  if (target === "/dashboard/deliveries")
    return pathname.startsWith("/dashboard/orders");
  if (target.startsWith("/dashboard/orders"))
    return pathname.startsWith("/dashboard/deliveries");
  return false;
}
