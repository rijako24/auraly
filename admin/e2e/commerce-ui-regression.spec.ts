import { expect, test, type Locator, type Page } from "@playwright/test";
import { login } from "./support/auth";

function field(scope: Locator, label: string): Locator {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function chooseFirst(scope: Locator, label: string, page: Page) {
  await field(scope, label).getByRole("combobox").click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible();
  await option.click();
}

async function createProduct(page: Page, suffix: string) {
  const name = `Producto regresión ${suffix}`;
  const reference = `REF-${suffix}`;
  const barcode = `770${suffix.replace(/\D/g, "").slice(-9).padStart(9, "0")}`;
  await page.goto("/dashboard/products");
  await page.getByRole("button", { name: "Nuevo producto" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByPlaceholder("Nombre claro para venta y búsqueda").fill(name);
  await dialog.getByPlaceholder("Referencia del fabricante").fill(reference);
  await dialog.getByPlaceholder("Escanea o escribe un código").fill(barcode);
  await dialog.getByPlaceholder("Escanea o escribe un código").press("Enter");
  await chooseFirst(dialog, "Proveedor principal *", page);
  await chooseFirst(dialog, "IVA de venta *", page);
  await chooseFirst(dialog, "IVA de compra *", page);
  await field(dialog, "Costo base *").locator("input").fill("10000");
  await field(dialog, "Margen sobre el precio antes de IVA *").locator("input").fill("20");
  await dialog.getByRole("button", { name: "Crear producto" }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });

  const search = page.getByPlaceholder("Buscar por nombre o SKU");
  await search.fill(name);
  const row = page.getByRole("row").filter({ hasText: name });
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole("button", { name: "Editar" }).click();
  const edit = page.getByRole("dialog");
  await expect(edit.getByText("Cargando identidad comercial del producto…")).toBeHidden({ timeout: 20_000 });
  await edit.locator("#product-name").fill(`${name} editado`);
  const updating = page.waitForResponse(response => response.request().method() === "PUT" &&
    /\/api\/commerce\/v1\/products\/[0-9a-f-]{36}$/i.test(response.url()));
  await edit.getByRole("button", { name: "Guardar producto" }).click();
  const updated = await updating;
  expect(updated.ok(), await updated.text()).toBe(true);
  await expect(edit).toBeHidden({ timeout: 20_000 });
  return { name: `${name} editado`, reference, barcode };
}

async function createAndEditParty(page: Page, role: "Cliente" | "Proveedor" | "Vendedor" | "Transportador", suffix: string) {
  const name = `${role} regresión ${suffix}`;
  await page.goto("/dashboard/parties");
  await page.getByRole("button", { name: "Nuevo tercero" }).click();
  let dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: new RegExp(`^${role}`) }).click();
  await field(dialog, "Identificación").locator("input").fill(`10${suffix.replace(/\D/g, "").slice(-8).padStart(8, "0")}`);
  await field(dialog, "Nombre visible").locator("input").fill(name);
  await field(dialog, "Nombres").locator("input").fill(role);
  await field(dialog, "Apellidos").locator("input").fill(`Regresión ${suffix}`);
  await field(dialog, "Teléfono").locator("input").fill("3001234567");
  await field(dialog, "Correo").locator("input").fill(`${role.toLowerCase()}-${suffix}@example.test`);
  await chooseFirst(dialog, "País", page);
  await chooseFirst(dialog, "Departamento", page);
  await chooseFirst(dialog, "Ciudad", page);
  await field(dialog, "Dirección").locator("input").fill("Calle 1 # 2-3");
  if (role === "Vendedor") await field(dialog, "Comisión %").locator("input").fill("5");
  await dialog.getByRole("button", { name: "Guardar tercero" }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });

  const search = page.getByPlaceholder(/Nombre, documento, correo o/);
  await search.fill(name);
  const row = page.getByRole("row").filter({ hasText: name });
  await expect(row).toBeVisible({ timeout: 20_000 });
  await row.getByRole("button", { name: "Editar" }).click();
  dialog = page.getByRole("dialog");
  await field(dialog, "Teléfono").locator("input").fill("3017654321");
  await dialog.getByRole("button", { name: "Guardar tercero" }).click();
  await expect(dialog.getByText("3017654321", { exact: true })).toBeVisible({ timeout: 20_000 });
  await dialog.getByRole("button", { name: "Cerrar" }).click();
  return name;
}


async function ensureWarehouses(page: Page) {
  await page.goto("/dashboard/settings/masters");
  await page.getByRole("button", { name: /Bodegas/ }).click();
  const names = ["Bodega E2E principal", "Bodega E2E destino"];
  for (const name of names) {
    await page.getByPlaceholder("Buscar bodega").fill(name);
    const existingWarehouse = page.getByRole("button", { name: new RegExp(name) }).first();
    await page.waitForTimeout(400);
    if (await existingWarehouse.count()) {
      await expect(existingWarehouse).toBeVisible();
      continue;
    }
    await page.getByPlaceholder("Buscar bodega").fill("");
    await page.getByRole("textbox").nth(1).fill(name);
    await page.getByRole("button", { name: "Crear bodega" }).click();
    await expect(page.getByText(name, { exact: true }).first()).toBeVisible({ timeout: 20_000 });
  }
}

async function selectInventoryProduct(operation: Locator, searchValue: string, quantity: string, index: number) {
  const search = operation.getByRole("textbox", { name: "Buscar producto" });
  await search.fill(searchValue);
  const option = operation.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
  const quantityInput = operation.getByRole("textbox", { name: /^Contar / }).nth(index);
  await expect(quantityInput).toBeFocused();
  await quantityInput.fill(quantity);
  await quantityInput.press("Enter");
  await expect(search).toBeFocused();
}

test.describe.serial("regresión comercial desde navegador", () => {
  test.setTimeout(240_000);

  test("crea y edita productos y todos los terceros", async ({ page }) => {
    await login(page);
    const suffix = String(Date.now()).slice(-8);
    await createProduct(page, `${suffix}1`);
    await createProduct(page, `${suffix}2`);
    for (const role of ["Cliente", "Proveedor", "Vendedor", "Transportador"] as const) {
      await createAndEditParty(page, role, `${suffix}${role.length}`);
    }
  });

  test("captura inventario con teclado, motivos maestros y procesa con el motor", async ({ page }) => {
    await login(page);
    const suffix = String(Date.now()).slice(-8);
    const firstProduct = await createProduct(page, `${suffix}3`);
    const secondProduct = await createProduct(page, `${suffix}4`);
    await ensureWarehouses(page);
    await page.goto("/dashboard/inventory");
    await page.getByRole("button", { name: "Nueva operación" }).click();
    const operation = page.getByRole("dialog", { name: "Nueva operación" });
    await operation.getByRole("combobox", { name: "Motivo" }).click();
    await page.getByRole("option", { name: "Conteo físico" }).click();
    await chooseFirst(operation, "Bodega", page);
    await chooseFirst(operation, "Motivo", page);
    await selectInventoryProduct(operation, firstProduct.name, "4", 0);
    await selectInventoryProduct(operation, secondProduct.reference, "6", 1);
    await operation.getByRole("button", { name: "Aplicar inventario" }).click();
    await expect(page.getByText(/fue enviado para aplicar/)).toBeVisible({ timeout: 20_000 });
    await page.waitForTimeout(1_500);
    await page.reload();
    await page.getByRole("tab", { name: "Operaciones" }).click();
    await expect(page.getByText("Procesado", { exact: true }).first()).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText("Conteo físico", { exact: false }).first()).toBeVisible();
  });
});
