import { expect, test, type Page } from "@playwright/test";
import { login as sharedLogin } from "./support/auth";

async function login(page: Page) {
  await sharedLogin(page, undefined, 60_000);
}

async function openOnlinePos(page: Page) {
  await page.goto("/pos");
  const setup = page.getByRole("heading", { name: /Prepara (?:tu espacio de venta|facturación)/ });
  if (await setup.isVisible({ timeout: 20_000 }).catch(() => false)) {
    const receipt = page.getByRole("button", { name: /Comprobante de venta/ });
    if (await receipt.count()) await receipt.click();
    const continueButton = page.getByRole("button", { name: "Entrar a ventas online" });
    await expect(continueButton).toBeEnabled({ timeout: 20_000 });
    await continueButton.click();
  }
  await expect(page.getByText("Lista para vender", { exact: true })).toBeVisible({ timeout: 30_000 });
}

test("vende un comprobante online con teclado, imprime tirilla y deja una venta nueva", async ({ page }) => {
  test.setTimeout(150_000);
  await login(page);
  await openOnlinePos(page);

  // A failed previous browser run may have left the recoverable active draft intact.
  // Reset it through the UI so this regression also proves the confirmation flow.
  const existingQuantity = page.getByRole("spinbutton", { name: /Cantidad de/ });
  if (await existingQuantity.count()) {
    await page.getByRole("button", { name: /Reiniciar/ }).click();
    const confirmation = page.getByRole("alertdialog");
    await expect(confirmation).toBeVisible();
    await confirmation.getByRole("button", { name: /reiniciar/i }).click();
    await expect(existingQuantity).toHaveCount(0);
  }

  await page.keyboard.press("F2");
  const productSearch = page.getByRole("dialog").filter({ hasText: "Buscar producto" });
  await expect(productSearch).toBeVisible();
  const firstProduct = productSearch.getByRole("option").first();
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

  await page.keyboard.press("F3");
  const lineEditor = page.locator('form[aria-keyshortcuts="Enter Escape"]').filter({ hasText: "Editar líneas" });
  await expect(lineEditor).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(lineEditor).toBeHidden();
  await expect(scanner).toBeFocused();

  await page.keyboard.press("F1");
  const payment = page.getByRole("dialog", { name: "Finalizar venta" });
  await expect(payment).toBeVisible();
  const cash = payment.getByRole("textbox", { name: "Valor recibido en Efectivo" });
  await cash.fill("999999");
  await expect(payment.getByText(/Cambio listo:/)).toBeVisible();

  const previewPromise = page.waitForEvent("popup");
  await payment.getByRole("button", { name: /Emitir e imprimir/ }).click();
  const preview = await previewPromise;
  await preview.waitForLoadState("domcontentloaded");
  await expect(preview.locator("body")).toContainText(/comprobante/i, { timeout: 20_000 });
  await preview.close();

  await expect(page.getByText(/emitida e impresa/)).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Entregar al cliente", { exact: true })).toBeVisible();
  await expect(page.getByText("Cambio", { exact: true })).toBeVisible();
  await expect(scanner).toBeFocused();
  await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(0);
});
