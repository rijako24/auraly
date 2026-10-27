import { expect, test, type Page } from "@playwright/test";
import { realAccountingWorkspace } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";
import type { AccountingEntry } from "../src/services/api/accounting";

async function openOnlinePos(page: Page) {
  await page.goto("/pos");
  const ready = page.getByText("Lista para vender", { exact: true });
  const continueButton = page.getByRole("button", { name: /(?:Entrar|Continuar) a ventas(?: online)?/ });
  await expect(ready.or(continueButton)).toBeVisible({ timeout: 30_000 });
  if (await continueButton.isVisible()) {
    const warehouse = page.getByRole("combobox").filter({ hasText: "Selecciona bodega" });
    if (await warehouse.count()) {
      await warehouse.click();
      await page.getByRole("option").first().click();
    }
    const receipt = page.getByRole("button", { name: /Comprobante de venta/ });
    if (await receipt.count()) await receipt.click();
    await expect(continueButton).toBeEnabled({ timeout: 20_000 });
    await continueButton.click();
  }
  await expect(ready).toBeVisible({ timeout: 30_000 });
}

test("vende un comprobante online con teclado, imprime tirilla y deja una venta nueva", async ({ page }) => {
  test.setTimeout(150_000);
  const { scopedJson } = await realAccountingWorkspace(page);
  const productName = await createRealProduct(page, false);
  await openOnlinePos(page);

  // A failed previous browser run may have left the recoverable active draft intact.
  // Reset it through the UI so this regression also proves the confirmation flow.
  const existingQuantity = page.getByRole("spinbutton", { name: /Cantidad de/ });
  if (await existingQuantity.count()) {
    const labels = await existingQuantity.evaluateAll(inputs => inputs.map(input => input.getAttribute("aria-label")));
    expect(labels.every(label => label?.startsWith("Cantidad de Producto E2E ")),
      "No se reinicia una venta ajena a los datos de esta regresión local.").toBe(true);
    await page.getByRole("button", { name: /Reiniciar/ }).click();
    const confirmation = page.getByRole("alertdialog");
    await expect(confirmation).toBeVisible();
    await confirmation.getByRole("button", { name: /reiniciar/i }).click();
    await expect(existingQuantity).toHaveCount(0);
  }

  await page.keyboard.press("F1");
  const productSearch = page.getByRole("dialog").filter({ hasText: "Buscar producto" });
  await expect(productSearch).toBeVisible();
  await productSearch.getByRole("combobox", { name: "Buscar producto", exact: true }).fill(productName);
  const firstProduct = productSearch.getByRole("option").first();
  await expect(firstProduct).toContainText(productName);
  const productCode = (await firstProduct.innerText()).split(/\s+/).find((part) => part.startsWith("PRD-"));
  expect(productCode).toBeTruthy();
  await page.keyboard.press("Escape");
  await expect(productSearch).toBeHidden();

  const scanner = page.locator("#pos-scanner");
  await expect(scanner).toBeFocused();
  await scanner.fill(productCode!);
  await scanner.press("Enter");
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(1, { timeout: 20_000 });
  await scanner.press("ArrowDown");
  const quantity = page.getByRole("spinbutton", { name: /Cantidad de/ }).last();
  await expect(quantity).toBeFocused();
  await quantity.fill("2");
  await quantity.press("Enter");
  await expect(scanner).toBeFocused();

  await page.keyboard.press("F2");
  const lineEditor = page.locator('form[aria-keyshortcuts="Enter Escape"]').filter({ hasText: "Editar líneas" });
  await expect(lineEditor).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(lineEditor).toBeHidden();
  await expect(scanner).toBeFocused();

  await page.keyboard.press("F8");
  const payment = page.getByRole("dialog", { name: "Finalizar venta" });
  await expect(payment).toBeVisible();
  const cash = payment.getByRole("textbox", { name: "Valor recibido en Efectivo" });
  await cash.fill("999999");
  await expect(payment.getByText(/Cambio listo:/)).toBeVisible();

  const ticketPromise = page.waitForResponse((response) =>
    response.request().method() === "POST" &&
    response.url().includes("/pos/drafts/sales/receipts/render"));
  await payment.getByRole("button", { name: /Emitir e imprimir/ }).click();
  const ticket = await ticketPromise;
  expect(ticket.ok(), await ticket.text()).toBe(true);
  const { receipts } = ticket.request().postDataJSON() as { receipts: Array<{ documentId: string }> };
  expect(receipts).toHaveLength(1);
  const { documentId } = receipts[0];
  expect(documentId).toBeTruthy();
  const entry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${documentId}`);
  expect(entry.debitTotal).toBeGreaterThan(0);
  expect(entry.debitTotal).toBe(entry.creditTotal);

  await expect(page.getByText(/emitida e impresa/)).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Entregar al cliente", { exact: true })).toBeVisible();
  await expect(page.getByText("Cambio", { exact: true })).toBeVisible();
  await expect(scanner).toBeFocused();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(0);
});
