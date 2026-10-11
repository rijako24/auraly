import { chromium, expect, test } from "@playwright/test";
import { login } from "./support/auth";

const businessId = process.env.AURALY_E2E_RESTAURANT_BUSINESS_ID;
const tableCode = process.env.AURALY_E2E_RESTAURANT_TABLE_CODE;
const productName = process.env.AURALY_E2E_RESTAURANT_PRODUCT_NAME;
const waiterSecret = process.env.AURALY_E2E_RESTAURANT_WAITER_SECRET;
const kdsUsername = process.env.AURALY_E2E_KDS_USERNAME;
const kdsPassword = process.env.AURALY_E2E_KDS_PASSWORD;

test("una comanda enviada en un navegador llega al otro sin recargar", async ({ page }) => {
  test.skip(!businessId || !tableCode || !productName || !waiterSecret ||
    !kdsUsername || !kdsPassword,
    "Requiere una sede de prueba y dos usuarios distintos para mantener ambas sesiones abiertas.");
  test.setTimeout(150_000);
  const senderBrowser = await chromium.launch({
    channel: "msedge", headless: process.env.AURALY_E2E_HEADED !== "1",
  });
  const sender = await senderBrowser.newPage({
    baseURL: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000",
  });
  const frames: string[] = [];
  const socketEvents: string[] = [];
  let cardFetches = 0;
  const cardResponses: Array<{ status: number; count: number; message?: string }> = [];
  page.on("websocket", (socket) => {
    socketEvents.push("created");
    socket.on("framereceived", (event) => frames.push(String(event.payload).slice(0, 500)));
    socket.on("close", () => socketEvents.push("closed"));
    socket.on("socketerror", () => socketEvents.push("error"));
  });
  page.on("response", (response) => {
    if (response.url().includes("/restaurant/command-cards?") &&
        response.request().method() === "GET") {
      cardFetches += 1;
      void response.json().then((body) => cardResponses.push({
        status: response.status(), count: body?.items?.length ?? -1,
      })).catch((error) => cardResponses.push({
        status: response.status(), count: -1, message: String(error),
      }));
    }
  });
  try {
    await login(page, {
      tenantKey: process.env.AURALY_E2E_TENANT_KEY ?? "",
      username: kdsUsername!,
      password: kdsPassword!,
    }, 60_000);
    await login(sender, undefined, 60_000);
    for (const target of [page, sender]) {
      await target.addInitScript((value) =>
        localStorage.setItem("selected_business_id", value), businessId!.toLowerCase());
    }

    await page.goto("/restaurant/commands");
    await expect(page.getByText("En vivo", { exact: true })).toBeVisible({ timeout: 30_000 });
    await page.getByRole("combobox").click();
    await page.getByPlaceholder("Escribe el número o nombre de mesa").fill(tableCode!);
    await page.getByRole("option", { name: new RegExp(tableCode!) }).click();
    const commandPageUrl = page.url();
    await sender.goto("/pos/restaurant");
    await expect(sender.getByRole("heading", { name: "¿A qué mesa atendemos?" }))
      .toBeVisible({ timeout: 30_000 });
    await sender.getByRole("button", { name: "Todas", exact: true }).click();
    await sender.getByText(tableCode!, { exact: true }).click();
    const waiter = sender.getByRole("dialog", { name: "Identificar mesero" });
    if (await waiter.isVisible().catch(() => false)) {
      await waiter.getByRole("button", { name: /Mesero 1/i }).click();
      await waiter.getByPlaceholder("Principal o secundaria").fill(waiterSecret!);
      await waiter.getByRole("button", { name: "Abrir mesa" }).click();
    }
    try {
      await expect(sender.getByRole("heading", { name: "Elige productos" })).toBeVisible();
    } catch (error) {
      console.log("Restaurant sender diagnostics", {
        url: sender.url(),
        text: (await sender.locator("body").innerText()).slice(0, 1800),
      });
      await sender.screenshot({ path: "../tmp/restaurant-command-sender-failed.png" });
      throw error;
    }
    await sender.getByPlaceholder("Buscar producto").fill(productName!);
    await sender.getByRole("button", { name: `Agregar ${productName}` }).click();
    const marker = `PRUEBA EN VIVO ${Date.now()}`;
    const pendingNote = sender.getByRole("button", { name: /Observación de la unidad/ }).last();
    await expect(pendingNote).toBeVisible({ timeout: 10_000 });
    await pendingNote.click();
    const note = sender.getByRole("dialog", { name: "Observación del producto" });
    await note.locator("textarea").fill(marker);
    await note.getByRole("button", { name: "Guardar observación" }).click();
    await sender.getByRole("button", { name: /Enviar pendientes/ }).click();

    try {
      await expect(page.getByText(marker, { exact: true })).toBeVisible({ timeout: 30_000 });
    } catch (error) {
      console.log("KDS live diagnostics", {
        cardFetches, cardResponses, socketEvents,
        visibility: await page.evaluate(() => document.visibilityState), frames: frames.slice(-8),
      });
      throw error;
    }
    expect(page.url()).toBe(commandPageUrl);
  } finally {
    await senderBrowser.close();
  }
});
