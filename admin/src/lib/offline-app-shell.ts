const RUNTIME_CACHE = "auraly-pwa-v14-runtime";

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
  for (const path of [...new Set(paths)]) {
    const request = new Request(path, { credentials: "include" });
    const response = await fetch(request, { cache: "reload" });
    if (!response.ok || new URL(response.url).pathname.startsWith("/login"))
      throw new Error("La sesión venció mientras se preparaba el acceso sin conexión.");
    const html = await response.clone().text();
    await cache.put(request, response.clone());
    await Promise.all(linkedStaticAssets(html).map(async (path) => {
      const assetRequest = new Request(path, { credentials: "include" });
      const assetResponse = await fetch(assetRequest, { cache: "reload" });
      if (!assetResponse.ok) throw new Error(`No fue posible preparar ${path}.`);
      await cache.put(assetRequest, assetResponse);
    }));
  }
}

export function prepareSellerAppShell() {
  return prepareAppShell(["/dashboard", "/dashboard/orders?view=today-route"]);
}

export function prepareCurrentAppShell(path: string) {
  const current = path.startsWith("/dashboard") ? path : "/dashboard";
  return prepareAppShell(["/dashboard", current]);
}
