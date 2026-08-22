const VERSION = "auraly-pwa-v6";
const SHELL_CACHE = `${VERSION}-shell`;
const RUNTIME_CACHE = `${VERSION}-runtime`;
const APP_SHELL = ["/login", "/app.webmanifest", "/brand/auraly-app-icon-192-v4.png", "/brand/auraly-app-icon-512-v4.png", "/brand/auraly-ios-icon-512-v4.png", "/brand/auraly-maskable-512-v4.png"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(SHELL_CACHE).then((cache) => cache.addAll(APP_SHELL)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(caches.keys().then((keys) => Promise.all(
    keys.filter((key) => key.startsWith("auraly-pwa-") && ![SHELL_CACHE, RUNTIME_CACHE].includes(key))
      .map((key) => caches.delete(key)),
  )));
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== "GET" || url.origin !== self.location.origin || url.pathname.startsWith("/api/")) return;

  if (request.mode === "navigate") {
    event.respondWith(fetch(request).then((response) => {
      if (response.ok) void caches.open(RUNTIME_CACHE).then((cache) => cache.put(request, response.clone()));
      return response;
    }).catch(async () => (await caches.match(request)) ?? (await caches.match("/dashboard")) ?? (await caches.match("/login"))));
    return;
  }

  if (url.pathname.startsWith("/_next/static/") || url.pathname.startsWith("/brand/")) {
    event.respondWith(caches.match(request).then((cached) => cached ?? fetch(request).then((response) => {
      if (response.ok) void caches.open(RUNTIME_CACHE).then((cache) => cache.put(request, response.clone()));
      return response;
    })));
  }
});

self.addEventListener("push", (event) => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch { data = {}; }
  event.waitUntil(self.registration.showNotification(data.title || "Auraly · autorización POS", {
    body: data.body || "Hay una solicitud de autorización pendiente.",
    tag: data.tag || "auraly-pos-approval",
    icon: "/brand/auraly-app-icon-192-v4.png",
    badge: "/brand/auraly-app-icon-192-v4.png",
    requireInteraction: true,
    data: { url: data.url || "/dashboard?posApproval=pending" },
    actions: [{ action: "open", title: "Revisar" }],
  }));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const targetUrl = new URL(event.notification.data?.url || "/dashboard?posApproval=pending", self.location.origin).href;
  event.waitUntil(self.clients.matchAll({ type: "window", includeUncontrolled: true }).then(async (clients) => {
    const existing = clients.find((client) => new URL(client.url).origin === self.location.origin);
    if (existing) { await existing.navigate(targetUrl); return existing.focus(); }
    return self.clients.openWindow(targetUrl);
  }));
});
