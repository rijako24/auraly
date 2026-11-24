import { expect, test, type Page } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
const partyId = "44444444-4444-4444-4444-444444444444", accountId = "55555555-5555-5555-5555-555555555555";

async function workspace(page: Page, baseURL: string, manageUsers = true) {
  const permissions = ["dashboard.read", "parties.read", "parties.update", "roles.read", ...(manageUsers ? ["users.update"] : [])];
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, username: "admin", firstName: "Prueba", lastName: "E2E", roles: ["Administrator"], permissions };
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL, httpOnly: true }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auraly:pwa-install-dismissed", "1");
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });
  let username = "elias.beltran", writes = 0, searches = 0, identityWrites = 0, detailReads = 0, rowVersion = "AAAA";
  let releaseSearch!: () => void;
  const searchGate = new Promise<void>(resolve => { releaseSearch = resolve; });
  let releaseUser!: () => void;
  const userGate = new Promise<void>(resolve => { releaseUser = resolve; });
  const identity = { partyId, partyType: "NaturalPerson", identificationCountryId: null, identificationTypeCode: "CC", identification: "100000001", displayName: "Elías Beltrán", firstName: "Elías", lastName: "Beltrán", email: "elias@example.test", phone: "3000000000", roles: ["User"], sites: [], primarySite: null, isActive: true, completionStatus: "Complete", rowVersion: "AAAA", userId: accountId };
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url()), path = url.pathname;
    let body: unknown = [];
    if (path.endsWith("/auth/me")) body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Auraly" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Auraly", timeZone: "America/Bogota" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: user.roles, permissions };
    else if (path.endsWith("/reference-options/party-role")) body = [{ id: "user-role", code: "User", label: "Usuario" }];
    else if (path.endsWith("/roles")) body = { items: [{ roleId: "role-1", name: "Administrativo", isActive: true }], totalCount: 1, page: 1, pageSize: 500 };
    else if (path.endsWith(`/users/${accountId}`) && route.request().method() === "PUT") {
      const value = route.request().postDataJSON().username;
      if (value === "ocupado") return route.fulfill({ status: 409, json: { detail: "El nombre de usuario ya está en uso.", message: "El nombre de usuario ya está en uso." } });
      writes++; await userGate; username = value; body = { userId: accountId, username };
    }
    else if (path.endsWith(`/parties/${partyId}`)) {
      if (route.request().method() === "PUT") {
        if (route.request().postDataJSON().rowVersion !== rowVersion) return route.fulfill({ status: 409, json: { detail: "El tercero cambió. Recarga sus datos." } });
        identityWrites++; rowVersion = `VERSION-${identityWrites}`;
      } else detailReads++;
      body = { ...identity, rowVersion, customer: null, supplier: null, seller: null, carrier: null, employee: null, user: { userId: accountId, username, email: identity.email, isActive: true, roles: [{ roleId: "role-1", roleName: "Administrativo", businessId }] } };
    }
    else if (path.endsWith("/commerce/v1/parties")) {
      if (url.searchParams.get("search")) { searches++; await searchGate; }
      body = { items: [identity], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  return { releaseSearch, releaseUser, writes: () => writes, searches: () => searches, identityWrites: () => identityWrites, detailReads: () => detailReads };
}

for (const width of [1366, 390]) {
  test(`tercero muestra y guarda el nombre de usuario sin cambiar contraseña ${width}`, async ({ page, baseURL }, info) => {
    await page.setViewportSize({ width, height: 844 });
    const state = await workspace(page, baseURL!);
    await page.goto("/dashboard/parties");
    await page.getByRole("button", { name: "Editar", exact: true }).first().click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("tab", { name: /^Usuario/ }).click();
    const username = dialog.getByLabel("Nombre de usuario", { exact: true });
    await expect(username).toHaveValue("elias.beltran");
    await username.fill(" ");
    await dialog.getByRole("button", { name: "Guardar tercero", exact: true }).click();
    await expect(dialog.getByRole("alert")).toHaveText("El nombre de usuario debe tener entre 1 y 100 caracteres.");
    expect(state.identityWrites()).toBe(0); expect(state.writes()).toBe(0);
    await username.fill("ocupado");
    await dialog.getByRole("button", { name: "Guardar tercero", exact: true }).click();
    await expect(dialog.getByRole("alert")).toHaveText("El nombre de usuario ya está en uso.");
    await expect(dialog.getByRole("button", { name: "Guardar tercero", exact: true })).toBeEnabled();
    expect(state.detailReads()).toBe(2); expect(state.identityWrites()).toBe(1);
    await username.fill("elias.transporte");
    await page.screenshot({ path: info.outputPath(`party-username-${width}.png`) });
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await dialog.getByRole("button", { name: "Guardar tercero", exact: true }).click();
    try {
      await expect(username).toBeDisabled();
      await expect(dialog.getByRole("button", { name: "Guardando...", exact: true })).toBeDisabled();
    } finally { state.releaseUser(); }
    await expect(username).toHaveValue("elias.transporte");
    await expect(username).toHaveAttribute("readonly", "");
    expect(state.writes()).toBe(1);
    expect(state.identityWrites()).toBe(2); expect(state.detailReads()).toBe(3);
  });

  test(`la búsqueda mantiene el indicador centrado durante toda la animación ${width}`, async ({ page, baseURL }, info) => {
    await page.setViewportSize({ width, height: 844 });
    const state = await workspace(page, baseURL!);
    await page.goto("/dashboard/parties");
    const input = page.getByPlaceholder(/Nombre, documento, correo o/);
    await expect(page.getByText("Elías Beltrán", { exact: true })).toBeVisible();
    await input.fill("Beltrán");
    const spinner = page.getByLabel("Buscando", { exact: true });
    await expect(spinner).toBeVisible();
    try {
      const offsets = await spinner.evaluate(async element => {
        const input = element.closest("label")!.querySelector("input")!;
        const samples: number[] = [];
        for (let index = 0; index < 40; index++) {
          await new Promise(requestAnimationFrame);
          const icon = element.getBoundingClientRect(), box = input.getBoundingClientRect();
          samples.push(icon.y + icon.height / 2 - box.y - box.height / 2);
        }
        return samples;
      });
      await info.attach("spinner-position", { body: JSON.stringify(offsets), contentType: "application/json" });
      expect(Math.max(...offsets.map(Math.abs))).toBeLessThanOrEqual(1);
      expect(state.searches()).toBe(1);
    } finally { state.releaseSearch(); }
    await expect(spinner).toBeHidden();
  });
}

test("sin permiso de usuarios el nombre es visible pero no editable", async ({ page, baseURL }) => {
  await workspace(page, baseURL!, false);
  await page.goto("/dashboard/parties");
  await page.getByRole("button", { name: "Editar", exact: true }).first().click();
  await page.getByRole("tab", { name: /^Usuario/ }).click();
  await expect(page.getByLabel("Nombre de usuario", { exact: true })).toHaveValue("elias.beltran");
  await expect(page.getByLabel("Nombre de usuario", { exact: true })).toHaveAttribute("readonly", "");
});
