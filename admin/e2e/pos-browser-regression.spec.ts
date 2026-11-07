import { expect, test, type Page } from "@playwright/test";
import { realAccountingWorkspace } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";
import type { AccountingEntry } from "../src/services/api/accounting";
import type { SalesWorkspaceOption } from "../src/services/pos/online-pos-client";

test.use({ actionTimeout: 60_000, serviceWorkers: "block" });

async function openOnlinePos(page: Page, businessName: string) {
  const activeDraft = page.waitForResponse(response => new URL(response.url()).pathname.endsWith("/pos/drafts/active") && response.ok());
  await page.goto("/pos");
  const ready = page.getByText("Lista para vender", { exact: true });
  const continueButton = page.getByRole("button", { name: /(?:Entrar|Continuar) a ventas(?: online)?/ });
  await expect(page.locator("#pos-scanner").or(continueButton)).toBeVisible({ timeout: 30_000 });
  if (await continueButton.isVisible()) {
    const business = page.getByRole("combobox").filter({ hasText: "Selecciona sede" });
    if (await business.count()) {
      await business.click();
      await page.getByRole("option", { name: businessName, exact: true }).click();
    }
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
  const draft = await (await activeDraft).json() as { lines: unknown[] };
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(draft.lines.length);
  await expect(page.locator("#pos-scanner")).toBeEnabled();
  if (!draft.lines.length) await expect(ready).toBeVisible({ timeout: 30_000 });
}

test("vende un comprobante online con teclado, imprime tirilla y deja una venta nueva", async ({ page }) => {
  test.setTimeout(240_000);
  const { scopedJson, businessId } = await realAccountingWorkspace(page);
  const productName = await createRealProduct(page, false);
  const workspace = (await scopedJson<SalesWorkspaceOption[]>("GET", "/api/commerce/v1/pos/workspace/options"))
    .find(option => option.businessId === businessId && option.pointOfSaleMode === "DirectSale");
  expect(workspace).toBeTruthy();
  await openOnlinePos(page, workspace!.businessName);

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
  const addedProduct = page.waitForResponse(response => response.request().method() === "POST" && /\/pos\/drafts\/[^/]+\/items$/.test(new URL(response.url()).pathname));
  await scanner.press("Enter");
  const added = await addedProduct;
  expect(added.ok(), await added.text()).toBe(true);
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(1, { timeout: 20_000 });
  await expect(scanner).toBeEnabled();
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

  // Exercise the real writer after each kind of commercial edit; reopening must
  // show the persisted values rather than a draft kept only inside the editor.
  await page.keyboard.press("F2");
  const originalPriceText = await lineEditor.getByText(/Precio original con IVA:/).innerText();
  const referencePrice = Number(originalPriceText.replace(/[^0-9,]/g, "").replace(",", "."));
  expect(referencePrice).toBeGreaterThan(9_000);
  await lineEditor.getByLabel("Precio de venta", { exact: false }).fill("9000");
  const savePrice = page.waitForResponse(response => response.request().method() === "PUT" && /\/pos\/drafts\/[^/]+\/lines$/.test(new URL(response.url()).pathname));
  await lineEditor.getByRole("button", { name: /Aplicar cambios/ }).click();
  const priced = await savePrice;
  expect(priced.ok(), await priced.text()).toBe(true);
  expect((await priced.json()).payableAmount).toBe(18_000);
  await expect(lineEditor).toBeHidden();
  await page.keyboard.press("F2");
  await lineEditor.locator('input[data-editor-column="4"]').fill("10");
  const savePercent = page.waitForResponse(response => response.request().method() === "PUT" && /\/pos\/drafts\/[^/]+\/lines$/.test(new URL(response.url()).pathname));
  await lineEditor.getByRole("button", { name: /Aplicar cambios/ }).click();
  const percentageSaved = await savePercent;
  expect(percentageSaved.ok(), await percentageSaved.text()).toBe(true);
  expect((await percentageSaved.json()).payableAmount).toBeCloseTo(referencePrice * 2 * 0.9, 0);
  await expect(lineEditor).toBeHidden();
  await page.keyboard.press("F2");
  await expect(lineEditor.locator('input[data-editor-column="4"]')).toHaveValue("10");
  await lineEditor.locator('input[data-editor-column="3"]').fill("1000");
  await lineEditor.getByLabel("Nombre del producto en el documento").fill(`${productName} · editado`);
  const saveAmount = page.waitForResponse(response => response.request().method() === "PUT" && /\/pos\/drafts\/[^/]+\/lines$/.test(new URL(response.url()).pathname));
  await lineEditor.getByRole("button", { name: /Aplicar cambios/ }).click();
  const amountSaved = await saveAmount;
  expect(amountSaved.ok(), await amountSaved.text()).toBe(true);
  expect((await amountSaved.json()).payableAmount).toBeCloseTo(referencePrice * 2 - 1_000, 0);
  await expect(lineEditor).toBeHidden();
  await page.getByRole("button", { name: /^Pausar venta/ }).click();
  const pause = page.getByRole("dialog", { name: "Pausar venta", exact: true });
  const pauseName = `Regresión directa ${productName}`;
  await pause.getByLabel("Nombre", { exact: true }).fill(pauseName);
  await pause.getByRole("button", { name: "Guardar", exact: true }).click();
  await expect(quantity).toHaveCount(0);
  await page.locator("article").filter({ hasText: pauseName }).getByRole("button", { name: "Continuar", exact: true }).click();
  await expect(quantity).toHaveValue("2");
  await expect(scanner).toBeEnabled();
  await page.keyboard.press("F2");
  await expect(lineEditor.locator('input[data-editor-column="3"]')).toHaveValue("1.000");
  await expect(lineEditor.getByLabel("Nombre del producto en el documento")).toHaveValue(`${productName} · editado`);
  await page.keyboard.press("Escape");
  await expect(lineEditor).toBeHidden();

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
