export function isOrderRecoveryWorkspace(href: string): boolean {
  const params = new URL(href).searchParams;
  return params.get("workspace") === "orders" || Boolean(params.get("recoverOrder")?.trim());
}

export function consumeOrderRecoveryUrl(href: string, preserveOrderWorkspace = false): {
  orderId: string | null;
  nextUrl: string;
} {
  const url = new URL(href);
  const orderId = url.searchParams.get("recoverOrder")?.trim() || null;
  url.searchParams.delete("recoverOrder");
  if (preserveOrderWorkspace) url.searchParams.set("workspace", "orders");
  return {
    orderId,
    nextUrl: `${url.pathname}${url.search}${url.hash}`,
  };
}
