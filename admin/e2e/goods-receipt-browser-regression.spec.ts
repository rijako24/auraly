import { expect, test, type Locator, type Page } from "@playwright/test";
import { login } from "./support/auth";

function field(scope: Locator, label: RegExp | string) {
  return scope.getByText(label, { exact: typeof label === "string" }).locator("..");
}


async function selectFirst(page: Page, dialog: Locator, label: RegExp | string) {
  const combo = field(dialog, label).getByRole("combobox");
  await expect(combo).toBeEnabled({ timeout: 20_000 });
  await combo.click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
}

async function chooseAnotherDate(page: Page, scope: Locator, label: string) {
  const trigger = field(scope, label).getByRole("button").first();
  const before = await trigger.textContent();
  await trigger.click();
  const day = page.locator('[data-radix-popper-content-wrapper]:visible button[aria-label*=" de "]:not(:disabled)').first();
  await expect(day).toBeVisible();
  await day.click();
  await expect(trigger).not.toHaveText(before ?? "");
}

async function createReceiptProduct(page: Page) {
  const suffix = String(Date.now()).slice(-9);
  const name = `Producto recepción ${suffix}`;
  await page.goto("/dashboard/products");
  await page.getByRole("button", { name: "Nuevo producto" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByPlaceholder("Nombre claro para venta y búsqueda").fill(name);
  await dialog.getByPlaceholder("Referencia del fabricante").fill(`REC-${suffix}`);
  await dialog.getByPlaceholder("Escanea o escribe un código").fill(`779${suffix}`);
  await dialog.getByPlaceholder("Escanea o escribe un código").press("Enter");
  await selectFirst(page, dialog, "Proveedor principal *");
  await selectFirst(page, dialog, "IVA de venta *");
  await selectFirst(page, dialog, "IVA de compra *");
  await field(dialog, /^Costo base \*/).locator("input").fill("10000");
  await field(dialog, /^Margen sobre el precio antes de IVA \*/).locator("input").fill("20");
  await dialog.getByRole("button", { name: "Crear producto" }).click();
  await expect(dialog).toBeHidden({ timeout: 20_000 });
  return name;
}

test("recibe compra por UI, calcula vencimiento, conserva el foco y procesa inventario", async ({ page }) => {
  test.setTimeout(180_000);
  await login(page);
  const productName = await createReceiptProduct(page);
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  await expect(page.getByRole("heading", { name: "Recepción de compra" })).toBeVisible();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await selectFirst(page, dialog, "Proveedor");
  await selectFirst(page, dialog, "Bodega");
  const evidence = dialog.getByRole("combobox", { name: "Tipo de soporte" });
  await evidence.click();
  await page.getByRole("option", { name: "Comprobante interno" }).click();
  await expect(field(dialog, "Fecha de emisión").locator("button")).toBeEnabled();
  await chooseAnotherDate(page, dialog, "Fecha de emisión");
  await expect(field(dialog, "Fecha de vencimiento").locator("button")).toBeEnabled();
  await chooseAnotherDate(page, dialog, "Fecha de vencimiento");

  const search = dialog.getByPlaceholder(/Escanea o busca por c.digo/);
  await dialog.getByRole("button", { name: /Buscar en todo el cat.logo/ }).click();
  await search.fill(productName);
  await expect(dialog.getByRole("option", { name: new RegExp(productName) })).toBeVisible({ timeout: 20_000 });
  await search.press("Enter");

  const association = page.getByRole("dialog", { name: /Asociar producto al proveedor/ });
  if (await association.isVisible().catch(() => false)) {
    await association.getByRole("button", { name: "Asociar y agregar" }).click();
    await expect(association).toBeHidden({ timeout: 20_000 });
  }

  const quantity = dialog.getByLabel(/Cantidad en/).first();
  await expect(quantity).toBeFocused({ timeout: 20_000 });
  await dialog.getByLabel(`Costo unitario de ${productName}`).fill("10000");
  await quantity.fill("10");
  await quantity.press("Enter");
  await expect(search).toBeFocused();
  await expect(dialog.getByText(/10 .* = 10/).first()).toBeVisible();
  const notes = dialog.getByPlaceholder("Observaciones de recepción");
  await expect(notes).toHaveCSS("resize", "none");

  const confirm = dialog.getByRole("button", { name: "Confirmar entrada" });
  await confirm.focus();
  await page.keyboard.press("Enter");
  const toast = page.getByText(/fue confirmada y enviada al motor/).last();
  await expect(toast).toBeVisible({ timeout: 20_000 });
  const documentNumber = ((await toast.textContent()) ?? "").match(/[A-Z]{3}\d{2}-\d{8}/)?.[0];
  expect(documentNumber).toBeTruthy();
  await expect(dialog).toBeHidden({ timeout: 20_000 });
  const row = page.getByRole("row").filter({ hasText: documentNumber! });
  await expect(row).toContainText("Procesada", { timeout: 45_000 });
});
