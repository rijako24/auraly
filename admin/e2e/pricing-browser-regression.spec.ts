import { expect, test, type Locator, type Page } from "@playwright/test";
import { login } from "./support/auth";

function field(scope: Locator, label: RegExp | string) {
  return scope.getByText(label, { exact: typeof label === "string" }).locator("..");
}


async function chooseFirst(page: Page, scope: Locator, label: RegExp | string) {
  await field(scope, label).getByRole("combobox").click();
  await page.getByRole("option").first().click();
}

async function createProduct(page: Page, suffix: string) {
  const name = `Producto precio UI ${suffix}`;
  await page.goto("/dashboard/products");
  await page.getByRole("button", { name: "Nuevo producto" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByPlaceholder(/Nombre claro/).fill(name);
  await dialog.getByPlaceholder(/Referencia del fabricante/).fill(`PREC-${suffix}`);
  await dialog.getByPlaceholder(/Escanea o escribe un c.digo/).fill(`771${suffix}`);
  await dialog.getByPlaceholder(/Escanea o escribe un c.digo/).press("Enter");
  await chooseFirst(page, dialog, "IVA de venta *");
  await chooseFirst(page, dialog, "IVA de compra *");
  await field(dialog, "Costo base").locator("input").fill("10000");
  await field(dialog, /Margen sobre el precio antes de IVA/).locator("input").fill("20");
  await dialog.getByRole("button", { name: "Crear producto" }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });
  return name;
}

test("edita margen y precio con teclado y publica por lote desde la grilla", async ({ page }) => {
  test.setTimeout(150_000);
  await login(page);
  const suffix = String(Date.now()).slice(-9);
  const product = await createProduct(page, suffix);

  await page.goto("/dashboard/products/pricing");
  const search = page.getByPlaceholder("Producto, código o proveedor");
  await search.fill(product);
  const row = page.getByRole("row").filter({ hasText: product });
  await expect(row).toBeVisible({ timeout: 20_000 });
  const margin = row.locator('input[id^="pricing-margin-"]');
  const price = row.locator('input[id^="pricing-price-"]');
  await expect(margin).toBeEnabled();
  await margin.focus();
  await margin.fill("25");
  await margin.press("ArrowRight");
  await expect(price).toBeFocused();
  await price.fill("28000");
  await price.press("Tab");
  await expect(margin).not.toHaveValue("25");

  await row.getByRole("checkbox", { name: "Seleccionar fila" }).click();
  await expect(page.getByText("1 fila(s) seleccionada(s)")).toBeVisible();
  await page.getByRole("button", { name: /Publicar selecci.n/ }).click();
  await expect(page.getByText(/Precio publicado y notificado|precios publicados/)).toBeVisible({ timeout: 20_000 });
  // The default view lists proposals that still require action. A published row
  // leaves that queue rather than remaining as a stale editable row.
  await expect(row).toBeHidden({ timeout: 20_000 });

  await page.goto("/dashboard/products");
  await page.getByPlaceholder("Buscar por nombre o SKU").fill(product);
  const productRow = page.getByRole("row").filter({ hasText: product });
  await expect(productRow).toContainText("28.000", { timeout: 20_000 });
});
