import { expect, test, type Page } from "@playwright/test";

const username = process.env.AURALY_E2E_USERNAME ?? "admin";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 20_000 });
}

const viewports = [
  { name: "teléfono", width: 390, height: 844 },
  { name: "tablet", width: 820, height: 1180 },
  { name: "escritorio", width: 1440, height: 1000 },
];

for (const viewport of viewports) {
  test(`Pedidos y Despachos comparten la cabecera estándar en ${viewport.name}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await login(page);

    const styles: string[] = [];
    for (const view of [
      { path: "/dashboard/orders", title: "Pedidos", oldLabel: "Comercio" },
      { path: "/dashboard/dispatches", title: "Despachos", oldLabel: "Logística de salida" },
    ]) {
      const response = await page.goto(view.path);
      expect(response?.status()).toBeLessThan(400);
      const heading = page.locator("main h1").filter({ hasText: view.title });
      await expect(heading).toHaveCount(1);
      await expect(heading).toHaveClass(/text-2xl/);
      await expect(heading).toHaveClass(/font-semibold/);
      await expect(page.getByText(view.oldLabel, { exact: true })).toHaveCount(0);
      await expect(page.locator("body")).not.toContainText("Error interno del servidor");

      styles.push(
        await heading.evaluate((element) => {
          const style = window.getComputedStyle(element);
          return `${style.fontSize}|${style.fontWeight}|${style.letterSpacing}`;
        }),
      );
      expect(
        await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
        `${view.title} no debe generar desplazamiento horizontal`,
      ).toBe(true);
    }
    expect(styles[0]).toBe(styles[1]);
  });
}
