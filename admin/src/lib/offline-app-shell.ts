const RUNTIME_CACHE = "auraly-pwa-v13-runtime";

export async function prepareSellerAppShell() {
  if (typeof caches === "undefined") return;
  const cache = await caches.open(RUNTIME_CACHE);
  for (const path of ["/dashboard", "/dashboard/orders?view=today-route"]) {
    const request = new Request(path, { credentials: "include" });
    const response = await fetch(request, { cache: "reload" });
    if (!response.ok || new URL(response.url).pathname.startsWith("/login"))
      throw new Error("La sesión venció mientras se preparaba el acceso sin conexión.");
    await cache.put(request, response.clone());
  }
}
