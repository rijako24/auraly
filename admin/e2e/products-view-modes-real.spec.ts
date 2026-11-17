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


test("productos explora la jerarquía paginada y abre el filtro canónico sin consultas de vistas ocultas", async ({ page }) => {
  const businessId = process.env.AURALY_E2E_STORE_BUSINESS_ID;
  test.skip(!businessId, "Requiere una sede local con categorías.");
  test.setTimeout(180_000);
  await login(page, undefined, 60_000);
  await page.addInitScript(id => localStorage.setItem("selected_business_id", id), businessId!);
  let productReads = 0;
  let categoryReads = 0;
  page.on("request", request => {
    const path = new URL(request.url()).pathname;
    if (request.method() === "GET" && /\/businesses\/[^/]+\/products$/.test(path)) productReads++;
    if (request.method() === "GET" && path.endsWith("/product-categories/browse")) categoryReads++;
  });
  await page.goto("/dashboard/products");
  const explorer = page.getByRole("tablist", { name: "Explorar productos", exact: true });
  await expect(explorer.getByRole("tab", { name: "Productos", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect.poll(() => productReads).toBeGreaterThan(0);
  expect(categoryReads).toBe(0);
  const levels = ["Áreas", "Líneas", "Grupos", "Subgrupos"];
  let area: { name: string; productCategoryId: string; path: string } | undefined;
  const productReadsBeforeCategories = productReads;
  for (const [depth, label] of levels.entries()) {
    const result = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname.endsWith("/product-categories/browse") && url.searchParams.get("depth") === String(depth) && response.ok();
    });
    await explorer.getByRole("tab", { name: label, exact: true }).click();
    const payload = await (await result).json() as { items: { name: string; productCategoryId: string; path: string; depth: number }[]; pageSize: number };
    expect(payload.items.every(item => item.depth === depth)).toBe(true);
    expect(payload.items.length).toBeLessThanOrEqual(payload.pageSize);
    expect(categoryReads).toBe(depth + 1);
    if (depth === 0) area = payload.items[0];
  }
  expect(productReads).toBe(productReadsBeforeCategories);
  const views = page.getByRole("group", { name: "Vista de los resultados", exact: true });
  const categoryReadsBeforePresentation = categoryReads;
  await views.getByRole("button", { name: "Tarjetas", exact: true }).click();
  await expect(views.getByRole("button", { name: "Tarjetas", exact: true })).toHaveAttribute("aria-pressed", "true");
  await views.getByRole("button", { name: "Galería", exact: true }).click();
  await expect(views.getByRole("button", { name: "Galería", exact: true })).toHaveAttribute("aria-pressed", "true");
  expect(categoryReads).toBe(categoryReadsBeforePresentation);
  await views.getByRole("button", { name: "Tabla", exact: true }).click();
  await explorer.getByRole("tab", { name: "Áreas", exact: true }).click();
  if (area) {
    await expect(page.getByRole("cell", { name: area.name, exact: true }).first()).toBeVisible();
    const selectedArea = area;
    const products = page.waitForResponse(response => {
      const url = new URL(response.url());
      return /\/businesses\/[^/]+\/products$/.test(url.pathname) && url.searchParams.get("areaId") === selectedArea.productCategoryId && response.ok();
    });
    await page.getByRole("cell", { name: area.name, exact: true }).first().click();
    await products;
    await expect(page.getByRole("navigation", { name: "Categoría explorada" })).toContainText(area.path);
  } else {
    await expect(page.getByText("Sin resultados.", { exact: true })).toBeVisible();
    await explorer.getByRole("tab", { name: "Productos", exact: true }).click();
  }
  await expect(explorer.getByRole("tab", { name: "Productos", exact: true })).toHaveAttribute("aria-selected", "true");
  await page.screenshot({ path: "test-results/products-category-navigation.png", fullPage: true });
  await page.reload();
  await expect(explorer.getByRole("tab", { name: "Productos", exact: true })).toHaveAttribute("aria-selected", "true");
});
