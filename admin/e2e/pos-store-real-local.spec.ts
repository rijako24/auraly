import { expect, test, type Page } from "@playwright/test";
import { login } from "./support/auth";

const businessId = process.env.AURALY_E2E_STORE_BUSINESS_ID;
const productName = process.env.AURALY_E2E_STORE_PRODUCT_NAME;
test.use({ serviceWorkers: "block", actionTimeout: 20_000 });
test.describe.configure({ mode: "serial" });

async function openStore(page: Page) {
  await login(page, undefined, 60_000);
  await page.addInitScript(id => localStorage.setItem("selected_business_id", id), businessId!);
  await page.goto("/pos?workspace=change");
  await page.getByRole("button", { name: "Comprobante de venta", exact: true }).click();
  const activeDraft = page.waitForResponse(response => response.url().endsWith("/pos/drafts/active") && response.ok());
  await page.getByRole("button", { name: "Continuar a ventas", exact: true }).click();
  const draft = await (await activeDraft).json() as { lines: unknown[] };
  await expect(page.getByRole("region", { name: "Catálogo de tiendas" })).toBeVisible();
  const quantities = page.getByRole("spinbutton", { name: /Cantidad de/ });
  await expect(quantities).toHaveCount(draft.lines.length);
  if (await quantities.count()) {
    await page.getByText("Acciones de la venta", { exact: true }).click();
    await page.getByRole("button", { name: /^Reiniciar/ }).click();
    await page.getByRole("button", { name: "Sí, reiniciar", exact: true }).click();
    await expect(quantities).toHaveCount(0);
  }
}

async function capture(page: Page) {
  await page.getByRole("button", { name: `Agregar ${productName}`, exact: true }).click();
  await expect(page.getByRole("spinbutton", { name: `Cantidad de ${productName}`, exact: true })).toHaveValue("1");
}

test("tienda real: edita el producto y conserva talla, color y su nombre manual", async ({ page }) => {
  test.skip(!businessId || !productName, "Requiere la sede y producto de prueba locales.");
  test.setTimeout(180_000);
  await login(page, undefined, 60_000);
  let releaseImages!: () => void;
  const imagesReady = new Promise<void>(resolve => { releaseImages = resolve; });
  await page.route("**/api/businesses/*/products/*/images", async route => {
    await imagesReady;
    await route.continue();
  });
  await page.addInitScript(id => localStorage.setItem("selected_business_id", id), businessId!);
  await page.goto("/dashboard/products");
  await page.getByPlaceholder("Buscar por nombre o SKU").fill(productName!);
  const row = page.getByRole("row").filter({ hasText: productName! });
  await row.getByRole("button", { name: "Editar", exact: true }).click();
  const editor = page.getByRole("dialog");
  await expect(editor.locator("#product-name")).toHaveValue(productName!);
  await expect(editor.getByRole("combobox", { name: "Color", exact: true })).toContainText("Negro");
  await expect(editor.getByRole("combobox", { name: "Talla", exact: true })).toContainText("M");
  await editor.locator("#product-description").fill("Camiseta de prueba local: edición, talla y color verificados.");
  await expect(editor.getByText("Todavía no hay componentes configurados.", { exact: true })).toBeVisible();
  await editor.getByRole("button", { name: "Guardar producto", exact: true }).click();
  await expect(page.getByText("Espera a que termine de cargar la galería de imágenes antes de guardar el producto.", { exact: true })).toBeVisible();
  const gallery = page.waitForResponse(response => /\/products\/[^/]+\/images$/.test(new URL(response.url()).pathname) && response.ok());
  releaseImages();
  const originalImages = await (await gallery).json() as { productImageId: string }[];
  expect(originalImages.length).toBeGreaterThan(0);
  await expect(editor.locator("#product-images img").first()).toBeVisible();
  const saved = page.waitForResponse(response => response.request().method() === "PUT" && /\/commerce\/v1\/products\/[^/]+$/.test(new URL(response.url()).pathname));
  await editor.getByRole("button", { name: "Guardar producto", exact: true }).click();
  const response = await saved;
  expect(response.ok(), await response.text()).toBe(true);
  expect(response.request().postDataJSON().images.map((image: { productImageId: string }) => image.productImageId)).toEqual(originalImages.map(image => image.productImageId));
  await expect(page.getByText("Producto guardado completamente", { exact: true })).toBeVisible();
});

test("tienda real: imágenes, familia, edición de línea, pausa, recuperación, faltante, exacto, cambio y venta", async ({ page }) => {
  test.skip(!businessId || !productName, "Requiere la sede y producto de prueba locales.");
  test.setTimeout(180_000);
  await openStore(page);
  const catalog = page.getByRole("region", { name: "Catálogo de tiendas" });
  const tile = catalog.getByRole("button", { name: `Agregar ${productName}`, exact: true });
  await expect(tile.locator("img")).toBeVisible();
  await expect.poll(() => tile.locator("img").evaluate((image: HTMLImageElement) => image.complete && image.naturalWidth > 0)).toBe(true);
  await tile.locator("..").getByRole("button", { name: "Ver familia", exact: true }).click();
  await expect(tile).toBeVisible();
  await capture(page);
  await page.getByRole("button", { name: `Agregar uno de ${productName}`, exact: true }).click();
  await expect(page.getByRole("spinbutton", { name: `Cantidad de ${productName}`, exact: true })).toHaveValue("2");
  await expect(page.getByRole("button", { name: /^Cobrar/ })).toBeEnabled();
  await page.keyboard.press("F2");
  const editor = page.locator('form[aria-keyshortcuts="Enter Escape"]');
  await editor.getByLabel("Nombre del producto en el documento").fill(`${productName} · línea QA`);
  await editor.getByRole("button", { name: /Aplicar cambios/ }).click();
  await expect(editor).toBeHidden();
  await expect(page.getByRole("spinbutton", { name: `Cantidad de ${productName} · línea QA`, exact: true })).toHaveValue("2");
  await page.getByRole("button", { name: /^Pausar venta/ }).click();
  const pause = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: "Pausar venta", exact: true }) });
  const pauseName = `QA tienda ${Date.now()}`;
  await pause.getByLabel("Nombre", { exact: true }).fill(pauseName);
  await pause.getByRole("button", { name: "Guardar", exact: true }).click();
  await expect(pause).toBeHidden();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(0);
  await page.locator("article").filter({ hasText: pauseName }).getByRole("button", { name: "Continuar", exact: true }).click();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveValue("2");
  await page.getByRole("button", { name: /^Cobrar/ }).click();
  const payment = page.getByRole("dialog", { name: "Finalizar venta" });
  const cash = payment.getByRole("textbox", { name: "Valor recibido en Efectivo", exact: true });
  await cash.fill("1");
  await expect(payment.getByText(/^Faltan/)).toBeVisible();
  await expect(payment.getByRole("button", { name: "Sin imprimir", exact: true })).toBeDisabled();
  await cash.fill("99800");
  await expect(payment.getByText("Pago completo", { exact: true })).toBeVisible();
  await cash.fill("100000");
  await expect(payment.getByText(/Cambio listo:/)).toBeVisible();
  const confirmation = page.waitForResponse(response => response.request().method() === "POST" && /\/pos\/drafts\/[^/]+\/complete$/.test(new URL(response.url()).pathname));
  await payment.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const response = await confirmation;
  expect(response.ok(), await response.text()).toBe(true);
  await expect(payment).toBeHidden();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(0);
  await expect(page.getByText("Entregar al cliente", { exact: true })).toBeVisible();
  await page.screenshot({ path: "test-results/store-real-sale-completed.png", fullPage: true });
});

test("tienda real: guarda un pedido desde el borrador compartido y lo recupera para cobrar", async ({ page }) => {
  test.skip(!businessId || !productName, "Requiere la sede y producto de prueba locales.");
  test.setTimeout(180_000);
  await openStore(page);
  await capture(page);
  await page.getByRole("button", { name: /^Guardar pedido/ }).click();
  const customer = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: "Buscar cliente", exact: true }) });
  await expect(customer).toBeVisible();
  const customerRow = customer.locator("button").filter({ has: page.locator("strong") }).first();
  await expect(customerRow.or(customer.getByText("No encontramos clientes.", { exact: true }))).toBeVisible();
  const saved = page.waitForResponse(response => response.request().method() === "POST" && /\/seller-orders$/.test(new URL(response.url()).pathname));
  if (await customerRow.isVisible()) await customerRow.click();
  else {
    await customer.getByRole("button", { name: "Nuevo cliente", exact: true }).click();
    const create = page.getByRole("dialog").filter({ has: page.getByRole("heading", { name: "Crear cliente", exact: true }) });
    const field = (name: string) => create.getByText(name, { exact: true }).locator("..");
    await field("Número").locator("input").fill(String(Date.now()).slice(-10));
    await field("Nombre completo").locator("input").fill("Cliente QA Tiendas local");
    await field("Dirección").locator("input").fill("Calle de prueba 123");
    await field("País").getByRole("combobox").click();
    await page.getByRole("option", { name: "Colombia", exact: true }).click();
    await field("Departamento / estado").getByRole("combobox").click();
    await page.getByRole("option").first().click();
    await field("Ciudad").getByRole("combobox").click();
    await page.getByRole("option").first().click();
    await create.getByRole("button", { name: "Crear y seleccionar", exact: true }).click();
  }
  const response = await saved;
  expect(response.ok(), await response.text()).toBe(true);
  await expect(customer).toBeHidden();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(0);
  const order = await response.json() as { orderNumber: string };
  await page.screenshot({ path: "test-results/store-real-order-saved.png", fullPage: true });
  await page.getByRole("button", { name: `Recuperar ${order.orderNumber}`, exact: true }).click();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveValue("1");
  await expect(page.getByText("Pedido recuperado · ocupado por esta sesión", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: /^Cobrar/ }).click();
  const payment = page.getByRole("dialog", { name: "Finalizar venta" });
  await payment.getByRole("textbox", { name: "Valor recibido en Efectivo", exact: true }).fill("100000");
  const completion = page.waitForResponse(result => result.request().method() === "POST" && /\/pos\/drafts\/[^/]+\/complete$/.test(new URL(result.url()).pathname));
  await payment.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const completed = await completion;
  expect(completed.ok(), await completed.text()).toBe(true);
  await expect(payment).toBeHidden();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(0);
});
