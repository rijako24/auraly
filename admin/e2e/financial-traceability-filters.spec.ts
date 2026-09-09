import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const permissions = ["dashboard.read", "accounting.read"];

test.use({ serviceWorkers: "block" });

const json = (route: Route, body: unknown) => route.fulfill({
  status: 200,
  contentType: "application/json",
  body: JSON.stringify(body),
});

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, userId, permissions }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: {
      userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test",
      firstName: "Prueba", lastName: "E2E", avatarUrl: null,
      roles: ["Administrator"], permissions,
    } }, version: 0 }));
  }, { tenantId, businessId, userId, permissions });
}

test("trazabilidad filtra por tipo de operación junto con el rango de fechas", async ({ page }) => {
  const documentRequests: string[] = [];
  await authenticate(page);
  await page.route("**/api/**", route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    if (path.endsWith("/auth/me")) return json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", roles: ["Administrator"], permissions });
    if (path.endsWith("/execution-context/tenants")) return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses")) return json(route, [{ tenantId, businessId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/access")) return json(route, { tenantId, businessId, roles: ["Administrator"], permissions });
    if (path.endsWith("/commerce/v1/reference-options/accounting-document-type")) return json(route, [
      { id: "44444444-4444-4444-4444-444444444444", code: "GoodsReceipt", label: "Recepción de compra", description: null, sortOrder: 60 },
    ]);
    if (path.endsWith("/commerce/v1/accounting/documents")) {
      documentRequests.push(url.search);
      return json(route, { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
    }
    return json(route, []);
  });

  await page.goto("/dashboard/financial-traceability");
  await page.getByLabel("Tipo de operación").click();
  await page.getByRole("option", { name: "Recepción de compra", exact: true }).click();

  await expect.poll(() => documentRequests.some(query =>
    new URLSearchParams(query).get("documentType") === "GoodsReceipt" &&
    new URLSearchParams(query).has("from") && new URLSearchParams(query).has("to"),
  )).toBe(true);
  await expect(page.getByText("No hay documentos para estos filtros.")).toBeVisible();
});
