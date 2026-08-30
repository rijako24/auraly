import { expect, test } from "@playwright/test";

test("la landing unificada presenta operación, facturación y agentes en escritorio", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { name: /Factura, opera y crece/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Planes que crecen contigo." })).toBeVisible();
  await expect(page.getByText("Facturación DIAN", { exact: true })).toBeVisible();
  await expect(page.getByText("Agentes de IA 24/7", { exact: true })).toBeVisible();
  await expect(page.getByText("Esencial", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Negocio", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Empresa", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("heading", { name: "Créditos y capacidad para conversaciones reales." })).toBeVisible();

  await page.screenshot({ path: "test-results/landing-desktop.png", fullPage: true });
});

test("la landing unificada conserva navegación y planes en teléfono", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");

  await expect(page.getByRole("heading", { name: /Factura, opera y crece/ })).toBeVisible();
  await page.screenshot({ path: "test-results/landing-mobile-hero.png" });
  await page.getByRole("button", { name: "Abrir menú" }).click();
  const mobileNavigation = page.locator("header");
  await expect(mobileNavigation.getByRole("link", { name: "Facturación electrónica" })).toBeVisible();
  await expect(mobileNavigation.getByRole("link", { name: "Agentes", exact: true })).toBeVisible();
  await expect(mobileNavigation.getByRole("link", { name: "Planes", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cerrar menú" }).click();

  await page.locator("#planes").scrollIntoViewIfNeeded();
  await expect(page.getByText("$119.900", { exact: true })).toBeVisible();
  await expect(page.getByText("$299.900", { exact: true })).toBeVisible();
  await expect(page.getByText("$449.900", { exact: true })).toBeVisible();

  await page.screenshot({ path: "test-results/landing-mobile-plans.png" });
});
