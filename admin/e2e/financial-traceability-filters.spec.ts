import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const permissions = ["dashboard.read", "accounting.read", "accounting.manual.create"];

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

async function mockTraceability(page: Page, documentRequests: string[] = []) {
  await authenticate(page);
  await page.route("**/api/**", route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    if (path.endsWith("/auth/me")) return json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", roles: ["Administrator"], permissions });
    if (path.endsWith("/execution-context/tenants")) return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses")) return json(route, [{ tenantId, businessId, name: "Auraly", timeZone: "America/Bogota" }]);
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
}

test("trazabilidad filtra por tipo de operación junto con el rango de fechas", async ({ page }) => {
  const documentRequests: string[] = [];
  await mockTraceability(page, documentRequests);
  await page.goto("/dashboard/financial-traceability");
  await page.getByRole("button", { name: "Filtros de documentos", exact: true }).click();
  await page.getByLabel("Tipo de operación").click();
  await page.getByRole("option", { name: "Recepción de compra", exact: true }).click();

  await expect.poll(() => documentRequests.some(query =>
    new URLSearchParams(query).get("documentType") === "GoodsReceipt" &&
    new URLSearchParams(query).has("from") && new URLSearchParams(query).has("to"),
  )).toBe(true);
  await expect(page.getByText("No hay documentos para estos filtros.")).toBeVisible();
});

for (const [path, title] of [["financial-traceability", "Trazabilidad financiera"], ["cash-receipts", "Recibos de caja"]]) {
  test(`cabeceras financieras legibles sin comprimir el texto: ${path}`, async ({ page }) => {
    await mockTraceability(page);
    await page.goto(`/dashboard/${path}`);
    const heading = page.getByRole("heading", { name: title, level: 1 });
    const content = heading.locator("..");
    const header = heading.locator("xpath=ancestor::header");
    for (const width of [320, 390, 768, 1366]) {
      await page.setViewportSize({ width, height: 900 });
      await heading.scrollIntoViewIfNeeded();
      await expect(heading).toBeVisible();
      const description = await content.locator("p").last().boundingBox();
      expect(description!.width).toBeGreaterThanOrEqual(170);
      expect(await header.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
      if (width < 640) {
        const text = await content.boundingBox();
        const action = await header.getByRole("button").boundingBox();
        expect(action!.y).toBeGreaterThanOrEqual(text!.y + text!.height);
        expect((await header.boundingBox())!.height).toBeLessThan(420);
      }
    }
  });
}
