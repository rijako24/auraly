import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

test.use({ ignoreHTTPSErrors: true });

async function openTestTable(page: import("@playwright/test").Page) {
  const businessId = process.env.AURALY_E2E_RESTAURANT_BUSINESS_ID;
  const tableCode = process.env.AURALY_E2E_RESTAURANT_TABLE_CODE;
  const productName = process.env.AURALY_E2E_RESTAURANT_PRODUCT_NAME;
  test.skip(!businessId || !tableCode || !productName,
    "Requiere sede, mesa y producto de prueba.");
  await login(page, undefined, 60_000);
  await page.addInitScript((value) => {
    localStorage.setItem("selected_business_id", value);
  }, businessId!.toLowerCase());
  await page.goto("/pos/restaurant");
  await page.getByRole("button", { name: new RegExp(`\\b${tableCode!}\\b`) }).first().click();
  const waiter = page.getByRole("dialog", { name: "Identificar mesero" });
  if (await waiter.isVisible().catch(() => false)) {
    await waiter.getByRole("button", { name: /Mesero 1/i }).click();
    await waiter.getByPlaceholder("Principal o secundaria")
      .fill(process.env.AURALY_E2E_RESTAURANT_WAITER_SECRET ?? "1234");
    await waiter.getByRole("button", { name: "Abrir mesa" }).click();
  }
  await expect(page.getByRole("heading", { name: "Elige productos" })).toBeVisible();
  await page.getByPlaceholder("Buscar producto").fill(productName!);
}

test("agregar producto y enviar comanda conserva la cuenta conciliada", async ({ page }) => {
  test.setTimeout(120_000);
  const productName = process.env.AURALY_E2E_RESTAURANT_PRODUCT_NAME;
  const responses: { method: string; path: string; status: number; durationMs: number }[] = [];
  page.on("response", (response) => {
    if (!response.url().includes("/restaurant/accounts/")) return;
    void response.finished().then(() => responses.push({
      method: response.request().method(),
      path: new URL(response.url()).pathname,
      status: response.status(),
      durationMs: Math.round(response.request().timing().responseEnd),
    })).catch(() => undefined);
  });
  await openTestTable(page);
  await page.getByRole("button", { name: `Agregar ${productName}` }).click();
  await expect(page.getByRole("button", { name: /Enviar pendientes/ })).toBeEnabled();
  await page.getByRole("button", { name: /Enviar pendientes/ }).click();
  try {
    await expect(page.getByRole("button", { name: "Reintentar envío" })).toHaveCount(0);
    await expect(page.getByText(/Comanda enviada/)).toBeVisible();
  } catch (error) {
    console.log("Restaurant reconciliation responses", responses);
    throw error;
  }
  console.log("Restaurant account request durations", responses.filter((response) =>
    response.method !== "GET"));
});

test("respuesta perdida del envío se reconcilia sin duplicar la comanda", async ({ page }) => {
  test.setTimeout(120_000);
  const productName = process.env.AURALY_E2E_RESTAURANT_PRODUCT_NAME;
  await openTestTable(page);
  await page.getByRole("button", { name: `Agregar ${productName}` }).click();
  const operations: string[] = [];
  let firstResponseStatus: number | undefined;
  await page.route("**/restaurant/accounts/*/commands", async (route) => {
    const body = route.request().postDataJSON() as { operationId: string };
    operations.push(body.operationId);
    if (operations.length === 1) {
      const committed = await route.fetch();
      firstResponseStatus = committed.status();
      await route.fulfill({ status: 503, contentType: "application/json",
        body: JSON.stringify({ message: "Respuesta perdida de prueba" }) });
    } else await route.continue();
  });
  await page.getByRole("button", { name: /Enviar pendientes/ }).click();
  await expect.poll(() => operations.length, { timeout: 30_000 }).toBe(2);
  await expect(page.getByRole("button", { name: /Enviar pendientes/ })).toHaveCount(0);
  expect(firstResponseStatus).toBe(200);
  expect(operations).toHaveLength(2);
  expect(operations[1]).toBe(operations[0]);
  await expect(page.getByRole("button", { name: "Reintentar envío" })).toHaveCount(0);
});

test("en celular los productos y la cuenta se pueden ver sin botones superpuestos", async ({ page }) => {
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 393, height: 852 });
  await openTestTable(page);
  const productName = process.env.AURALY_E2E_RESTAURANT_PRODUCT_NAME!;
  const productTile = page.getByRole("button", { name: `Agregar ${productName}` });
  await expect(productTile).toBeVisible();
  expect((await productTile.boundingBox())!.height).toBeGreaterThanOrEqual(208);
  await page.getByRole("button", { name: /Cuenta ·/ }).click();
  const account = page.locator("aside").first();
  await expect(account.getByText(productName, { exact: true }).first()).toBeVisible();
  const geometry = await page.evaluate(() => {
    const header = document.querySelector("main > header")!;
    const buttons = Array.from(header.querySelectorAll("button, a"))
      .filter((element) => getComputedStyle(element).display !== "none")
      .map((element) => element.getBoundingClientRect());
    const account = document.querySelector("main aside")!;
    const items = account.children[1] as HTMLElement;
    const footer = account.children[2].getBoundingClientRect();
    return {
      overflow: getComputedStyle(items).overflowY,
      itemBottom: items.getBoundingClientRect().bottom,
      footerTop: footer.top,
      outOfBounds: buttons.some((box) => box.left < 0 || box.right > innerWidth + 1),
      overlaps: buttons.some((first, index) => buttons.slice(index + 1).some((second) =>
        Math.min(first.right, second.right) - Math.max(first.left, second.left) > 1 &&
        Math.min(first.bottom, second.bottom) - Math.max(first.top, second.top) > 1)),
    };
  });
  expect(geometry.overflow).toBe("visible");
  expect(geometry.footerTop).toBeGreaterThanOrEqual(geometry.itemBottom - 1);
  expect(geometry.outOfBounds).toBe(false);
  expect(geometry.overlaps).toBe(false);
});

test("un envío sin conexión no deja atrapado al usuario en la mesa", async ({ page }) => {
  test.setTimeout(120_000);
  await openTestTable(page);
  const productName = process.env.AURALY_E2E_RESTAURANT_PRODUCT_NAME!;
  await page.getByRole("button", { name: `Agregar ${productName}` }).click();
  await page.route("**/restaurant/accounts/*/commands", (route) => route.abort());
  await page.route("**/restaurant/accounts/*/draft", (route) => route.abort());
  await page.getByRole("button", { name: /Enviar pendientes/ }).click();
  await expect(page.getByText(/No se pudo verificar el envío/)).toBeVisible();
  await page.getByRole("button", { name: "Volver a mesas" }).click();
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" }))
    .toBeVisible();
  await page.unrouteAll();
  await page.getByRole("button", { name: new RegExp(`\\b${process.env.AURALY_E2E_RESTAURANT_TABLE_CODE!}\\b`) }).first().click();
  await expect(page.getByRole("heading", { name: "Elige productos" })).toBeVisible();
});
