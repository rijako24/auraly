import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

const businessId = process.env.AURALY_E2E_RESTAURANT_BUSINESS_ID;
const tableCode = process.env.AURALY_E2E_RESTAURANT_TABLE_CODE;
const productName = process.env.AURALY_E2E_RESTAURANT_PRODUCT_NAME;
const waiterSecret = process.env.AURALY_E2E_RESTAURANT_WAITER_SECRET;

test("una comanda enviada en un navegador llega al otro sin recargar", async ({ browser, page }) => {
  test.skip(!businessId || !tableCode || !productName || !waiterSecret,
    "Requiere una sede, mesa y producto dedicados a la prueba en vivo.");
  test.setTimeout(150_000);
  const sender = await browser.newPage();
  try {
    await login(page, undefined, 60_000);
    await login(sender, undefined, 60_000);
    for (const target of [page, sender]) {
      await target.addInitScript((value) =>
        localStorage.setItem("selected_business_id", value), businessId!.toLowerCase());
    }

    await page.goto("/restaurant/commands");
    await expect(page.getByText("En vivo", { exact: true })).toBeVisible({ timeout: 30_000 });
    const commandPageUrl = page.url();
    await sender.goto("/pos/restaurant");
    await expect(sender.getByRole("heading", { name: "¿A qué mesa atendemos?" }))
      .toBeVisible({ timeout: 30_000 });
    await sender.getByRole("button", { name: "Todas", exact: true }).click();
    await sender.getByRole("button", { name: new RegExp(`Mesa ${tableCode}.*(?:Abrir mesa|Ver cuenta)`) }).click();
    const waiter = sender.getByRole("dialog", { name: "Identificar mesero" });
    if (await waiter.isVisible().catch(() => false)) {
      await waiter.getByRole("button", { name: /Mesero 1/i }).click();
      await waiter.getByPlaceholder("Principal o secundaria").fill(waiterSecret!);
      await waiter.getByRole("button", { name: "Abrir mesa" }).click();
    }
    await expect(sender.getByRole("heading", { name: "Elige productos" })).toBeVisible();
    await sender.getByRole("button", { name: `Agregar ${productName}` }).click();
    const marker = `PRUEBA EN VIVO ${Date.now()}`;
    await sender.getByRole("button", { name: "Observación de la unidad 1" }).click();
    const note = sender.getByRole("dialog", { name: "Observación del producto" });
    await note.locator("textarea").fill(marker);
    await note.getByRole("button", { name: "Guardar observación" }).click();
    await sender.getByRole("button", { name: /Enviar pendientes/ }).click();

    await expect(page.getByText(marker, { exact: true })).toBeVisible({ timeout: 30_000 });
    expect(page.url()).toBe(commandPageUrl);
  } finally {
    await sender.close();
  }
});
