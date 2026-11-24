import { expect, test } from "@playwright/test";
import { realAccountingContext, selectRealCashForm, waitForRealPosting } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";
import type { CatalogProductDetail } from "../src/services/api/products";
import type { AccountingEntry } from "../src/services/api/accounting";

test.use({ serviceWorkers: "block", actionTimeout: 40_000 });

test("DEV mini: vende producto genérico desde la interfaz sin vendedor explícito", async ({ page }, info) => {
  test.skip(process.env.AURALY_E2E_TARGET !== "dev");
  test.setTimeout(180_000);
  const context = await realAccountingContext(page);
  const created = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/products"));
  const name = await createRealProduct(page, false, true);
  const product = await (await created).json() as CatalogProductDetail;
  await page.goto("/pos?workspace=change");
  await page.getByRole("button", { name: "Comprobante de venta", exact: true }).click();
  await page.getByRole("button", { name: "Continuar a ventas", exact: true }).click();
  const priorLines = page.getByRole("spinbutton", { name: /^Cantidad de / });
  if (await priorLines.count()) {
    // A failed test may leave its own synthetic sale. Never reset another draft.
    for (const line of await priorLines.all()) await expect(line).toHaveAccessibleName(/^Cantidad de Producto E2E [a-f0-9]{8}$/);
    await page.getByRole("button", { name: "Reiniciar F4", exact: true }).click();
    await page.getByRole("button", { name: "Sí, reiniciar", exact: true }).click();
    await expect(priorLines).toHaveCount(0);
  }
  const scanner = page.locator("#pos-scanner");
  await scanner.fill(product.productCode); await scanner.press("Enter");
  const price = page.getByRole("dialog", { name: "Define el valor del producto genérico", exact: true });
  await price.getByRole("textbox", { name: "Precio de venta · IVA incluido", exact: true }).fill("10000");
  await price.getByRole("button", { name: "Agregar · Enter", exact: true }).click();
  await expect(page.getByRole("spinbutton", { name: `Cantidad de ${name}`, exact: true })).toHaveValue("1");
  await expect(price).toBeHidden();
  await page.getByRole("button", { name: "Cobrar F8", exact: true }).click();
  const checkout = page.getByRole("dialog", { name: "Finalizar venta", exact: true });
  await selectRealCashForm(page, checkout);
  const completed = page.waitForResponse(r => r.request().method() === "POST" && /\/drafts\/[^/]+\/complete$/.test(new URL(r.url()).pathname));
  await checkout.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const response = await completed; expect(response.ok(), await response.text()).toBe(true);
  const result = await response.json(); const id = result.receipt.documentId;
  await waitForRealPosting(context, id);
  const entry = await context.scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${id}`);
  expect(entry.debitTotal).toBeGreaterThan(0); expect(entry.debitTotal).toBe(entry.creditTotal);
  await expect(checkout).toBeHidden();
  await page.screenshot({ path: info.outputPath("generic-sale-dev.png") });
  await info.attach("generic-sale", { body: JSON.stringify({ documentId: id, productId: product.productId, debit: entry.debitTotal, credit: entry.creditTotal }), contentType: "application/json" });
});

test("DEV mini: registra movimiento de mercancía desde la interfaz y conserva su saldo", async ({ page }, info) => {
  test.skip(process.env.AURALY_E2E_TARGET !== "dev");
  test.setTimeout(180_000);
  const context = await realAccountingContext(page);
  const created = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/products"));
  await createRealProduct(page);
  const product = await (await created).json() as CatalogProductDetail;
  await page.goto("/dashboard/inventory");
  await page.getByRole("button", { name: "Nueva operación", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva operación", exact: true });
  await dialog.getByRole("button", { name: /Movimientos de mercancía/ }).click();
  const warehouse = dialog.getByText("Bodega", { exact: true }).locator("..").getByRole("combobox");
  await warehouse.click(); await page.getByRole("option").first().click();
  await dialog.getByTestId("inventory-reason-select").click(); await page.getByRole("option").first().click();
  await dialog.locator("#inventory-product-search").fill(product.productCode);
  await dialog.getByRole("option").filter({ hasText: product.productCode }).click();
  await dialog.getByTestId("inventory-quantity-0").fill("5");
  const confirmed = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/inventory-adjustments/confirm"));
  await dialog.getByRole("button", { name: "Confirmar movimientos de mercancía", exact: true }).click();
  const response = await confirmed; expect(response.ok(), await response.text()).toBe(true);
  const command = response.request().postDataJSON();
  await expect(dialog).toBeHidden();
  const path = `/api/commerce/v1/inventory/balances?warehouseId=${command.warehouseId}&productId=${product.productId}&page=1&pageSize=10`;
  await expect.poll(async () => (await context.scopedJson<{ items: Array<{ quantityOnHand: number }> }>("GET", path)).items[0]?.quantityOnHand,
    { timeout: 30_000, intervals: [500, 1000, 2000] }).toBe(5);
  const replay = await context.scopedResponse("POST", new URL(response.url()).pathname, command,
    { "Idempotency-Key": response.request().headers()["idempotency-key"] });
  expect(replay.ok, JSON.stringify(replay)).toBe(true);
  expect((await context.scopedJson<{ items: Array<{ quantityOnHand: number }> }>("GET", path)).items[0]?.quantityOnHand).toBe(5);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: info.outputPath("inventory-movement-mobile-dev.png"), fullPage: true });
});
