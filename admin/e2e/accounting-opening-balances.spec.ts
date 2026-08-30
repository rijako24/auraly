import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const permissions = ["accounting.read", "accounting.configure", "accounting.activate", "parties.read"];

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: "33333333-3333-3333-3333-333333333333", tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, granted: permissions });
}

async function json(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
}

test("saldos iniciales busca cualquier tercero activo sin filtrar por rol", async ({ page }) => {
  let partyRequest = "";
  await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/reference-options**", route => json(route, []));
  await page.route("**/api/commerce/v1/accounting/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/accounts")) return json(route, [
      { accountId: "44444444-4444-4444-4444-444444444444", code: "130505", name: "Cuentas por cobrar", accountType: "Asset", allowsPosting: true, requiresParty: true, isActive: true },
      { accountId: "55555555-5555-5555-5555-555555555555", code: "310505", name: "Capital", accountType: "Equity", allowsPosting: true, requiresParty: false, isActive: true },
    ]);
    if (path.endsWith("/cost-centers")) return json(route, []);
    if (path.endsWith("/periods")) return json(route, []);
    if (path.endsWith("/mappings")) return json(route, []);
    if (path.endsWith("/category-definitions")) return json(route, []);
    if (path.endsWith("/readiness")) return json(route, { status: "Disabled", blockingIssues: ["Configura los saldos iniciales."], effectiveFrom: null, openingBalanceMode: null });
    if (path.endsWith("/opening-balances")) return json(route, null);
    return json(route, []);
  });
  await page.route("**/api/commerce/v1/parties**", async route => {
    partyRequest = route.request().url();
    return json(route, { items: [
      { partyId: "66666666-6666-6666-6666-666666666666", displayName: "Proveedor Andino", identification: "900100200", email: null, roles: ["Supplier"], isActive: true },
      { partyId: "77777777-7777-7777-7777-777777777777", displayName: "Acreedor Logístico", identification: "901300400", email: null, roles: ["Carrier"], isActive: true },
      { partyId: "88888888-8888-8888-8888-888888888888", displayName: "Cliente Contado", identification: "10203040", email: null, roles: ["Customer"], isActive: true },
    ], page: 1, pageSize: 30, totalCount: 3, totalPages: 1 });
  });

  await authenticate(page);
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales" }).click();
  await page.getByRole("combobox", { name: "Seleccionar tercero" }).first().click();

  await expect(page.getByText("Proveedor Andino")).toBeVisible();
  await expect(page.getByText("Acreedor Logístico")).toBeVisible();
  await expect(page.getByText("Cliente Contado")).toBeVisible();
  const url = new URL(partyRequest);
  expect(url.searchParams.has("role")).toBe(false);
  expect(Number(url.searchParams.get("pageSize"))).toBeLessThanOrEqual(100);
});
