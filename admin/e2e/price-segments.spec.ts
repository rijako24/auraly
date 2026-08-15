import { expect, test, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(user);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 60_000 });
  await page.evaluate(() => {
    const raw = localStorage.getItem("auth-state");
    if (!raw) return;
    const auth = JSON.parse(raw);
    const current = auth.state.user.permissions ?? [];
    auth.state.user.permissions = Array.from(new Set([...current, "pricing.segments.read", "pricing.segments.manage"]));
    localStorage.setItem("auth-state", JSON.stringify(auth));
  });
}

test("listas y canales administra condiciones y cada guardado cierra su modal", async ({ page }) => {
  test.setTimeout(120_000);
  const listId = "99999999-9999-7999-8999-999999999999";
  const productId = "77777777-7777-7777-7777-777777777777";
  let segments = [
    { id: listId, kind: "PriceList", code: "MAYORISTA", name: "Mayoristas", isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 2 },
    { id: "88888888-8888-7888-8888-888888888888", kind: "PriceChannel", code: "WEB", name: "Tienda web", isActive: true, createdAt: "2026-08-14T10:00:00Z", productCount: 0, customerCount: 0 },
  ];
  let savedItem: Record<string, unknown> | null = null;

  await page.route("**/api/commerce/v1/pricing/segments**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (request.method() === "GET" && /\/items$/.test(url.pathname)) {
      await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
      return;
    }
    if (request.method() === "GET") {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(segments) });
      return;
    }
    if (request.method() === "POST") {
      const body = request.postDataJSON() as { kind: "PriceList" | "PriceChannel"; code: string; name: string };
      const created = { id: crypto.randomUUID(), kind: body.kind, code: body.code, name: body.name, isActive: true, createdAt: new Date().toISOString(), productCount: 0, customerCount: 0 };
      segments = [...segments, created];
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(created) });
      return;
    }
    if (request.method() === "PUT") {
      savedItem = request.postDataJSON() as Record<string, unknown>;
      await route.fulfill({ status: 204 });
      return;
    }
    await route.fulfill({ status: 204 });
  });

  await page.route("**/api/businesses/*/products**", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        items: [{ productId, businessId: "22222222-2222-2222-2222-222222222222", productCode: "PRD-000001", sku: "ACE-3L", name: "Aceite vegetal 3000 ml", unitPrice: 29500, currency: "COP", manageStock: true, isActive: true }],
        page: 1,
        pageSize: 50,
        totalCount: 1,
        totalPages: 1,
      }),
    });
  });

  await page.route("**/api/execution-context/access", async (route) => {
    const headers = route.request().headers();
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        tenantId: headers["x-tenant-id"] ?? "22222222-2222-2222-2222-222222222222",
        businessId: headers["x-business-id"] ?? "22222222-2222-2222-2222-222222222222",
        roles: ["Administrator"],
        permissions: ["dashboard.read", "catalog.read", "pricing.read", "pricing.segments.read", "pricing.segments.manage"],
      }),
    });
  });

  await login(page);
  await page.goto("/dashboard/products/price-segments");
  await expect(page.getByRole("heading", { name: "Listas y canales comerciales" })).toBeVisible();

  await page.getByRole("button", { name: "Nueva lista o canal" }).click();
  const createDialog = page.getByRole("dialog", { name: "Nueva lista o canal" });
  await createDialog.getByText("Código *", { exact: true }).locator("..").getByRole("textbox").fill("DISTRIBUIDOR");
  await createDialog.getByText("Nombre *", { exact: true }).locator("..").getByRole("textbox").fill("Distribuidores");
  await createDialog.getByRole("button", { name: "Crear", exact: true }).click();
  await expect(createDialog).toBeHidden();
  await expect(page.getByRole("button", { name: /Distribuidores/ })).toBeVisible();

  await page.getByRole("button", { name: /Mayoristas/ }).click();
  const detailDialog = page.getByRole("dialog", { name: "Mayoristas" });
  await expect(detailDialog.getByText("Escalas por cantidad")).toBeVisible();
  await detailDialog.getByRole("button", { name: "Agregar producto" }).click();

  const itemDialog = page.getByRole("dialog", { name: "Agregar producto" });
  await itemDialog.getByPlaceholder(/Buscar por nombre/).fill("Aceite");
  await itemDialog.getByRole("button", { name: /Aceite vegetal 3000 ml/ }).click();
  const minimum = itemDialog.getByText("Cantidad mínima *", { exact: true }).locator("..").locator("input");
  await minimum.fill("12");
  await itemDialog.getByRole("button", { name: "Guardar condición" }).click();
  await expect(itemDialog).toBeHidden();
  await expect(detailDialog).toBeVisible();
  expect(savedItem).toMatchObject({ amount: 29500, minimumQuantity: 12, excluded: false });
});
