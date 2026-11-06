import { expect, test } from "@playwright/test";

test.use({ serviceWorkers: "block" });

for (const installed of [false, true]) {
  test(`la sede restaurante seleccionada prevalece sobre venta directa recordada, instalada=${installed}`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const directBusinessId = "33333333-3333-3333-3333-333333333333";
    const warehouseId = "44444444-4444-4444-4444-444444444444";
    const user = { userId: "55555555-5555-5555-5555-555555555555", tenantId, username: "qa",
      roles: ["Administrador"], permissions: ["sales.create", "restaurant.attend"] };
    const restaurant = { businessId, warehouseId, businessName: "Restaurante", warehouseName: "Principal", pointOfSaleMode: "RestaurantBar", hasDianDocumentQuota: false };
    await page.context().addCookies([{ name: "auth_token", value: "qa", url: baseURL!, httpOnly: true }]);
    await page.addInitScript(({ user, tenantId, businessId, directBusinessId, warehouseId, installed }) => {
      localStorage.setItem("selected_tenant_id", tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
      localStorage.setItem(`auraly.pos.sales-workspace:${tenantId}:${user.userId}`, `${directBusinessId}:${warehouseId}`);
      if (installed) sessionStorage.setItem("auraly.pos.edge-token", "qa");
    }, { user, tenantId, businessId, directBusinessId, warehouseId, installed });
    const localWrites: string[] = [];
    const selections: string[] = [];
    let bootstrapReads = 0;
    await page.route("http://127.0.0.1:47831/**", async route => {
      if (route.request().method() !== "GET") localWrites.push(route.request().url());
      if (route.request().url().endsWith("/configuration/printers")) {
        await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({
          installedPrinters: ["EPSON"], configuration: { posOutputFormat: "Receipt", orderOutputFormat: "Receipt",
            receiptMode: "WindowsRaw", orderMode: "WindowsRaw", receiptPaperWidthMillimeters: 80, posPrinterName: "EPSON", orderPrinterName: "EPSON" },
        }) });
        return;
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({
        status: "Ready", identityReady: true, businessId: directBusinessId, warehouseId,
        pointOfSaleMode: "DirectSale", userId: user.userId, workSessionId: null,
      }) });
    });
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let value: unknown = [];
      if (path.endsWith("/auth/me")) value = user;
      else if (path.endsWith("/workspace/bootstrap")) { bootstrapReads++; value = { tenantId, userId: user.userId, userDisplayName: "QA", tenantName: "QA",
        options: [restaurant, { ...restaurant, businessId: directBusinessId, pointOfSaleMode: "DirectSale" }] }; }
      else if (path.endsWith("/workspace/select")) { selections.push(route.request().postDataJSON().businessId); value = restaurant; }
      else if (path.endsWith("/work-sessions/current")) value = { workSessionId: "qa-session" };
      else if (path.endsWith("/rooms") || path.endsWith("/tables")) value = { items: [], totalCount: 0 };
      else if (path.endsWith("/occupancy")) value = [];
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(value) });
    });
    await page.goto("/pos");
    await expect(page).toHaveURL(/\/pos\/restaurant$/);
    await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?" })).toBeVisible();
    expect(localWrites).toEqual([]);
    expect(bootstrapReads).toBe(1);
    expect(selections).toEqual([businessId]);
    await page.getByRole("button", { name: "Periféricos", exact: true }).click();
    const peripherals = page.getByRole("dialog", { name: "Periféricos de restaurante" });
    await expect(peripherals).toBeVisible();
    await expect(peripherals.getByRole("link", { name: /estaciones/i })).toBeVisible();
    await expect(peripherals.getByText("Báscula", { exact: true })).toHaveCount(0);
  });
}
