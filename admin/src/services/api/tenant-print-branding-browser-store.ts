const CACHE_NAME = "auraly-print-branding-v2";

export type ConditionalBranding<T> =
  | { notModified: true }
  | { notModified: false; value: T; etag: string | null };

export async function loadTenantPrintBrandingFromBrowserStore<T extends { tenantId: string }>(
  expectedTenantId: string | null,
  fetchBranding: (etag: string | null) => Promise<ConditionalBranding<T>>,
  storage: CacheStorage | undefined,
  origin: string,
): Promise<T> {
  const key = new URL(
    `/__auraly/print-branding/${encodeURIComponent(expectedTenantId ?? "current")}`,
    origin,
  );
  let cache: Cache | null = null;
  let previous: { etag: string; branding: T } | null = null;
  try {
    cache = await storage?.open(CACHE_NAME) ?? null;
    const saved = await cache?.match(key);
    if (saved) {
      const entry = await saved.json() as { etag?: string; branding?: T };
      if (entry.etag && entry.branding &&
          (!expectedTenantId || entry.branding.tenantId.toLowerCase() === expectedTenantId.toLowerCase()))
        previous = { etag: entry.etag, branding: entry.branding };
    }
  } catch {
    // The POS still prints when private mode disallows browser disk storage.
    cache = null;
  }

  const response = await fetchBranding(previous?.etag ?? null);
  if (response.notModified) {
    if (!previous) throw new Error("La copia local del logo no está disponible.");
    return previous.branding;
  }
  const branding = response.value;
  if (expectedTenantId && branding.tenantId.toLowerCase() !== expectedTenantId.toLowerCase())
    throw new Error("La sesión de impresión cambió de empresa. Vuelve a abrir el punto de venta.");
  if (cache && response.etag) {
    try {
      await cache.put(key, new Response(JSON.stringify({ etag: response.etag, branding }), {
        headers: { "Content-Type": "application/json" },
      }));
    } catch {
      // A full browser cache must not prevent use of the logo for this visit.
    }
  }
  return branding;
}
