import { expect, test } from "@playwright/test";
import { realAccountingContext } from "./support/real-accounting-workspace";

const viewports = [
  { name: "teléfono", width: 390, height: 844 },
  { name: "tablet", width: 820, height: 1180 },
  { name: "escritorio", width: 1440, height: 1000 },
];

for (const viewport of viewports) {
  test(`Pedidos y Despachos comparten la cabecera estándar en ${viewport.name}`, async ({ page }) => {
    test.setTimeout(120_000);
    await page.setViewportSize(viewport);
    await realAccountingContext(page);

    const styles: string[] = [];
    for (const view of [
      { path: "/dashboard/orders", title: "Pedidos", oldLabel: "Comercio" },
      { path: "/dashboard/dispatches", title: "Despachos", oldLabel: "Logística de salida" },
    ]) {
      const resource = page.waitForResponse(response => response.request().method() === "GET" &&
        new URL(response.url()).pathname === `/api/commerce/v1/${view.title === "Pedidos" ? "orders" : "dispatches"}`);
      const response = await page.goto(view.path);
      expect(response?.status()).toBeLessThan(400);
      expect((await resource).ok()).toBe(true);
      await expect(page.getByText("Iniciando Auraly", { exact: true })).toBeHidden();
      if (view.title === "Pedidos") {
        await expect(page.getByText("Actualizando pedidos…", { exact: true })).toBeHidden();
        await expect(page.getByRole("combobox", { name: "Forma de pago del lote", exact: true })).toBeVisible();
        const condition = page.locator('[aria-label="Condición de pago"]');
        expect(await condition.locator("button").evaluateAll(buttons => buttons.every(button =>
          button.clientWidth >= button.scrollWidth && button.clientHeight >= button.scrollHeight))).toBe(true);
        const boxes = await condition.locator("button").evaluateAll(buttons => buttons.map(button => {
          const box = button.getBoundingClientRect(); return { left: box.left, right: box.right };
        }));
        expect(boxes).toHaveLength(2);
        expect(boxes[0].right).toBeLessThanOrEqual(boxes[1].left + 1);
      }
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
      await page.screenshot({ path: test.info().outputPath(`${view.title}-${viewport.width}.png`), fullPage: true });
    }
    expect(styles[0]).toBe(styles[1]);
  });
}
