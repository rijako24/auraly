import { expect, test } from "@playwright/test";
import { login } from "./support/auth";

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
