import { expect, test } from "@playwright/test";

test("el comprobante manual busca cuentas por código o nombre en el servidor", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
    tenantKey: "@accounts", username: "accountant", firstName: "Prueba", lastName: "Contable",
    roles: [], permissions: ["accounting.read", "accounting.manual.create"] };
  const account = { accountId: "44444444-4444-4444-4444-444444444444", code: "519595", name: "Servicios de prueba",
    accountType: "Expense", allowsPosting: true, requiresParty: true, isActive: true, level: 6 };
  await page.context().addCookies([{ name: "auth_token", value: "accounts", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  const queries: URL[] = [];
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url()), path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path.endsWith("/accounting/accounts")) body = [account];
    else if (path.endsWith("/accounting/readiness")) body = { status: "Ready", blockingIssues: [] };
    else if (path.endsWith("/accounting/account-options")) {
      queries.push(url);
      body = { items: url.searchParams.get("search") === "inexistente" ? [] : [account], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/financial-traceability?new=1");
  expect(queries).toHaveLength(0);
  await page.getByRole("combobox", { name: "Seleccionar cuenta contable" }).first().click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill("519595");
  await expect.poll(() => queries.at(-1)?.searchParams.get("search")).toBe("519595");
  await page.getByRole("option", { name: /519595.*Servicios de prueba/ }).click();
  await expect(page.getByRole("combobox", { name: "Seleccionar cuenta contable" }).first()).toHaveText(/519595.*Servicios de prueba/);
  await page.getByRole("row", { name: "Partida 2", exact: true }).getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill("inexistente");
  await expect(page.getByText("No hay resultados.", { exact: true })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toHaveCount(0);
});
