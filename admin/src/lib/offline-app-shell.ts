const RUNTIME_CACHE = "auraly-pwa-v14-runtime";
const ASSET_DOWNLOAD_CONCURRENCY = 4;

function linkedStaticAssets(html: string) {
  const documentValue = new DOMParser().parseFromString(html, "text/html");
  return [...documentValue.querySelectorAll<HTMLLinkElement | HTMLScriptElement>("link[href],script[src]")]
    .map((element) => new URL(element.getAttribute("href") ?? element.getAttribute("src") ?? "", window.location.origin))
    .filter((url) => url.origin === window.location.origin &&
      (url.pathname.startsWith("/_next/static/") || url.pathname.startsWith("/brand/")))
    .map((url) => url.pathname + url.search);
}

async function prepareAppShell(paths: string[]) {
  if (typeof caches === "undefined") return;
  const cache = await caches.open(RUNTIME_CACHE);
  const assets = new Set<string>();
  for (const path of [...new Set(paths)]) {
    const request = new Request(path, { credentials: "include" });
    const response = await fetch(request, { cache: "no-store" });
    if (!response.ok || new URL(response.url).pathname.startsWith("/login"))
      throw new Error("La sesión venció mientras se preparaba el acceso sin conexión.");
    const html = await response.clone().text();
    await cache.put(request, response.clone());
    for (const asset of linkedStaticAssets(html)) assets.add(asset);
  }

  const pendingAssets = [...assets];
  const downloadAsset = async () => {
    for (;;) {
      const path = pendingAssets.shift();
      if (!path) return;
      const assetRequest = new Request(path, { credentials: "include" });
      if (await cache.match(assetRequest)) continue;
      const assetResponse = await fetch(assetRequest);
      if (!assetResponse.ok) throw new Error(`No fue posible preparar ${path}.`);
      await cache.put(assetRequest, assetResponse);
    }
  };
  await Promise.all(
    Array.from(
      { length: Math.min(ASSET_DOWNLOAD_CONCURRENCY, pendingAssets.length) },
      downloadAsset,
    ),
  );
}

export function prepareSellerAppShell() {
  return prepareAppShell([
    "/dashboard",
    "/dashboard/my-routes",
    "/dashboard/orders",
  ]);
}
