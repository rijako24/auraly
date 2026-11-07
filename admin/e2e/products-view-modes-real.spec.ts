import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

test.use({ serviceWorkers: "block", actionTimeout: 60_000 });

test("productos: lista, tarjetas y galería reutilizan la página y conservan la preferencia", async ({ page }) => {
  const businessId = process.env.AURALY_E2E_STORE_BUSINESS_ID;
  test.skip(!businessId, "Requiere una sede de prueba con más de veinte productos.");
  test.setTimeout(180_000);
  await login(page, undefined, 60_000);
  await page.addInitScript(id => localStorage.setItem("selected_business_id", id), businessId!);
  const reads: string[] = [];
  let galleryReads = 0;
  page.on("request", request => {
    const url = new URL(request.url());
    if (request.method() === "GET" && /\/businesses\/[^/]+\/products$/.test(url.pathname)) reads.push(url.search);
    if (request.method() === "GET" && /\/products\/[^/]+\/images$/.test(url.pathname)) galleryReads++;
  });
  const started = Date.now();
  await page.goto("/dashboard/products");
  const views = page.getByRole("group", { name: "Vista de los resultados", exact: true });
  await expect(page.getByRole("row")).toHaveCount(21);
  await test.info().attach("primera-pagina-ms", { body: String(Date.now() - started), contentType: "text/plain" });
  await expect(page.getByRole("button", { name: /Columnas/ })).toHaveCount(0);
  const firstPageReads = reads.length;
  await views.getByRole("button", { name: "Tarjetas", exact: true }).click();
  await expect(views.getByRole("button", { name: "Tarjetas", exact: true })).toHaveAttribute("aria-pressed", "true");
  await expect(page.getByRole("row")).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Siguiente página", exact: true })).toBeEnabled();
  await views.getByRole("button", { name: "Galería", exact: true }).click();
  const gallery = page.getByLabel("Galería de resultados", { exact: true });
  await expect(gallery.getByRole("button")).toHaveCount(20);
  const images = gallery.locator("img");
  await expect.poll(() => images.count()).toBeGreaterThan(0);
  await expect.poll(() => images.evaluateAll(elements => elements.filter(element => {
    const rect = element.getBoundingClientRect();
    return rect.top < innerHeight && rect.bottom > 0;
  }).every(element => (element as HTMLImageElement).complete && (element as HTMLImageElement).naturalWidth > 0))).toBe(true);
  expect(reads.length).toBe(firstPageReads);
  expect(galleryReads).toBe(0);
  await page.screenshot({ path: "test-results/products-gallery.png", fullPage: true });
  const nextPage = page.waitForResponse(response => {
    const url = new URL(response.url());
    return /\/businesses\/[^/]+\/products$/.test(url.pathname) && url.searchParams.get("page") === "2" && response.ok();
  });
  await page.getByRole("button", { name: "Siguiente página", exact: true }).click();
  const next = await (await nextPage).json() as { items: unknown[] };
  await expect(gallery.getByRole("button")).toHaveCount(next.items.length);
  expect(reads.length).toBe(firstPageReads + 1);
  expect(galleryReads).toBe(0);
  await page.reload();
  await expect(views.getByRole("button", { name: "Galería", exact: true })).toHaveAttribute("aria-pressed", "true");
  await expect(gallery.getByRole("button")).toHaveCount(20);
  await views.getByRole("button", { name: "Tabla", exact: true }).click();
  await expect(page.getByRole("row")).toHaveCount(21);
  await page.reload();
  await expect(views.getByRole("button", { name: "Tabla", exact: true })).toHaveAttribute("aria-pressed", "true");
});
