import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.route("**/api/tenant-commercial/catalog", route => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({
      plans: [
        { planId: "starter", code: "starter", name: "Plan Inicio", monthlyPriceCop: 80000, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 1, includedSellerUsers: 0, includedPosDevices: 0, includedDianDocuments: 100, includedPayrollEmployees: 0, isRecommended: false, isCustom: false, features: ["Facturación electrónica", "Inventario"] },
        { planId: "essential", code: "essential", name: "Plan Esencial", monthlyPriceCop: 119900, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 3, includedSellerUsers: 0, includedPosDevices: 1, includedDianDocuments: 500, includedPayrollEmployees: 10, isRecommended: false, isCustom: false, features: ["POS", "Facturación electrónica", "Inventario", "Contabilidad", "Nómina"] },
        { planId: "business", code: "business", name: "Plan Negocio", monthlyPriceCop: 299900, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 8, includedSellerUsers: 0, includedPosDevices: 3, includedDianDocuments: 1500, includedPayrollEmployees: 30, isRecommended: true, isCustom: false, features: ["POS", "Facturación electrónica", "Inventario", "Contabilidad", "Nómina", "Soporte prioritario"] },
        { planId: "company", code: "company", name: "Plan Empresa", monthlyPriceCop: 449900, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 12, includedSellerUsers: 0, includedPosDevices: 5, includedDianDocuments: 3000, includedPayrollEmployees: 100, isRecommended: false, isCustom: false, features: ["POS", "Facturación electrónica", "Inventario", "Contabilidad", "Nómina"] },
        { planId: "corporate", code: "corporate", name: "Personalizado", monthlyPriceCop: 0, salesTaxRate: 19, annualDiscountRate: 15, includedFullUsers: 0, includedSellerUsers: 0, includedPosDevices: 0, includedDianDocuments: 0, includedPayrollEmployees: 0, isRecommended: false, isCustom: true, features: ["Facturación electrónica", "Inventario", "Contabilidad", "Nómina", "Acompañamiento especializado"] },
      ],
      addOns: [{ addOnId: "full-user", code: "full_user", name: "Usuario completo adicional", unitLabel: "usuario", unitSize: 1, monthlyUnitPriceCop: 30000, salesTaxRate: 19 }],
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
  const operationsPlans = page.locator("#planes");
  await expect(operationsPlans.getByText("Plan Inicio", { exact: true })).toBeVisible();
  await expect(operationsPlans.getByText(/\$\s*80\.000/)).toBeVisible();
  const starterPlan = operationsPlans.locator('[data-plan-code="starter"]');
  const essentialPlan = operationsPlans.locator('[data-plan-code="essential"]');
  const businessPlan = operationsPlans.locator('[data-plan-code="business"]');
  await expect(starterPlan.getByText("Facturación electrónica", { exact: true })).toBeVisible();
  await expect(starterPlan.getByText("Inventario", { exact: true })).toBeVisible();
  await expect(starterPlan.getByText("Contabilidad", { exact: true })).toBeHidden();
  await expect(starterPlan.getByText("Nómina", { exact: true })).toBeHidden();
  await expect(starterPlan.getByText("No incluye caja, contabilidad ni nómina.", { exact: true })).toBeVisible();
  await expect(essentialPlan.getByText("Inventario", { exact: true })).toBeVisible();
  await expect(essentialPlan.getByText("Contabilidad", { exact: true })).toBeVisible();
  await expect(essentialPlan.getByText("Nómina", { exact: true })).toBeVisible();
  await expect(businessPlan.getByText("Inventario", { exact: true })).toBeVisible();
  await expect(businessPlan.getByText("Contabilidad", { exact: true })).toBeVisible();
  await expect(businessPlan.getByText("Nómina", { exact: true })).toBeVisible();
  await expect(operationsPlans.getByText("Plan Esencial", { exact: true })).toBeVisible();
  await expect(operationsPlans.getByText("Plan Negocio", { exact: true })).toBeVisible();
  await expect(operationsPlans.getByText("Plan Empresa", { exact: true })).toBeHidden();
  await expect(operationsPlans.getByText("Personalizado", { exact: true })).toBeHidden();
  await expect(operationsPlans.getByText("Usuario completo adicional", { exact: true })).toBeHidden();
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
  await expect(page.getByText("Plan Inicio", { exact: true })).toBeVisible();
  await expect(page.getByText("Plan Esencial", { exact: true })).toBeVisible();
  await expect(page.getByText("Plan Negocio", { exact: true })).toBeVisible();
  await expect(page.getByText("Plan Empresa", { exact: true })).toBeHidden();
  await expect(page.getByRole("link", { name: /Crear empresa/ }).first()).toBeVisible();

});
