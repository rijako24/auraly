import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const productId = "44444444-4444-4444-4444-444444444444";
const categoryId = "55555555-5555-5555-5555-555555555555";
const customerId = "99999999-9999-9999-9999-999999999999";
const sellerId = "aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb";
const permissions = [
  "dashboard.read", "products.read", "promotions.read", "promotions.create",
  "promotions.update", "promotions.delete", "services.read", "services.create",
  "services.delete", "inventory.read", "orders.read",
];

test.use({ serviceWorkers: "block" });

const json = (route: Route, body: unknown, status = 200) => route.fulfill({
  status,
  contentType: "application/json",
  body: JSON.stringify(body),
});

async function authenticate(page: Page) {
  const baseUrl = process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000";
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseUrl, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
}

function pageResult(items: unknown[]) {
  return { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1, hasNextPage: false, hasPreviousPage: false };
}

async function mockApi(page: Page, productRequests: string[], serviceRequests: string[], promotionPosts: unknown[], orderRequests: string[] = []) {
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (path.endsWith("/auth/me")) return json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions });
    if (path.endsWith("/execution-context/tenants")) return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses")) return json(route, [{ tenantId, businessId, name: "Sede Centro" }]);
    if (path.endsWith("/execution-context/access")) return json(route, { tenantId, businessId, roles: ["Administrator"], permissions });
    if (path.endsWith("/businesses") && request.method() === "GET") return json(route, pageResult([{ businessId, tenantId, name: "Sede Centro", isActive: true }]));
    if (path.endsWith(`/businesses/${businessId}/product-categories`)) return json(route, [{ productCategoryId: categoryId, parentProductCategoryId: null, name: "Bebidas", displayOrder: 1, isActive: true, isBrowsable: true, depth: 0, path: "Bebidas" }]);
    if (path.endsWith(`/businesses/${businessId}/products`)) {
      productRequests.push(url.search);
      return json(route, pageResult([{ productId, businessId, productCode: "CAF-01", sku: "CAF-01", reference: "CAF-01", name: "Café especial", unitPrice: 20_000, currency: "COP", manageStock: true, stockQuantity: 8, isActive: true }]));
    }
    if (path.endsWith(`/businesses/${businessId}/promotions`)) {
      if (request.method() === "POST") {
        const payload = request.postDataJSON();
        promotionPosts.push(payload);
        return json(route, { promotionId: crypto.randomUUID(), tenantId, createdAt: new Date().toISOString(), ...payload }, 201);
      }
      return json(route, pageResult([{ promotionId: "66666666-6666-6666-6666-666666666666", tenantId, name: "Bebidas 20%", description: null, isActive: true, startsAtUtc: null, endsAtUtc: null, priority: 0, isCombinable: false, appliesToAllBusinesses: true, applicableBusinessIds: [], couponCode: null, conditions: [], benefits: [{ benefitType: 0, targetItemType: 3, productCategoryId: categoryId, discountPercentage: 20, appliesToQuantity: null }], createdAt: "2026-09-09T12:00:00Z" }]));
    }
    if (path.endsWith("/services")) {
      serviceRequests.push(url.search);
      return json(route, pageResult([{ serviceId: "77777777-7777-7777-7777-777777777777", businessId, serviceName: "Consulta inicial", description: "Valoración profesional", durationMinutes: 45, price: 80_000, includeInCheckoutTotal: true, isActive: true, categoryId, categoryName: "Consultas", tier: 0, serviceType: 0, createdAt: "2026-09-09T12:00:00Z", updatedAt: null }]));
    }
    if (path.endsWith("/commerce/v1/inventory/warehouses")) return json(route, [{ warehouseId: "88888888-8888-8888-8888-888888888888", code: "PRIN", name: "Principal" }]);
    if (path.endsWith("/commerce/v1/inventory/balances")) return json(route, pageResult([{ warehouseId: "88888888-8888-8888-8888-888888888888", warehouseCode: "PRIN", warehouseName: "Principal", productId, productCode: "CAF-01", productName: "Café especial", managesInventory: true, quantityOnHand: 8, unitCost: 10_000, averageUnitCost: 9_500, inventoryValue: 76_000, updatedAt: "2026-09-09T12:00:00Z" }]));
    if (path.endsWith("/commerce/v1/inventory/movements") || path.endsWith("/commerce/v1/inventory/operations")) return json(route, pageResult([]));
    if (path.endsWith("/commerce/v1/pos/workspace/options")) return json(route, []);
    if (path.endsWith("/commerce/v1/routes")) return json(route, pageResult([]));
    if (path.endsWith("/commerce/v1/orders")) { orderRequests.push(url.search); return json(route, { items: [], page: 1, pageSize: 20, totalCount: 0, hasMore: false }); }
    if (path.endsWith("/parties/role-options")) {
      const role = url.searchParams.get("role");
      const item = role === "Seller"
        ? { partyId: "seller-party", roleId: sellerId, role: "Seller", displayName: "Ana Vendedora", identification: "1010" }
        : { partyId: "customer-party", roleId: customerId, role: "Customer", displayName: "Cliente Ejemplo", identification: "900123" };
      return json(route, pageResult([item]));
    }
    if (path.endsWith("/commerce/v1/product-brands")) return json(route, []);
    return json(route, []);
  });
}

test("promociones usa el configurador guiado y guarda categoría por identificador", async ({ page }, testInfo) => {
  const productRequests: string[] = [];
  const serviceRequests: string[] = [];
  const promotionPosts: unknown[] = [];
  await mockApi(page, productRequests, serviceRequests, promotionPosts);
  await authenticate(page);
  await page.goto("/dashboard/promotions");

  await expect(page.getByRole("heading", { name: "Promociones" })).toBeVisible();
  await expect(page.getByText("Bebidas 20%", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Nueva promoción" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva promoción" });
  await expect(dialog.getByText("Aplicar en todas las sedes", { exact: true })).toBeVisible();
  await dialog.getByLabel("Nombre de la promoción").fill("Bebidas fin de semana");
  await dialog.getByRole("button", { name: /Una categoría/ }).first().click();
  await dialog.getByRole("combobox").first().click();
  await page.getByRole("option", { name: "Bebidas" }).click();
  await expect(dialog.getByText(/Aplica 20% de descuento a Bebidas/)).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("promotions-dialog.png"), fullPage: true });
  await dialog.getByRole("button", { name: "Crear y sincronizar" }).click();
  await expect(dialog).toBeHidden();
  expect(promotionPosts).toHaveLength(1);
  expect(promotionPosts[0]).toMatchObject({
    name: "Bebidas fin de semana",
    appliesToAllBusinesses: true,
    conditions: [],
    benefits: [{ benefitType: 0, targetItemType: 3, productCategoryId: categoryId, discountPercentage: 20 }],
  });
});

test("productos busca en servidor, servicios e inventario muestran el estilo y perfil abre", async ({ page }, testInfo) => {
  const productRequests: string[] = [];
  const serviceRequests: string[] = [];
  await mockApi(page, productRequests, serviceRequests, []);
  await authenticate(page);

  await page.goto("/dashboard/products");
  const productSearch = page.getByPlaceholder("Buscar por nombre o SKU en todos los productos...");
  await expect(productSearch).toBeVisible();
  await expect(page.getByText("Producto o SKU", { exact: true })).toHaveCount(0);
  await productSearch.fill("CAF-01");
  await expect.poll(() => productRequests.some((value) => value.includes("search=CAF-01") && value.includes("includeInactive=false"))).toBe(true);

  await page.goto("/dashboard/services");
  await expect(page.getByRole("heading", { name: "Servicios" })).toBeVisible();
  await expect(page.getByText("Consulta inicial", { exact: true })).toBeVisible();
  await page.getByPlaceholder("Buscar en todos los servicios...").fill("Consulta");
  await expect.poll(() => serviceRequests.some((value) => value.includes("search=Consulta"))).toBe(true);
  await expect(page.getByText("Iniciando Auraly", { exact: true })).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath("services.png"), fullPage: true });

  await page.goto("/dashboard/inventory");
  await expect(page.getByRole("columnheader", { name: "Inventario" })).toBeVisible();
  await expect(page.getByText("Sí maneja", { exact: true })).toBeVisible();
  await expect(page.getByText("Iniciando Auraly", { exact: true })).toHaveCount(0);
  await page.screenshot({ path: testInfo.outputPath("inventory.png"), fullPage: true });

  await page.goto("/dashboard/settings/profile");
  await expect(page.getByRole("heading", { name: "Perfil" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Información personal" })).toBeVisible();
});

test("pedidos agrupa filtros y consulta cliente y vendedor por identificador", async ({ page }, testInfo) => {
  const orderRequests: string[] = [];
  await mockApi(page, [], [], [], orderRequests);
  await authenticate(page);
  await page.goto("/dashboard/orders?view=all");

  await page.getByRole("button", { name: /Filtros/ }).click();
  await expect(page.getByText("Desde", { exact: true })).toBeVisible();
  await expect(page.getByText("Hasta", { exact: true })).toBeVisible();

  await page.getByRole("combobox", { name: "Seleccionar customer" }).click();
  await page.getByRole("option", { name: /Cliente Ejemplo/ }).click();
  await page.getByRole("combobox", { name: "Seleccionar seller" }).click();
  await page.getByRole("option", { name: /Ana Vendedora/ }).click();

  await expect.poll(() => orderRequests.some((value) => value.includes(`customerId=${customerId}`) && value.includes(`sellerId=${sellerId}`))).toBe(true);
  await page.screenshot({ path: testInfo.outputPath("orders-filters.png"), fullPage: true });
});
