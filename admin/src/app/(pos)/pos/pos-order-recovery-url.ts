export function consumeOrderRecoveryUrl(href: string): {
  orderId: string | null;
  nextUrl: string;
} {
  const url = new URL(href);
  const orderId = url.searchParams.get("recoverOrder")?.trim() || null;
  url.searchParams.delete("recoverOrder");
  return {
    orderId,
    nextUrl: `${url.pathname}${url.search}${url.hash}`,
  };
}
