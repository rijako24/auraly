import { expect, test } from "@playwright/test";

test.use({ serviceWorkers: "block" });

for (const mode of ["Physical", "Both"] as const) {
  test(`la estación configura directamente su impresora en modo ${mode}`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const stationId = "33333333-3333-3333-3333-333333333333";
    const deviceId = "44444444-4444-4444-4444-444444444444";
    const user = { userId: "55555555-5555-5555-5555-555555555555", tenantId,
      username: "qa", roles: ["Administrador"], permissions: ["dashboard.read", "business_config.read", "businesses.update"] };
    await page.context().addCookies([{ name: "auth_token", value: "qa", url: baseURL!, httpOnly: true }]);
    await page.addInitScript(({ user, tenantId, businessId }) => {
      localStorage.setItem("selected_tenant_id", tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
      sessionStorage.setItem("auraly.pos.edge-token", "qa-local-session");
    }, { user, tenantId, businessId });
    let profileReads = 0;
    let configurationReads = 0;
    const writes: string[] = [];
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let value: unknown = [];
      if (path.endsWith("/auth/me")) value = user;
      else if (path.endsWith("/execution-context/tenants")) value = [{ tenantId, name: "QA" }];
      else if (path.endsWith("/execution-context/businesses")) value = [{ tenantId, businessId, name: "Restaurante" }];
      else if (path.endsWith("/execution-context/access")) value = { tenantId, businessId, roles: user.roles, permissions: user.permissions };
      else if (path.endsWith("/restaurant/stations")) value = { items: [{ stationId, businessId, code: "COCINA", name: "Cocina", kind: "Preparation", isActive: true }], page: 1, pageSize: 100, totalCount: 1 };
      else if (path.endsWith("/command-configuration")) {
        const configuration = { stationId, name: "Cocina", mode: "Virtual", printerDeviceId: null, printerProfileKey: null, rowVersion: "v1" };
        if (route.request().method() === "PUT") {
          writes.push("command");
          const request = route.request().postDataJSON();
          expect(request).toMatchObject({ mode, printerDeviceId: deviceId, printerProfileKey: stationId });
          value = { ...configuration, ...request, rowVersion: "v2" };
        } else { configurationReads++; value = configuration; }
      } else if (path.endsWith("/print-devices")) value = [{ deviceId, name: "Caja cocina" }];
      else value = { items: [], totalCount: 0, page: 1, pageSize: 100 };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(value) });
    });
    await page.route("http://127.0.0.1:47831/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let value: unknown;
      if (path.endsWith("/health")) value = { deviceId, status: "Ready" };
      else if (path.endsWith("/configuration/printers")) value = { installedPrinters: ["EPSON Cocina"], configuration: {} };
      else if (route.request().method() === "PUT") {
        writes.push("printer");
        value = route.request().postDataJSON();
        expect(value).toMatchObject({ key: stationId, printerName: "EPSON Cocina", paperWidthMillimeters: 58, copies: 2, cut: true });
      } else { profileReads++; value = []; }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(value) });
    });
    await page.goto("/dashboard/settings/restaurant#estaciones");
    await page.getByRole("button", { name: "Configurar comanda Cocina" }).click();
    const dialog = page.getByRole("dialog", { name: "Comanda de Cocina" });
    await expect(dialog.getByText("Perfil de impresora", { exact: true })).toHaveCount(0);
    expect(profileReads).toBe(0);
    await dialog.getByRole("combobox").first().click();
    await page.getByRole("option", { name: mode === "Both" ? "Pantalla e impresora" : "Solo impresora", exact: true }).click();
    await dialog.getByRole("combobox", { name: "Impresora Windows" }).click();
    await page.getByRole("option", { name: "EPSON Cocina" }).click();
    await dialog.getByRole("combobox", { name: "Tamaño de tirilla" }).click();
    await page.getByRole("option", { name: "58 mm" }).click();
    await dialog.getByLabel("Copias", { exact: true }).fill("2");
    await dialog.getByRole("button", { name: "Guardar comanda", exact: true }).click();
    await expect(dialog).toBeHidden();
    expect(writes).toEqual(["printer", "command"]);
    expect(profileReads).toBe(1);
    expect(configurationReads).toBe(1);
  });
}
