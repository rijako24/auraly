import { expect, test, type Page } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";

async function prepare(page: Page, permissions: string[]) {
  const user = {
    userId: "33333333-3333-3333-3333-333333333333",
    tenantId, tenantKey: permissions.includes("tenants.read") ? "@auraly" : "@navigation-test", username: "navigation-test",
    firstName: "Prueba", lastName: "Navegación", roles: [], permissions,
  };
  const calls = { list: 0, own: 0 };
  const tenant = {
    tenantId, tenantKey: user.tenantKey, name: "Empresa de prueba",
    email: "navigation@example.test", isActive: true, businessCount: 1,
    activeUserCount: 1, maximumUsers: 5, activeEnrolledDeviceCount: 0,
    maximumEnrolledDevices: 1, createdAt: "2026-09-19T12:00:00Z",
    entityType: "Organization", legalName: "Empresa de prueba",
  };
  await page.context().addCookies([{
    name: "auth_token", value: "navigation-test",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000",
    httpOnly: true, sameSite: "Lax",
  }]);
  await page.addInitScript(({ identity, business }) => {
    localStorage.setItem("selected_tenant_id", identity.tenantId);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({
      state: { isAuthenticated: true, user: identity }, version: 0,
    }));
  }, { identity: user, business: businessId });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants"))
      body = [{ tenantId, name: tenant.name }];
    else if (path.endsWith("/execution-context/businesses"))
      body = [{ tenantId, businessId, name: "Sede de prueba" }];
    else if (path.endsWith("/execution-context/access"))
      body = { tenantId, businessId, roles: user.roles, permissions };
    else if (path.endsWith(`/tenants/${tenantId}/subscription`)) body = null;
    else if (path.endsWith(`/tenants/${tenantId}`)) {
      calls.own++;
      body = tenant;
    } else if (path.endsWith("/tenants")) {
      calls.list++;
      body = { items: [tenant], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/settings");
  await expect(page.getByRole("heading", { name: "Configuración", exact: true })).toBeVisible();
  return calls;
}

for (const scenario of [
  { name: "cliente", permissions: ["tenant.profile.read"], href: "/dashboard/company" },
  { name: "plataforma", permissions: ["tenant.profile.read", "tenants.read"], href: "/dashboard/tenants" },
]) {
  test(`${scenario.name}: una sola Empresa en escritorio, búsqueda y móvil`, async ({ page }) => {
    const calls = await prepare(page, scenario.permissions);
    const desktop = page.locator("aside").getByRole("link", { name: "Empresa", exact: true });
    await expect(desktop).toHaveCount(1);
    await expect(desktop).toHaveAttribute("href", scenario.href);
    await expect(page.locator("aside").getByRole("link", { name: "Empresas", exact: true })).toHaveCount(0);

    await page.getByRole("button", { name: "Buscar", exact: true }).click();
    await page.getByPlaceholder("Buscar páginas...").fill("Empresa");
    await expect(page.getByRole("option", { name: "Empresa", exact: true })).toHaveCount(1);
    await page.keyboard.press("Escape");

    await page.setViewportSize({ width: 800, height: 1000 });
    await page.getByRole("button", { name: "Abrir menú", exact: true }).click();
    const menu = page.getByRole("dialog");
    const company = menu.getByRole("link", { name: "Empresa", exact: true });
    await expect(company).toHaveCount(1);
    await expect(company).toHaveAttribute("href", scenario.href);
    await company.click();
    if (scenario.name === "cliente") {
      await expect(page).toHaveURL(`/dashboard/tenants/${tenantId}?scope=profile`);
      await expect(page.getByRole("heading", { name: "Información legal y comercial" })).toBeVisible();
      expect(calls.list).toBe(0);
      expect(calls.own).toBe(1);
    } else {
      await expect(page).toHaveURL("/dashboard/tenants");
      await expect(page.getByRole("heading", { name: "Empresas", exact: true })).toBeVisible();
      await expect(page.getByRole("row").filter({ hasText: "Empresa de prueba" })).toBeVisible();
      expect(calls.list).toBe(1);
      expect(calls.own).toBe(0);
    }
    await expect(page.getByRole("button", { name: "Editar información" })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "Nueva empresa" })).toHaveCount(0);
  });
}

test("sin permiso de lectura no se ofrece Empresa ni se consulta el listado", async ({ page }) => {
  const calls = await prepare(page, ["tenant.profile.update"]);
  await expect(page.locator("aside").getByRole("link", { name: /^Empresas?$/ })).toHaveCount(0);
  await page.getByRole("button", { name: "Buscar", exact: true }).click();
  await page.getByPlaceholder("Buscar páginas...").fill("Empresa");
  await expect(page.getByRole("option", { name: /^Empresas?$/ })).toHaveCount(0);
  expect(calls).toEqual({ list: 0, own: 0 });
});
