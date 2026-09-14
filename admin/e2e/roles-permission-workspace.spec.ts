import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const roleId = "33333333-3333-3333-3333-333333333333";
const permissions = [
  "dashboard.read",
  "roles.read",
  "roles.create",
  "roles.update",
  "roles.assign_permissions",
  "permissions.read",
];

async function json(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function login(page: Page) {
  await page.context().addCookies([{
    name: "auth_token",
    value: "e2e",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000",
    httpOnly: true,
    sameSite: "Lax",
  }]);
  await page.addInitScript(({ tenant, business, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({
      state: {
        isAuthenticated: true,
        user: {
          userId: "44444444-4444-4444-4444-444444444444",
          tenantId: tenant,
          tenantKey: "AURALY",
          username: "e2e",
          firstName: "Prueba",
          lastName: "E2E",
          roles: ["Administrator"],
          permissions: granted,
        },
      },
      version: 0,
    }));
  }, { tenant: tenantId, business: businessId, granted: permissions });
}

test("ver un rol carga un solo workspace y se puede cerrar sin bloquear la aplicación", async ({ page }) => {
  let workspaceRequests = 0;
  const obsoleteRequests: string[] = [];
  const role = {
    roleId,
    tenantId,
    name: "Supervisor",
    description: "Rol de prueba",
    isSystemRole: false,
    isActive: true,
    createdAt: "2026-09-14T12:00:00Z",
    userCount: 1,
    permissionCount: 2,
  };
  const catalog = [
    { permissionId: "55555555-5555-5555-5555-555555555555", module: "Identity", action: "Read", resource: "roles.read", description: "Ver roles" },
    { permissionId: "66666666-6666-6666-6666-666666666666", module: "Identity", action: "Assign", resource: "roles.assign_permissions", description: "Asignar permisos" },
  ];

  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path === "/api/auth/me") return json(route, {
      userId: "44444444-4444-4444-4444-444444444444",
      tenantId,
      tenantKey: "AURALY",
      username: "e2e",
      firstName: "Prueba",
      lastName: "E2E",
      roles: ["Administrator"],
      permissions,
    });
    if (path.endsWith("/execution-context/tenants"))
      return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses"))
      return json(route, [{ tenantId, businessId, name: "Principal" }]);
    if (path.endsWith("/execution-context/access"))
      return json(route, { tenantId, businessId, roles: ["Administrator"], permissions });
    if (path.endsWith(`/roles/${roleId}/permission-workspace`)) {
      workspaceRequests += 1;
      return json(route, {
        role,
        permissions: catalog,
        assignedPermissionIds: [catalog[0].permissionId, catalog[1].permissionId],
      });
    }
    if (path.endsWith(`/roles/${roleId}`) ||
        path.endsWith(`/roles/${roleId}/permissions`) ||
        path.endsWith("/permissions")) {
      obsoleteRequests.push(path);
      return json(route, []);
    }
    if (path.endsWith("/roles"))
      return json(route, { items: [role], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    return json(route, []);
  });

  await login(page);
  await page.goto("/dashboard/roles");
  await expect(page.getByRole("heading", { name: "Roles y permisos" })).toBeVisible();
  await page.getByRole("row", { name: /Supervisor/ }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("heading", { name: "Configurar rol" })).toBeVisible();
  await dialog.getByRole("button", { name: "Cerrar" }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByPlaceholder("Buscar rol")).toBeEditable();

  expect(workspaceRequests).toBe(1);
  expect(obsoleteRequests).toEqual([]);
});
