import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const permissions = ["dashboard.read", "products.read"];

function json(route: Route, body: unknown) {
  return route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function authenticate(page: Page) {
  await page.context().addCookies([{
    name: "auth_token",
    value: "e2e",
    url: "http://127.0.0.1:3000",
    httpOnly: true,
    sameSite: "Lax",
  }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({
      state: {
        isAuthenticated: true,
        user: {
          userId: user,
          tenantId: tenant,
          tenantKey: "AURALY",
          username: "e2e",
          email: "e2e@auraly.test",
          firstName: "Prueba",
          lastName: "E2E",
          avatarUrl: null,
          roles: ["Administrator"],
          permissions: granted,
        },
      },
      version: 0,
    }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
}

async function mockApi(page: Page) {
  await page.route("**/api/**", route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/execution-context/tenants"))
      return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses"))
      return json(route, [{ tenantId, businessId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/access"))
      return json(route, { tenantId, businessId, roles: ["Administrator"], permissions });
    if (path.endsWith(`/businesses/${businessId}/products`))
      return json(route, { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
    return json(route, []);
  });
}

async function openProductsAndAssertSingleNavigation(page: Page) {
  const shellRequests: Array<{ path: string; type: string }> = [];
  page.on("request", request => {
    const url = new URL(request.url());
    if (!url.search && ["/dashboard", "/dashboard/products"].includes(url.pathname))
      shellRequests.push({ path: url.pathname, type: request.resourceType() });
  });

  await page.goto("/dashboard/products");
  await expect(page.getByRole("heading", { name: "Productos", exact: true })).toBeVisible();
  await page.waitForTimeout(1_500);
  expect(shellRequests).toEqual([
    { path: "/dashboard/products", type: "document" },
  ]);
}

test.describe("navegación web normal", () => {
  test.use({ serviceWorkers: "block" });

  test("abrir Productos no vuelve a descargar su documento ni el dashboard", async ({ page }) => {
    await authenticate(page);
    await mockApi(page);
    await openProductsAndAssertSingleNavigation(page);
  });
});

test.describe("navegación de la aplicación instalada", () => {
  test.use({ serviceWorkers: "allow" });

  test("el Service Worker controla Productos sin duplicar la navegación", async ({ page }) => {
    await authenticate(page);
    await mockApi(page);
    await page.goto("/login");
    await expect.poll(() => page.evaluate(async () => {
      const registration = await navigator.serviceWorker.ready;
      return registration.active?.state ?? null;
    })).toBe("activated");

    await openProductsAndAssertSingleNavigation(page);
    await expect.poll(() => page.evaluate(() => Boolean(navigator.serviceWorker.controller))).toBe(true);
  });
});
