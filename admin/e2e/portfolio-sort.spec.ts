import { expect, test } from "@playwright/test";

test("cartera ordena en el servidor antes de paginar", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const user = { userId: "55555555-5555-5555-5555-555555555555", tenantId,
    tenantKey: "@portfolio-sort", username: "portfolio-sort", firstName: "Prueba", lastName: "Cartera",
    roles: [], permissions: ["payables.read", "receivables.read"] };
  await page.context().addCookies([{ name: "auth_token", value: "portfolio-sort", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });

  const requests: Array<{ path: string; by: string | null; direction: string | null; page: string | null }> = [];
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (["/payables/suppliers", "/receivables/customers", "/payables", "/receivables", "/payable-payments", "/receivable-payments"].some(suffix => path.endsWith(suffix))) {
      requests.push({ path, by: url.searchParams.get("sortBy"), direction: url.searchParams.get("sortDirection"), page: url.searchParams.get("page") });
      body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
        totalOutstanding: 0, totalOverdue: 0, totalInvoiceCount: 0, totalSupplierCredit: 0 };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/dashboard/payables");
  await expect(page.getByRole("tab", { name: "Proveedores" })).toBeVisible();
  await page.getByRole("button", { name: "Ordenar Saldo" }).click();
  await expect.poll(() => requests.at(-1)).toMatchObject({ by: "outstandingAmount", direction: "asc", page: "1" });
  await page.getByRole("button", { name: "Ordenar Saldo" }).click();
  await expect.poll(() => requests.at(-1)).toMatchObject({ by: "outstandingAmount", direction: "desc", page: "1" });
  await page.getByRole("tab", { name: "Facturas" }).click();
  await page.getByRole("columnheader", { name: /Vencimiento/ }).click();
  await expect.poll(() => requests.at(-1)).toMatchObject({ by: "dueDate", direction: "desc", page: "1" });
  await page.getByRole("tab", { name: "Pagos" }).click();
  await page.getByRole("button", { name: "Ordenar Total" }).click();
  await expect.poll(() => requests.at(-1)).toMatchObject({ by: "totalAmount", direction: "asc", page: "1" });

  await page.goto("/dashboard/receivables");
  await expect(page.getByRole("tab", { name: "Clientes" })).toBeVisible();
  await page.getByRole("button", { name: "Ordenar Cliente" }).click();
  await expect.poll(() => requests.at(-1)).toMatchObject({ by: "name", direction: "desc", page: "1" });
  await page.getByRole("tab", { name: "Facturas" }).click();
  await page.getByRole("columnheader", { name: /Vencimiento/ }).click();
  await expect.poll(() => requests.at(-1)).toMatchObject({ by: "dueDate", direction: "desc", page: "1" });
});
