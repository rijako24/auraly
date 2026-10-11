import { devices, expect, test, webkit } from "@playwright/test";
import { login } from "./support/auth";

const businessId = process.env.AURALY_E2E_RESTAURANT_BUSINESS_ID;
const username = process.env.AURALY_E2E_KDS_USERNAME;
const password = process.env.AURALY_E2E_KDS_PASSWORD;

test("las comandas conectan con la sede seleccionada", async ({ page }) => {
  test.skip(!businessId || !username || !password, "Requiere usuario de comandas de desarrollo.");
  const negotiations: number[] = [];
  page.on("response", (response) => {
    if (response.url().includes("/restaurant/command-cards/synchronization/negotiate"))
      negotiations.push(response.status());
  });
  await login(page, { tenantKey: process.env.AURALY_E2E_TENANT_KEY ?? "", username: username!, password: password! }, 60_000);
  await page.addInitScript((id) => localStorage.setItem("selected_business_id", id), businessId!.toLowerCase());
  await page.goto("/restaurant/commands");
  await expect(page.getByText("En vivo", { exact: true })).toBeVisible({ timeout: 30_000 });
  expect(negotiations).toContain(200);
});

test("las comandas conectan en WebKit móvil", async () => {
  test.skip(!businessId || !username || !password, "Requiere usuario de comandas de desarrollo.");
  test.setTimeout(90_000);
  const browser = await webkit.launch({ channel: "webkit" });
  try {
    const context = await browser.newContext({
      ...devices["iPhone 13"],
      baseURL: process.env.AURALY_E2E_BASE_URL,
    });
    const page = await context.newPage();
    const negotiations: number[] = [];
    const sockets: string[] = [];
    page.on("response", (response) => {
      if (response.url().includes("/restaurant/command-cards/synchronization/negotiate"))
        negotiations.push(response.status());
    });
    page.on("websocket", (socket) => {
      sockets.push("created");
      socket.on("socketerror", () => sockets.push("error"));
      socket.on("close", () => sockets.push("closed"));
    });
    await login(page, { tenantKey: process.env.AURALY_E2E_TENANT_KEY ?? "", username: username!, password: password! }, 60_000);
    await page.addInitScript((id) => localStorage.setItem("selected_business_id", id), businessId!.toLowerCase());
    await page.goto("/restaurant/commands");
    try {
      await expect(page.getByText("En vivo", { exact: true })).toBeVisible({ timeout: 30_000 });
    } catch (error) {
      console.log("WebKit comandas:", { negotiations, sockets, body: (await page.locator("body").innerText()).slice(0, 400) });
      throw error;
    }
    expect(negotiations).toContain(200);
    await context.close();
  } finally {
    await browser.close();
  }
});

test("un permiso rechazado no provoca negociaciones repetidas", async ({ page }) => {
  test.skip(!businessId || !username || !password, "Requiere usuario de comandas de desarrollo.");
  await login(page, { tenantKey: process.env.AURALY_E2E_TENANT_KEY ?? "", username: username!, password: password! }, 60_000);
  await page.addInitScript((id) => localStorage.setItem("selected_business_id", id), businessId!.toLowerCase());
  let negotiations = 0;
  await page.route("**/restaurant/command-cards/synchronization/negotiate", async (route) => {
    negotiations += 1;
    await route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ detail: "No tienes permiso para recibir comandas de esta sede." }) });
  });
  await page.goto("/restaurant/commands");
  await expect(page.getByRole("alert").filter({ hasText: "No tienes permiso para recibir comandas" })).toBeVisible();
  await page.waitForTimeout(2_500);
  expect(negotiations).toBe(1);
});
