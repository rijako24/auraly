import { expect, test, type Locator, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin2222";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(user);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 20_000 });
}

async function chooseFirst(page: Page, scope: Locator, label: string) {
  const combo = field(scope, label).getByRole("combobox");
  await expect(combo).toBeEnabled({ timeout: 20_000 });
  await combo.click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
}

async function ensureWarehouses(page: Page) {
  await page.goto("/dashboard/settings/masters");
  const warehouseResponse = page.waitForResponse(
    (response) =>
      response.request().method() === "GET" &&
      response.url().includes("/commerce/v1/inventory/warehouse-masters") &&
      response.ok(),
  );
  await page.getByRole("button", { name: /Bodegas/ }).click();
  await warehouseResponse;
  for (const name of ["Bodega E2E principal", "Bodega E2E destino"]) {
    const search = page.getByPlaceholder("Buscar bodega");
    await search.fill(name);
    if (await page.getByRole("button", { name: new RegExp(name) }).count()) continue;
    await search.fill("");
    await page.getByRole("textbox").nth(1).fill(name);
    await page.getByRole("button", { name: "Crear bodega" }).click();
    await expect(
      page.getByRole("button", { name: new RegExp(name) }).first(),
    ).toBeVisible({ timeout: 20_000 });
  }
}

async function createReason(page: Page, suffix: string) {
  const name = `Motivo ajuste E2E ${suffix}`;
  await page.goto("/dashboard/settings/masters");
  await page.getByRole("button", { name: /Motivos/ }).click();
  const form = page.getByText("Nuevo motivo", { exact: true }).locator("..").locator("..");
  await form.getByRole("combobox").first().click();
  await page.getByRole("option", { name: "Ajuste", exact: true }).click();
  await field(form, "Nombre visible").locator("input").fill(name);
  await form.getByRole("button", { name: "Crear motivo" }).click();
  await expect(page.getByText("Motivo creado", { exact: true })).toBeVisible({ timeout: 20_000 });
  const search = page.getByPlaceholder("Buscar motivo");
  await search.fill(name);
  await page.getByRole("button", { name: new RegExp(name) }).click();
  await field(page.locator("main"), "Nombre visible").locator("input").fill(`${name} editado`);
  await page.getByRole("button", { name: "Guardar cambios" }).click();
  await expect(page.getByText("Motivo actualizado", { exact: true })).toBeVisible({ timeout: 20_000 });
}


async function readTwoCatalogProducts(page: Page) {
  await page.goto("/dashboard/products");
  const rows = page.getByRole("row").filter({ has: page.getByRole("button", { name: "Editar" }) });
  await expect(rows).toHaveCount(2, { timeout: 20_000 }).catch(async () => {
    expect(await rows.count()).toBeGreaterThanOrEqual(2);
  });
  const first = rows.nth(0);
  const second = rows.nth(1);
  return {
    firstName: (await first.locator("p").first().textContent())!.trim(),
    secondName: (await second.locator("p").first().textContent())!.trim(),
    secondCode: (await second.locator("p").nth(1).textContent())!.trim(),
  };
}
async function addProduct(page: Page, searchValue: string, quantity: string, index: number) {
  const search = page.getByTestId("inventory-product-search");
  await search.fill(searchValue);
  await expect(page.getByRole("option").first()).toBeVisible({ timeout: 20_000 });
  await search.press("Enter");
  const quantityInput = page.getByTestId(`inventory-quantity-${index}`);
  await expect(quantityInput).toBeFocused({ timeout: 10_000 });
  await quantityInput.fill(quantity);
  await quantityInput.press("Enter");
  await expect(search).toBeFocused();
}

async function waitForProcessed(page: Page, expectedReason?: string) {
  const accepted = page.getByText(/fue enviado al motor/).last();
  await expect(accepted).toBeVisible({ timeout: 20_000 });
  const text = (await accepted.textContent()) ?? "";
  const documentNumber = text.match(/[A-Z]{3}\d{2}-\d{8}/)?.[0];
  expect(documentNumber).toBeTruthy();
  await page.waitForTimeout(1_500);
  await page.reload();
  await page.getByRole("tab", { name: "Historial" }).click();
  const row = page.getByRole("row").filter({ hasText: documentNumber! });
  await expect(row).toContainText("Processed", { timeout: 30_000 });
  if (expectedReason) await expect(row).toContainText(expectedReason);
}

async function beginOperation(page: Page, name: RegExp) {
  await page.goto("/dashboard/inventory");
  await page.getByRole("tab", { name: /Nueva operaci.n/ }).click();
  await page.getByRole("button", { name }).click();
}

test.describe.serial("inventario completo desde navegador", () => {
  test.setTimeout(120_000);

  test.beforeEach(async ({ page }) => login(page));

  test("crea y edita un motivo y prepara las bodegas desde Maestros", async ({ page }) => {
    await ensureWarehouses(page);
    await createReason(page, String(Date.now()).slice(-7));
  });

  test("conteo fisico busca por nombre y codigo, conserva el foco y procesa", async ({ page }) => {
    await ensureWarehouses(page);
    const products = await readTwoCatalogProducts(page);
    await beginOperation(page, /Conteo f.sico/);
    await chooseFirst(page, page.locator("main"), "Bodega");
    const initialCatalogRequest = page.waitForResponse((response) =>
      response.request().method() === "GET" &&
      response.url().includes("/commerce/v1/inventory/products") &&
      response.url().includes("pageSize=50") &&
      response.ok(),
    );
    await page.getByTestId("inventory-product-search").focus();
    await initialCatalogRequest;
    await expect(page.getByRole("option").first()).toBeVisible({ timeout: 20_000 });
    await addProduct(page, products.firstName, "4", 0);
    await addProduct(page, products.secondCode === "Sin SKU" ? products.secondName : products.secondCode, "6", 1);
    await page.getByRole("button", { name: "Preparar conteo" }).click();
    await expect(page.getByText(/saldo base qued./)).toBeVisible({ timeout: 20_000 });
    await page.getByTestId("inventory-quantity-0").fill("4");
    await page.getByTestId("inventory-quantity-1").fill("6");
    await page.getByRole("button", { name: /Confirmar conteo/ }).click();
    await waitForProcessed(page, "Conteo");
  });

  test("ajuste agrega existencia y el motor lo procesa", async ({ page }) => {
    await beginOperation(page, /^Ajuste/);
    await chooseFirst(page, page.locator("main"), "Bodega");
    await addProduct(page, "Producto regres", "1", 0);
    await page.getByLabel(/Costo de/).fill("12000");
    await page.getByRole("button", { name: /Confirmar ajuste/ }).focus();
    await page.keyboard.press("Enter");
    await waitForProcessed(page);
  });

  test("traslado mueve una cantidad entre dos bodegas", async ({ page }) => {
    await beginOperation(page, /^Traslado/);
    await chooseFirst(page, page.locator("main"), "Bodega de origen");
    await chooseFirst(page, page.locator("main"), "Bodega de destino");
    await addProduct(page, "Producto regres", "1", 0);
    await page.getByRole("button", { name: /Confirmar traslado/ }).focus();
    await page.keyboard.press("Enter");
    await waitForProcessed(page);
  });

  test("conversion consume y produce productos distintos", async ({ page }) => {
    const products = await readTwoCatalogProducts(page);
    await beginOperation(page, /Conversi.n/);
    await chooseFirst(page, page.locator("main"), "Bodega");
    await addProduct(page, products.firstName, "1", 0);
    await addProduct(page, products.secondCode === "Sin SKU" ? products.secondName : products.secondCode, "1", 1);
    const rows = page.getByRole("row").filter({ has: page.getByTestId(/inventory-quantity-/) });
    await expect(rows).toHaveCount(2);
    await page.getByRole("button", { name: /Confirmar conversi.n/ }).focus();
    await page.keyboard.press("Enter");
    await waitForProcessed(page);
  });

  test("averia retira unidades y conserva trazabilidad", async ({ page }) => {
    // Make the scenario repeatable even when the E2E database already contains
    // previous inventory runs: the same browser flow first guarantees one unit
    // in the warehouse that will receive the damage operation.
    await beginOperation(page, /^Ajuste/);
    await chooseFirst(page, page.locator("main"), "Bodega");
    await addProduct(page, "Producto regres", "1", 0);
    await page.getByLabel(/Costo de/).fill("12000");
    await page.getByRole("button", { name: /Confirmar ajuste/ }).click();
    await waitForProcessed(page);

    await beginOperation(page, /Aver.a/);
    await chooseFirst(page, page.locator("main"), "Bodega");
    await addProduct(page, "Producto regres", "1", 0);
    await page.getByRole("button", { name: /Confirmar aver.a/ }).focus();
    await page.keyboard.press("Enter");
    await waitForProcessed(page);
  });
});
