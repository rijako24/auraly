import { expect, test, type Locator, type Page } from "@playwright/test";

const username = process.env.AURALY_E2E_USERNAME ?? "admin2222";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

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

async function createInventoryTestProduct(page: Page) {
  const suffix = String(Date.now()).slice(-8);
  const productName = `Producto inventario ${suffix}`;
  await page.goto("/dashboard/products");
  await page.getByRole("button", { name: "Nuevo producto" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByPlaceholder(/Nombre claro para venta/).fill(productName);
  await dialog.getByPlaceholder(/Referencia del fabricante/).fill(`INV-${suffix}`);
  const barcode = dialog.getByPlaceholder(/Escanea o escribe un c/);
  await barcode.fill(`779${suffix.padStart(9, "0")}`);
  await barcode.press("Enter");

  for (const label of ["IVA de venta *", "IVA de compra *"]) {
    await field(dialog, label).getByRole("combobox").click();
    await page.getByRole("option").first().click();
  }

  await field(dialog, "Costo base").locator("input").fill("10000");
  await field(dialog, "Margen sobre el precio antes de IVA").locator("input").fill("20");
  await dialog.getByRole("button", { name: "Crear producto" }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });
  return productName;
}

async function editInventoryManagement(page: Page, productName: string, enabled: boolean) {
  await page.goto("/dashboard/products");
  const search = page.getByPlaceholder("Buscar por nombre o SKU");
  await search.fill(productName);
  const row = page.getByRole("row").filter({ hasText: productName });
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole("button", { name: "Editar" }).click();
  const dialog = page.getByRole("dialog");
  const inventorySwitch = dialog.getByText("Controla inventario", { exact: true }).locator("..").locator("..").getByRole("switch");
  await expect(inventorySwitch).toBeVisible({ timeout: 20_000 });
  if ((await inventorySwitch.isChecked()) !== enabled) await inventorySwitch.click();
  await dialog.getByRole("button", { name: "Guardar producto" }).click();
  await expect(page.getByText("Producto guardado completamente", { exact: true })).toBeVisible({ timeout: 20_000 });
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

test("editar Controla inventario controla la búsqueda de productos", async ({ page }) => {
  test.setTimeout(150_000);
  await login(page);
  const productName = await createInventoryTestProduct(page);

  try {
    await editInventoryManagement(page, productName, false);
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

    await editInventoryManagement(page, productName, true);
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
    await editInventoryManagement(page, productName, true).catch(() => undefined);
  }
});
