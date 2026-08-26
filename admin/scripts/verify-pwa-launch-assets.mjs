import { access, readFile } from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";

const root = process.cwd();
const expectedScreens = [
  [750, 1334], [828, 1792], [1125, 2436], [1170, 2532],
  [1179, 2556], [1242, 2688], [1284, 2778], [1290, 2796],
];

for (const [width, height] of expectedScreens) {
  const file = path.join(root, "public", "brand", "launch", `auraly-${width}x${height}-v4.png`);
  await access(file);
  const image = sharp(file);
  const metadata = await image.metadata();
  if (metadata.width !== width || metadata.height !== height)
    throw new Error(`${path.basename(file)} has ${metadata.width}x${metadata.height}; expected ${width}x${height}.`);
  const { data, info } = await image.raw().toBuffer({ resolveWithObject: true });
  const pixel = (x, y) => Array.from(data.subarray((y * info.width + x) * info.channels, (y * info.width + x + 1) * info.channels));
  for (const [x, y] of [[0, 0], [width - 1, 0], [0, height - 1], [width - 1, height - 1]]) {
    const channels = pixel(x, y);
    if (channels.slice(0, 3).some(channel => channel < 220) || (channels[3] !== undefined && channels[3] !== 255))
      throw new Error(`${path.basename(file)} has a dark or transparent launch corner at ${x},${y}: ${channels.join(",")}.`);
  }
}

const manifest = JSON.parse(await readFile(path.join(root, "public", "app.webmanifest"), "utf8"));
if (manifest.display !== "standalone" || manifest.background_color !== "#f8fafc" || manifest.theme_color !== "#f8fafc")
  throw new Error("The app manifest must use standalone mode and the light launch background.");
for (const icon of manifest.icons) {
  const iconPath = new URL(icon.src, "https://auraly.local").pathname;
  await access(path.join(root, "public", iconPath.replace(/^\//, "")));
}

const layout = await readFile(path.join(root, "src", "app", "layout.tsx"), "utf8");
for (const [width, height] of expectedScreens) {
  if (!layout.includes(`auraly-${width}x${height}-v4.png?v=5`))
    throw new Error(`The iOS startup metadata is missing ${width}x${height}.`);
}
if (!layout.includes("#auraly-standalone-boot") ||
    !layout.includes("background:#f8fafc") ||
    !layout.includes('colorScheme: "light"'))
  throw new Error("The first standalone DOM frame must force the light launch screen.");

const worker = await readFile(path.join(root, "public", "app-sw.js"), "utf8");
if (!worker.includes('VERSION = "auraly-pwa-v14"') ||
    /APP_SHELL\s*=\s*\[[^\]]*"\/dashboard"/.test(worker))
  throw new Error("The current worker must not pre-cache an unauthenticated dashboard response.");
if (worker.includes("self.skipWaiting()") || worker.includes("self.clients.claim()"))
  throw new Error("A new worker must wait for existing tabs before replacing their Next.js asset cache.");
if (!worker.includes("!response.redirected") ||
    !worker.includes("responseUrl.pathname === url.pathname"))
  throw new Error("Authenticated navigation caches must reject redirected login responses.");
if (!worker.includes('cacheDocumentAndAssets("/login"') ||
    !worker.includes('url.pathname.startsWith("/_next/static/")'))
  throw new Error("The offline login shell must include its versioned Next.js design assets.");
const offlineShell = await readFile(path.join(root, "src", "lib", "offline-app-shell.ts"), "utf8");
if (!offlineShell.includes('RUNTIME_CACHE = "auraly-pwa-v14-runtime"'))
  throw new Error("The offline shell writer must use the active service-worker runtime cache.");
if (!offlineShell.includes("linkedStaticAssets(html)") ||
    !offlineShell.includes("await cache.put(assetRequest, assetResponse)") ||
    !offlineShell.includes("prepareCurrentAppShell"))
  throw new Error("Preparing a seller shell must persist the CSS and chunks required to render it.");
const nextStaticStrategy = worker.match(/if \(url\.pathname\.startsWith\("\/_next\/static\/"\)\) \{([\s\S]*?)\n  \}/)?.[1] ?? "";
if (!nextStaticStrategy.includes("fetch(request).then") ||
    !nextStaticStrategy.includes("caches.match(request)") ||
    nextStaticStrategy.indexOf("fetch(request).then") > nextStaticStrategy.indexOf("caches.match(request)"))
  throw new Error("Next.js chunks must use network-first caching so a deployment cannot reuse stale POS code.");

const offlineDatabase = await readFile(path.join(root, "src", "lib", "sales-offline-database.ts"), "utf8");
const sellerOfflineStore = await readFile(path.join(root, "src", "lib", "seller-order-offline-store.ts"), "utf8");
const sellerOrdersPanel = await readFile(path.join(root, "src", "components", "orders", "seller-route-orders-panel.tsx"), "utf8");
if (!offlineDatabase.includes('"seller-order-snapshots"') ||
    !sellerOfflineStore.includes("saveSellerOrderSnapshot") ||
    !sellerOfflineStore.includes("loadSellerOrderSnapshots") ||
    !sellerOrdersPanel.includes("Por sincronizar"))
  throw new Error("Seller orders must be rendered from local snapshots before server reconciliation.");

console.log(`Verified ${expectedScreens.length} opaque iOS launch screens and ${manifest.icons.length} Android PWA icons.`);
