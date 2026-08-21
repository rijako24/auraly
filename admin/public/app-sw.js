const VERSION = "auraly-pwa-v5";
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
