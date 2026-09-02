import { expect, test, type Locator, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(user);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 60_000 });
}

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function selectFirst(page: Page, scope: Locator, label: string) {
  const combo = field(scope, label).getByRole("combobox");
  await expect(combo).toBeEnabled({ timeout: 20_000 });
  await combo.click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
}

test.describe("cierre transversal del buscador de productos", () => {
  test.beforeEach(async ({ page }) => login(page));
  test.setTimeout(120_000);

  test("inventario cierra con Escape, clic exterior y perdida de foco", async ({ page }) => {
    await page.goto("/dashboard/inventory");
    await page.getByRole("tab", { name: /Nueva operaci.n/ }).click();
    await page.getByRole("button", { name: /^Ajuste/ }).click();
    await selectFirst(page, page.locator("main"), "Bodega");

    const search = page.getByTestId("inventory-product-search");
    const results = page.locator("#inventory-product-results");
    await search.focus();
    await expect(results).toBeVisible({ timeout: 20_000 });
    await search.press("Escape");
    await expect(results).toBeHidden();

    await search.click();
    await expect(results).toBeVisible({ timeout: 20_000 });
    await page.mouse.click(20, 20);
    await expect(results).toBeHidden();

    await search.click();
    await expect(results).toBeVisible({ timeout: 20_000 });
    await page.getByRole("tab", { name: "Historial" }).focus();
    await expect(results).toBeHidden();
  });

  test("recepcion cierra con Escape, clic exterior y perdida de foco", async ({ page }) => {
    await page.goto("/dashboard/purchasing/goods-receipts");
    await page.getByRole("button", { name: "Nueva entrada" }).click();
    const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
    await selectFirst(page, dialog, "Proveedor");
    await selectFirst(page, dialog, "Bodega");

    const search = dialog.getByPlaceholder(/Escanea o busca por c.digo/);
    const results = dialog.getByRole("listbox");
    await search.focus();
    await expect(results).toBeVisible({ timeout: 20_000 });
    await search.press("Escape");
    await expect(results).toBeHidden();

    await search.click();
    await expect(results).toBeVisible({ timeout: 20_000 });
    const box = await dialog.boundingBox();
    expect(box).not.toBeNull();
    await page.mouse.click(box!.x + 20, box!.y + 20);
    await expect(results).toBeHidden();

    await search.click();
    await expect(results).toBeVisible({ timeout: 20_000 });
    await dialog.getByRole("button", { name: "Cerrar" }).focus();
    await expect(results).toBeHidden();
  });
});
