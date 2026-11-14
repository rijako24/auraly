import { expect, test } from "@playwright/test";

test.use({ serviceWorkers: "block" });

for (const { mode, failCommandOnce, creating, deviceMatches } of [
  { mode: "Physical", failCommandOnce: false, creating: false, deviceMatches: true }, { mode: "Both", failCommandOnce: false, creating: false, deviceMatches: true },
  { mode: "Both", failCommandOnce: true, creating: false, deviceMatches: true }, { mode: "Physical", failCommandOnce: false, creating: true, deviceMatches: true },
  { mode: "Both", failCommandOnce: true, creating: true, deviceMatches: true },
  { mode: "Physical", failCommandOnce: false, creating: false, deviceMatches: false },
  { mode: "Both", failCommandOnce: true, creating: false, deviceMatches: false },
  { mode: "Both", failCommandOnce: false, creating: true, deviceMatches: false },
  { mode: "Physical", failCommandOnce: false, creating: false, deviceMatches: null },
] as const) {
  test(`la estación configura directamente su impresora en modo ${mode}, reintento=${failCommandOnce}, nueva=${creating}, mismo equipo=${deviceMatches}`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    let stationId = "33333333-3333-3333-3333-333333333333";
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
    let stationCreated = false;
    const writes: string[] = [];
    const localReads: string[] = [];
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let value: unknown = [];
      if (path.endsWith("/auth/me")) value = user;
      else if (path.endsWith("/execution-context/tenants")) value = [{ tenantId, name: "QA" }];
      else if (path.endsWith("/execution-context/businesses")) value = [{ tenantId, businessId, name: "Restaurante" }];
      else if (path.endsWith("/execution-context/access")) value = { tenantId, businessId, roles: user.roles, permissions: user.permissions };
      else if (path.endsWith("/reference-options/restaurant-station-kind")) value = [{ id: "preparation", code: "Preparation", label: "Preparación" }, { id: "pickup", code: "Pickup", label: "Retiro" }];
      else if (path.endsWith("/reference-options/restaurant-command-mode")) value = [{ id: "virtual", code: "Virtual", label: "Solo pantalla" }, { id: "both", code: "Both", label: "Pantalla e impresora" }, { id: "physical", code: "Physical", label: "Solo impresora" }];
      else if (path.endsWith("/restaurant/stations")) {
        if (route.request().method() === "POST") {
          const request = route.request().postDataJSON();
          stationId = request.stationId;
          expect(request.code).toBe(stationId.replaceAll("-", ""));
          stationCreated = true;
          writes.push("station");
          value = { ...request, isActive: true, rowVersion: "station-v1" };
        } else {
          const items = creating && !stationCreated ? [] : [{ stationId, businessId, code: "COCINA", name: "Cocina", sortOrder: 0, kind: "Preparation", targetMinutes: null, isActive: true, rowVersion: "station-v1" }];
          value = { items, page: 1, pageSize: 100, totalCount: items.length };
        }
      }
      else if (path.endsWith("/command-configuration")) {
        const configuration = { stationId, name: "Cocina", mode: "Virtual", printerDeviceId: null, printerProfileKey: null, rowVersion: "v1" };
        if (route.request().method() === "PUT") {
          writes.push("command");
          if (failCommandOnce && writes.filter(write => write === "command").length === 1)
            return route.fulfill({ status: 409, contentType: "application/json", body: JSON.stringify({ detail: "La comanda cambió. Recarga antes de guardar." }) });
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
      if (route.request().method() === "GET") localReads.push(path);
      let value: unknown;
      if (path.endsWith("/health")) value = { deviceId: deviceMatches ? deviceId : deviceMatches === false ? "88888888-8888-8888-8888-888888888888" : null, status: "Ready" };
      else if (path.endsWith("/configuration/printers")) value = { installedPrinters: ["EPSON Cocina"], configuration: {} };
      else if (route.request().method() === "PUT") {
        writes.push("printer");
        value = route.request().postDataJSON();
        expect(value).toMatchObject({ key: stationId, printerName: "EPSON Cocina", paperWidthMillimeters: 58, copies: 2, cut: true });
      } else { profileReads++; value = []; }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(value) });
    });
    await page.goto("/dashboard/settings/restaurant#estaciones");
    await page.getByRole("button", { name: creating ? "Nueva estación" : "Editar estación Cocina", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: creating ? "Nueva estación" : "Editar estación Cocina", exact: true });
    if (creating) await dialog.getByLabel("Nombre", { exact: true }).fill("Cocina");
    await expect(dialog.getByLabel("Código", { exact: true })).toHaveCount(0);
    await expect(dialog.getByText("Perfil de impresora", { exact: true })).toHaveCount(0);
    expect(profileReads).toBe(0);
    await dialog.getByRole("combobox", { name: "Salida de comanda" }).click();
    await page.getByRole("option", { name: mode === "Both" ? "Pantalla e impresora" : "Solo impresora", exact: true }).click();
    await expect(dialog.getByRole("combobox", { name: "Impresora Windows" })).toBeVisible();
    await expect(dialog.getByText(/Para seleccionar o cambiar la impresora|Abre esta configuración en el equipo de impresión/)).toHaveCount(0);
    await dialog.getByRole("combobox", { name: "Impresora Windows" }).click();
    await page.getByRole("option", { name: "EPSON Cocina" }).click();
    if (!deviceMatches) {
      await dialog.getByRole("combobox", { name: "Equipo que imprime" }).click();
      await page.getByRole("option", { name: "Caja cocina", exact: true }).click();
      await expect(dialog.getByRole("combobox", { name: "Impresora Windows" })).toContainText("EPSON Cocina");
    }
    await dialog.getByRole("combobox", { name: "Tamaño de tirilla" }).click();
    await page.getByRole("option", { name: "58 mm" }).click();
    await dialog.getByLabel("Copias", { exact: true }).fill("2");
    await dialog.getByRole("button", { name: "Guardar estación", exact: true }).click();
    if (failCommandOnce) {
      await expect(dialog.getByRole("alert")).toContainText("La impresora quedó guardada");
      await dialog.getByRole("button", { name: "Guardar estación", exact: true }).click();
    }
    await expect(dialog).toBeHidden();
    expect(writes).toEqual([...(creating ? ["station"] : []), "printer", "command", ...(failCommandOnce ? ["command"] : [])]);
    expect(profileReads).toBe(1);
    expect(localReads.sort()).toEqual(["/edge/v1/configuration/printers",
      "/edge/v1/configuration/restaurant-command-printers", "/edge/v1/health"].sort());
    expect(configurationReads).toBe(1);
  });
}
