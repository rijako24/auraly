import { expect, test, type Page } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const initial = { accountId: "44444444-4444-4444-4444-444444444444", code: "519595", name: "Servicios de prueba",
  accountType: "Expense", allowsPosting: true, requiresParty: false, isActive: true, level: "Subaccount", rowVersion: "AAAAAAAAAAE=" };

async function openPuc(page: Page, baseURL: string, canConfigure = true) {
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId,
    tenantKey: "@accounts", username: "accountant", firstName: "Prueba", lastName: "Contable",
    roles: [], permissions: ["accounting.read", ...(canConfigure ? ["accounting.configure"] : [])] };
  await page.context().addCookies([{ name: "auth_token", value: "accounts", url: baseURL, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  const state = { rows: [{ ...initial }], pages: [] as URL[], writes: [] as Record<string, unknown>[], failWrite: false, failList: false, pucReads: 0 };
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    let body: unknown = [], status = 200;
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path.endsWith("/accounting/readiness")) body = { status: "Ready", blockingIssues: [] };
    else if (path.endsWith("/accounting/puc-configuration")) { state.pucReads++; body = { formats: [], dimensions: [] }; }
    else if (path.endsWith("/reference-options/accounting-financial-function")) body = [{ id: "general", code: "General", label: "General", sortOrder: 1 }];
    else if (path.endsWith("/reference-options/accounting-balance-side")) body = [{ id: "debit", code: "Debit", label: "Débito", sortOrder: 1 }];
    else if (path.endsWith("/accounting/account-options")) body = { items: [{ ...initial, accountId: "55555555-5555-5555-5555-555555555555", code: "519598", name: "Gastos diversos", allowsPosting: false }], page: 1, totalPages: 1, totalCount: 1 };
    else if (path.endsWith("/reference-options/accounting-account-type")) body = [{ id: "expense", code: "Expense", label: "Gasto", sortOrder: 1 }];
    else if (request.method() === "PUT" && path.includes("/accounting/accounts/")) {
      const value = request.postDataJSON(); state.writes.push(value);
      if (state.failWrite) { status = 409; body = { title: "Conflicto", detail: "La cuenta cambió. Actualiza la lista antes de guardar." }; }
      else { state.rows[0] = { ...state.rows[0], ...value, rowVersion: "AAAAAAAAAAI=" }; body = state.rows[0]; }
    } else if (path.endsWith("/accounting/accounts") && request.method() === "POST") {
      const value = request.postDataJSON(); state.writes.push(value);
      const row = { ...initial, ...value, rowVersion: "AAAAAAAAAAM=" };
      state.rows.push(row); body = row; status = 201;
    } else if (path.endsWith("/accounting/accounts")) {
      if (url.searchParams.has("page")) {
        state.pages.push(url);
        const search = url.searchParams.get("search")?.toLowerCase() ?? "";
        const rows = state.rows.filter(row => `${row.code} ${row.name}`.toLowerCase().includes(search));
        body = { items: rows, page: 1, pageSize: Number(url.searchParams.get("pageSize") ?? 20), totalCount: rows.length, totalPages: rows.length ? 1 : 0 };
        if (state.failList) { status = 500; body = { title: "Error", detail: "No fue posible consultar el PUC." }; }
      } else body = state.rows;
    }
    await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Plan de cuentas", exact: true }).click();
  await expect(page.getByRole("cell", { name: initial.name, exact: true })).toBeVisible();
  return state;
}

test("el editor unificado muestra el error del catálogo y reintenta solo el recurso fallido", async ({ page, baseURL }) => {
  const state = await openPuc(page, baseURL!);
  let failed = true, functionReads = 0;
  await page.route("**/api/commerce/v1/reference-options/accounting-financial-function", async route => {
    functionReads++;
    await route.fulfill({ status: failed ? 500 : 200, contentType: "application/json",
      body: JSON.stringify(failed ? { detail: "No fue posible consultar las funciones contables." }
        : [{ id: "general", code: "General", label: "General", sortOrder: 1 }]) });
  });
  await page.getByRole("button", { name: "Nueva cuenta", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva cuenta PUC", exact: true });
  await expect(dialog.getByRole("alert")).toContainText("No fue posible consultar las funciones contables.");
  await expect(dialog.getByRole("button", { name: "Guardar formato", exact: true })).toHaveCount(0);
  const reads = functionReads;
  failed = false;
  await dialog.getByRole("button", { name: "Reintentar", exact: true }).click();
  await expect(dialog.getByLabel("Función financiera", { exact: true })).toBeVisible();
  expect(state.pucReads).toBe(0);
  expect(functionReads).toBe(reads + 1);
});

test("el PUC abre la cuenta desde la fila, guarda y reabre sin volver a consultar la lista", async ({ page, baseURL }, info) => {
  const state = await openPuc(page, baseURL!);
  const count = state.pages.length;
  await page.getByRole("cell", { name: initial.name, exact: true }).click();
  await expect(page.getByRole("dialog", { name: "Editar cuenta PUC" })).toBeVisible();
  await expect(page.getByLabel("Código PUC", { exact: true })).toBeDisabled();
  await page.getByLabel("Nombre", { exact: true }).fill("Servicios corregidos");
  await page.getByRole("checkbox", { name: "Exigir tercero en cada partida" }).check();
  await page.getByRole("button", { name: "Guardar cambios" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("cell", { name: "Servicios corregidos", exact: true })).toBeVisible();
  expect(state.pages).toHaveLength(count);
  expect(state.writes).toEqual([{ name: "Servicios corregidos", requiresParty: true, rowVersion: initial.rowVersion }]);
  const row = page.getByRole("row").filter({ hasText: "Servicios corregidos" });
  await row.focus(); await page.keyboard.press("Enter");
  await expect(page.getByLabel("Nombre", { exact: true })).toHaveValue("Servicios corregidos");
  await expect(page.getByRole("checkbox", { name: "Exigir tercero en cada partida" })).toBeChecked();
  await page.getByRole("button", { name: "Cancelar", exact: true }).click();
  await page.screenshot({ path: info.outputPath("puc-lista.png"), fullPage: true });
  await page.getByPlaceholder("Buscar cuenta por código o nombre").fill("inexistente");
  await expect.poll(() => state.pages.at(-1)?.searchParams.get("search")).toBe("inexistente");
  await expect(page.getByRole("cell", { name: "Servicios corregidos", exact: true })).toHaveCount(0);
});

test("el PUC crea desde el encabezado y conserva el formulario cuando hay conflicto de edición", async ({ page, baseURL }, info) => {
  const state = await openPuc(page, baseURL!);
  await page.getByRole("button", { name: "Nueva cuenta", exact: true }).click();
  await expect(page.getByRole("button", { name: "Crear cuenta", exact: true })).toBeDisabled();
  await expect(page.getByRole("checkbox", { name: "Exigir tercero en cada partida" })).toBeVisible();
  await expect(page.getByRole("checkbox", { name: "Exigir tercero en cada partida" })).toBeDisabled();
  await page.getByLabel("Código PUC", { exact: true }).fill("51959899");
  await expect(page.getByRole("checkbox", { name: "Exigir tercero en cada partida" })).toBeEnabled();
  await page.getByRole("checkbox", { name: "Exigir tercero en cada partida" }).check();
  await page.getByLabel("Nombre", { exact: true }).fill("Cuenta nueva");
  await page.getByLabel("Tipo de cuenta", { exact: true }).click();
  await page.getByRole("option", { name: "Gasto", exact: true }).click();
  await page.getByRole("combobox", { name: "Cuenta padre", exact: true }).click();
  await page.getByRole("option", { name: "519598 · Gastos diversos" }).click();
  await page.getByLabel("Función financiera", { exact: true }).click();
  await page.getByRole("option", { name: "General", exact: true }).click();
  await page.getByLabel("Naturaleza del saldo", { exact: true }).click();
  await page.getByRole("option", { name: "Débito", exact: true }).click();
  await expect(page.getByLabel("Cuenta bancaria vinculada", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Configurar auxiliares PUC", exact: true })).toHaveCount(0);
  await page.screenshot({ path: info.outputPath("puc-auxiliar-libre.png"), fullPage: true });
  const count = state.pages.length;
  await page.getByRole("button", { name: "Crear cuenta", exact: true }).click();
  await expect(page.getByRole("cell", { name: "Cuenta nueva", exact: true })).toBeVisible();
  expect(state.pages).toHaveLength(count + 1);
  expect(state.writes[0]).toMatchObject({ code: "51959899", requiresParty: true, semantics: { parentAccountId: "55555555-5555-5555-5555-555555555555", financialFunction: "General", balanceSide: "Debit", bankAccountId: null, businessId: null } });
  await page.getByRole("row").filter({ hasText: initial.name }).getByRole("button", { name: "Editar", exact: true }).click();
  state.failWrite = true;
  await page.getByLabel("Nombre", { exact: true }).fill("Corrección concurrente");
  await page.getByRole("button", { name: "Guardar cambios" }).click();
  await expect(page.getByRole("dialog").getByRole("alert")).toContainText("La cuenta cambió");
  await expect(page.getByLabel("Nombre", { exact: true })).toHaveValue("Corrección concurrente");
  await page.getByRole("button", { name: "Cancelar", exact: true }).click();
  await expect(page.getByRole("cell", { name: initial.name, exact: true })).toBeVisible();
  state.failList = true;
  await page.getByRole("button", { name: "Actualizar", exact: true }).click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText("No fue posible consultar el PUC");
  state.failList = false;
  await page.getByRole("button", { name: "Reintentar", exact: true }).click();
  await expect(page.getByRole("cell", { name: initial.name, exact: true })).toBeVisible();
});

test("el PUC de solo lectura no ofrece crear ni editar", async ({ page, baseURL }) => {
  await openPuc(page, baseURL!, false);
  await expect(page.getByRole("button", { name: "Nueva cuenta", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Editar", exact: true })).toHaveCount(0);
  await page.getByRole("cell", { name: initial.name, exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
});
