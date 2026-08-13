import { expect, test, type Locator, type Page } from "@playwright/test";

const username = process.env.AURALY_E2E_USERNAME ?? "admin2222";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";
const productName = "Aceite vegetal 3000 ml";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 20_000 });
}

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function editInventoryManagement(page: Page, enabled: boolean) {
  await page.goto("/dashboard/products");
  const search = page.getByPlaceholder("Buscar por nombre o SKU");
  await search.fill(productName);
  const row = page.getByRole("row").filter({ hasText: productName });
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole("button", { name: "Editar" }).click();
  const dialog = page.getByRole("dialog");
  const inventorySwitch = dialog.getByText("Maneja inventario", { exact: true }).locator("..").getByRole("switch");
  await expect(inventorySwitch).toBeVisible({ timeout: 20_000 });
  if ((await inventorySwitch.isChecked()) !== enabled) await inventorySwitch.click();
  await dialog.getByRole("button", { name: "Guardar producto" }).click();
  await expect(page.getByText("Producto guardado completamente", { exact: true })).toBeVisible({ timeout: 20_000 });
  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
}

async function openInventoryProductSearch(page: Page) {
  await page.goto("/dashboard/inventory");
  await page.getByRole("tab", { name: /Nueva operaci.n/ }).click();
  await page.getByRole("button", { name: /Conteo f.sico/ }).click();
  const warehouse = field(page.locator("main"), "Bodega").getByRole("combobox");
  await expect(warehouse).toBeEnabled({ timeout: 20_000 });
  await warehouse.click();
  await page.getByRole("option").first().click();
  const search = page.getByTestId("inventory-product-search");
  await expect(search).toBeEnabled();
  return search;
}

test("editar Maneja inventario controla la busqueda de productos", async ({ page }) => {
  test.setTimeout(120_000);
  await login(page);

  try {
    await editInventoryManagement(page, false);
    let search = await openInventoryProductSearch(page);
    const disabledResponse = page.waitForResponse((response) =>
      response.request().method() === "GET" &&
      response.url().includes("/commerce/v1/inventory/products") &&
      response.url().includes("search=") &&
      response.ok(),
    );
    await search.fill(productName);
    await disabledResponse;
    await expect(page.getByRole("option").filter({ hasText: productName })).toHaveCount(0);

    await editInventoryManagement(page, true);
    search = await openInventoryProductSearch(page);
    const enabledResponse = page.waitForResponse((response) =>
      response.request().method() === "GET" &&
      response.url().includes("/commerce/v1/inventory/products") &&
      response.url().includes("search=") &&
      response.ok(),
    );
    await search.fill(productName);
    await enabledResponse;
    const result = page.getByRole("option").filter({ hasText: productName });
    await expect(result).toBeVisible({ timeout: 20_000 });

    await search.press("Enter");
    const quantity = page.getByTestId("inventory-quantity-0");
    await expect(quantity).toBeFocused();
    await quantity.fill("8");
    await quantity.press("Enter");
    await expect(search).toBeFocused();

    await search.fill(productName);
    await expect(result).toBeVisible({ timeout: 20_000 });
    await search.press("Enter");
    await expect(quantity).toBeFocused();
    await expect(quantity).toHaveValue("8");
  } finally {
    await editInventoryManagement(page, true).catch(() => undefined);
  }
});
