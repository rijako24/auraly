import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.route("**/api/tenant-commercial/catalog", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      plans: [
        { planId: "starter", code: "starter", name: "Esencial", monthlyPriceCop: 119900, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 1, includedSellerUsers: 0, includedPosDevices: 1, includedDianDocuments: 100, includedPayrollEmployees: 0, isRecommended: false, isCustom: false, features: ["Facturación y operación"] },
        { planId: "business", code: "business", name: "Negocio", monthlyPriceCop: 299900, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 3, includedSellerUsers: 2, includedPosDevices: 2, includedDianDocuments: 1000, includedPayrollEmployees: 10, isRecommended: true, isCustom: false, features: ["Inventario y contabilidad"] },
        { planId: "company", code: "company", name: "Empresa", monthlyPriceCop: 449900, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 5, includedSellerUsers: 5, includedPosDevices: 4, includedDianDocuments: 2000, includedPayrollEmployees: 30, isRecommended: false, isCustom: false, features: ["Operación multiárea"] },
      ],
      addOns: [],
    }),
  }));
});

test("la landing vende la plataforma completa y conserva sus conversiones", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("heading", { name: /Tu negocio, en una sola verdad/ })).toBeVisible();
  await expect(page.getByRole("link", { name: "Entrar", exact: true })).toHaveAttribute("href", "/login");
  await expect(page.getByRole("link", { name: /Solicitar demo/, exact: true }).first()).toHaveAttribute("href", "#demo");
  await expect(page.getByRole("tab", { name: /Facturación y control/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Vendes una vez. Auraly conecta todo lo demás." })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Tu vendedor sigue vendiendo, incluso donde no llega la señal." })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Cada entrega explica qué pasó y cómo pagó el cliente." })).toBeVisible();
  await expect(page.getByRole("link", { name: "Abrir WhatsApp con Aly" })).toHaveAttribute("href", /wa\.me/);
  await expect(page.getByRole("heading", { name: "Planes que crecen contigo." })).toBeVisible();
  await expect(page.getByText("Facturación electrónica DIAN", { exact: true })).toBeVisible();
  await expect(page.getByText("Agentes listos para operar", { exact: true })).toBeVisible();
  await expect(page.getByText("Esencial", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Negocio", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Empresa", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("$350.000", { exact: true })).toBeHidden();
  await page.getByRole("button", { name: /Ver planes y precios de agentes/ }).click();
  await expect(page.getByText("$350.000", { exact: true })).toBeVisible();
  await expect(page.getByText("Plan de agentes de IA", { exact: true }).first()).toBeVisible();
  await expect(page.getByText(/No son los precios de facturación, POS/)).toBeVisible();

});

test("la landing unificada conserva navegación y planes en teléfono", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");

  await expect(page.getByRole("heading", { name: /Tu negocio, en una sola verdad/ })).toBeVisible();
  await page.getByRole("button", { name: "Abrir menú" }).click();
  const mobileNavigation = page.locator("header");
  await expect(mobileNavigation.getByRole("link", { name: "Facturación electrónica" })).toBeVisible();
  await expect(mobileNavigation.getByRole("link", { name: "Pedidos", exact: true })).toBeVisible();
  await expect(mobileNavigation.getByRole("link", { name: "Transportador", exact: true })).toBeVisible();
  await expect(mobileNavigation.getByRole("link", { name: "Agentes", exact: true })).toBeVisible();
  await expect(mobileNavigation.getByRole("link", { name: "Planes", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cerrar menú" }).click();

  await page.locator("#planes").scrollIntoViewIfNeeded();
  await expect(page.getByText("Esencial", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Negocio", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("Empresa", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("link", { name: /Crear empresa/ }).first()).toBeVisible();

});
