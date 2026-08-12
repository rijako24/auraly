import { expect, test, type Page } from "@playwright/test";

const username = process.env.AURALY_E2E_USERNAME ?? "admin2222";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 20_000 });
}

test("las vistas comerciales canónicas cargan desde el navegador", async ({ page }) => {
  await login(page);
  for (const route of [
    "/dashboard/products",
    "/dashboard/parties",
    "/dashboard/inventory",
    "/dashboard/purchasing/goods-receipts",
  ]) {
    const response = await page.goto(route);
    expect(response, `La vista ${route} debe responder por HTTP`).not.toBeNull();
    expect(response!.status(), `La vista ${route} no debe devolver un error HTTP`).toBeLessThan(400);
    await expect(page.locator("body")).not.toContainText("Error interno del servidor");
  }
});
