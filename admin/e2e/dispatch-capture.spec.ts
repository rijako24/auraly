import { expect, test } from "@playwright/test";

test.use({ timezoneId: "Asia/Tokyo" });

test("despacho carga transportadores y documentos por sede y fechas, con varias bodegas y sin consultas ocultas", async ({ page, baseURL }) => {
  test.setTimeout(120_000);
  await page.clock.setFixedTime(new Date("2026-11-17T01:30:00Z"));
  const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "dispatch-test",
    firstName: "Prueba", lastName: "Despachos", roles: [], permissions: ["dispatches.read", "dispatches.read-all", "dispatches.create", "dispatches.attach-documents"] };
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });
  const candidateQueries: URLSearchParams[] = [], driverQueries: URLSearchParams[] = [];
  let optionsReads = 0, listReads = 0, createBody: Record<string, unknown> | null = null;
  const documents = [0, 1].map(index => ({ documentId: `source-${index}`, documentType: index ? "SalesInvoice" : "SalesReceipt",
    documentNumber: index ? "FV-200" : "CV-100", issuedAt: "2026-11-16T15:00:00Z", warehouseId: `warehouse-${index}`,
    warehouseName: `Bodega ${index + 1}`, customerName: "Cliente del despacho", sellerName: "Vendedor", lineCount: 1, documentTotal: 1000 }));
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url()), path = url.pathname;
    let body: unknown = [];
    if (path.endsWith("/auth/me")) body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede seleccionada", timeZone: "America/Bogota" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
    else if (path.endsWith("/dispatches/options")) { optionsReads++; body = { warehouses: [], routes: [], drivers: [] }; }
    else if (path.endsWith("/dispatches/drivers")) {
      driverQueries.push(url.searchParams);
      body = { items: [{ userId: "driver-user", name: "Transportador habilitado" }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 };
    } else if (path.endsWith("/dispatches/candidates")) {
      candidateQueries.push(url.searchParams);
      expect(url.searchParams.get("warehouseId")).toBeNull();
      expect(url.searchParams.get("dateRangeVersion")).toBe("2");
      expect(url.searchParams.get("pageSize")).toBe("20");
      body = { items: url.searchParams.get("page") === "2" ? [] : documents, page: Number(url.searchParams.get("page")), pageSize: 20, totalCount: 22, totalPages: 2 };
    } else if (path.endsWith("/dispatches") && route.request().method() === "POST") {
      createBody = route.request().postDataJSON();
      body = { dispatchId: "dispatch-created", dispatchNumber: "DSP-TEST", status: "Draft", rowVersion: "AAAAAAAAAAA=", documentCount: 2, lineCount: 2, expectedQuantity: 2 };
    } else if (path.endsWith("/dispatches")) {
      listReads++; body = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 };
    } else if (path.endsWith("/dispatches/dispatch-created")) {
      body = { dispatchId: "dispatch-created", businessId, dispatchNumber: "DSP-TEST", scheduledDate: "2026-11-16", driverName: "Transportador habilitado",
        status: "Draft", documents: [], lines: [], shortages: [], rowVersion: "AAAAAAAAAAA=" };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/dispatches");
  await expect(page.getByRole("button", { name: "Crear despacho", exact: true })).toBeEnabled();
  expect(optionsReads).toBe(0); expect(candidateQueries).toHaveLength(0); expect(driverQueries).toHaveLength(0);
  await page.getByRole("button", { name: "Crear despacho", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Nuevo despacho de mercancía" });
  await expect(dialog.getByText("CV-100 · Cliente del despacho")).toBeVisible();
  expect(candidateQueries[0].get("from")).toBe("2026-11-16"); expect(candidateQueries[0].get("to")).toBe("2026-11-16");
  await expect(dialog.getByText("Bodega", { exact: true })).toHaveCount(0);
  await dialog.getByRole("combobox", { name: "Transportador del despacho" }).click();
  await page.getByRole("option", { name: "Transportador habilitado", exact: true }).click();
  expect(driverQueries).toHaveLength(1); expect(driverQueries[0].get("userId")).toBeNull();
  await dialog.getByRole("checkbox").nth(0).check(); await dialog.getByRole("checkbox").nth(1).check();
  await expect(dialog.getByText("2 seleccionados", { exact: false })).toBeVisible();
  expect(candidateQueries).toHaveLength(1);
  await dialog.getByLabel("Documentos desde").click();
  await page.getByRole("button", { name: "15 de noviembre de 2026", exact: true }).click();
  await expect.poll(() => candidateQueries.at(-1)?.get("from")).toBe("2026-11-15");
  await expect(dialog.getByText("0 seleccionados", { exact: false })).toBeVisible();
  await dialog.getByRole("checkbox").nth(0).check(); await dialog.getByRole("checkbox").nth(1).check();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
  await dialog.getByRole("button", { name: "Crear despacho", exact: true }).click();
  await expect.poll(() => createBody).not.toBeNull();
  expect(createBody).toEqual(expect.objectContaining({ businessId, driverUserId: "driver-user", sourceDocumentIds: ["source-0", "source-1"] }));
  expect(createBody).not.toHaveProperty("warehouseId");
  expect(listReads).toBe(1); expect(optionsReads).toBe(1); expect(driverQueries).toHaveLength(1);
  expect(candidateQueries).toHaveLength(2);
});
