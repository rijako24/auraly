import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";

test("auxiliar pagina más de 200 movimientos, conserva totales y comparte el catálogo de centros", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const centerId = "33333333-3333-3333-3333-333333333333";
  const partyId = "66666666-6666-6666-6666-666666666666";
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId,
    tenantKey: "AUXILIAR", username: "contador", firstName: "Contador", lastName: "Prueba",
    roles: [], permissions: ["dashboard.read", "accounting.read"] };
  const requests: URL[] = [];
  const printRequests: URL[] = [];
  let centerReads = 0;
  let fail = false;
  let printFail = true;
  await page.context().addCookies([{ name: "auth_token", value: "auxiliary-test", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auraly:pwa-install-dismissed", "1");
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa del auxiliar" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede principal" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path.endsWith("/readiness")) body = { status: "Ready", blockingIssues: [] };
    else if (path.endsWith("/tenants/branding")) body = { tenantId, displayName: "Empresa del auxiliar", legalName: "Empresa del auxiliar", nit: "900123456", verificationDigit: "0", logoUrl: null };
    else if (path.endsWith("/parties/role-options")) body = { items: [{ partyId, roleId: partyId,
      role: "Supplier", displayName: "Proveedor principal", identification: "900123456" }],
      page: 1, pageSize: 10, totalCount: 1, totalPages: 1 };
    else if (path.endsWith("/cost-centers")) {
      centerReads++;
      body = [{ costCenterId: centerId, code: "CENTRO", name: "Centro contable", isActive: true }];
    } else if (path.endsWith("/trial-balance")) body = [{ accountCode: "13300501", accountName: "Anticipos a proveedores",
      level: "Auxiliary", openingDebit: 50, openingCredit: 0, debit: 201, credit: 0, closingDebit: 251, closingCredit: 0 }];
    else if (path.endsWith("/account-movements") || path.endsWith("/account-movements/print")) {
      const printing = path.endsWith("/print");
      (printing ? printRequests : requests).push(url);
      if (printing && printFail) return route.fulfill({ status: 400, contentType: "application/problem+json",
        body: JSON.stringify({ detail: "El libro auxiliar supera el límite. Reduce el período o el rango de cuentas." }) });
      if (fail) return route.fulfill({ status: 503, contentType: "application/problem+json",
        body: JSON.stringify({ detail: "No se pudo consultar el libro." }) });
      const currentPage = printing ? 1 : Number(url.searchParams.get("page"));
      const range = url.searchParams.get("accountFrom") === "11050501";
      body = { page: currentPage, pageSize: printing ? 5000 : 50, totalCount: range ? 203 : 201, totalPages: printing ? 1 : 5,
        accounts: [
          ...(range ? [{ accountCode: "11050501", accountName: "Caja principal", openingBalance: 100,
            debit: 25, credit: 5, closingBalance: 120, movementCount: 2, baseAmount: null }] : []),
          { accountCode: "13300501", accountName: "Anticipos a proveedores",
            openingBalance: 50, debit: 201.1234, credit: 0, closingBalance: 251.1234, movementCount: 201, baseAmount: null }],
        items: Array.from({ length: printing ? range ? 203 : 201 : 50 }, (_, index) => {
          const number = (currentPage - 1) * 50 + index + 1;
          const cash = range && number <= 2;
          return { entryId: `entry-${number}`, lineNumber: 1, entryNumber: `CONT-${number.toString().padStart(3,"0")}`,
            accountCode: cash ? "11050501" : "13300501", accountName: cash ? "Caja principal" : "Anticipos a proveedores",
            sourceDocumentId: "55555555-5555-5555-5555-555555555555", sourceDocumentType: "ManualAccountingVoucher",
            sourceDocumentNumber: `DOC-${number}`, occurredAt: "2026-10-20T12:00:00Z", reference: "FACTURA-ORIGINAL",
            description: "Movimiento del auxiliar", partyName: "Proveedor principal", partyIdentification: "900123456",
            debit: cash ? number === 1 ? 25 : 0 : 1,
            credit: cash && number === 2 ? 5 : 0,
            balance: cash ? number === 1 ? 125 : 120 : 50 + number - (range ? 2 : 0), baseAmount: null,
            costCenterId: centerId, costCenterCode: "CENTRO", costCenterName: "Centro contable" };
        }) };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Balance de prueba", exact: true }).click();
  expect(requests).toHaveLength(0);
  await page.getByRole("button", { name: "13300501 · Anticipos a proveedores", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "13300501 · Anticipos a proveedores" });
  await expect(dialog.getByText("201 movimientos", { exact: true })).toBeVisible();
  await expect(dialog.getByText("CONT-001", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("cell", { name: "Proveedor principal", exact: true })).toHaveCount(50);
  await expect(dialog.getByRole("cell", { name: "900123456", exact: true })).toHaveCount(50);
  await expect(dialog.getByText("DOC-1", { exact: true })).toBeVisible();
  await expect(dialog.getByText("FACTURA-ORIGINAL", { exact: true })).toHaveCount(50);
  await expect(dialog.getByText("Nuevo saldo", { exact: true }).locator("..")).toContainText("251,1234");
  expect(requests).toHaveLength(1);
  expect(requests[0].searchParams.get("pageSize")).toBe("50");
  expect(requests[0].searchParams.get("accountRootCode")).toBe("13300501");
  expect(requests[0].searchParams.get("accountFrom")).toBeNull();
  expect(requests[0].searchParams.get("accountTo")).toBeNull();
  expect(centerReads).toBe(1);
  await dialog.getByRole("button", { name: "Siguiente", exact: true }).click();
  await expect(dialog.getByText("CONT-051", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Página 2 de 5", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Nuevo saldo", { exact: true }).locator("..")).toContainText("251,1234");
  expect(requests).toHaveLength(2);
  await dialog.getByRole("combobox", { name: "Filtrar auxiliar por centro" }).click();
  await page.getByRole("option", { name: "CENTRO · Centro contable", exact: true }).click();
  await expect(dialog.getByText("CONT-001", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Página 1 de 5", { exact: true })).toBeVisible();
  expect(requests).toHaveLength(3);
  expect(requests[2].searchParams.get("page")).toBe("1");
  expect(requests[2].searchParams.get("costCenterId")).toBe(centerId);
  await dialog.getByRole("combobox", { name: "Filtrar auxiliar por tercero" }).click();
  await page.getByRole("option", { name: /Proveedor principal/ }).click();
  await expect.poll(()=>requests.length).toBe(4);
  expect(requests[3].searchParams.get("partyId")).toBe(partyId);
  expect(requests[3].searchParams.get("page")).toBe("1");
  fail = true;
  await dialog.getByRole("button", { name: "Actualizar auxiliar" }).click();
  await expect(dialog.getByRole("alert")).toContainText("No se pudo cargar el auxiliar");
  expect(requests).toHaveLength(5);
  fail = false;
  await dialog.getByRole("button", { name: "Reintentar auxiliar" }).click();
  await expect(dialog.getByRole("alert")).toHaveCount(0);
  expect(requests).toHaveLength(6);
  await page.keyboard.press("Escape");
  await expect(dialog).not.toBeVisible();
  await page.getByRole("button", { name: "Libros y estados", exact: true }).click();
  expect(requests).toHaveLength(6);
  expect(centerReads).toBe(1);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "Balance de prueba", exact: true }).click();
  await page.getByRole("button", { name: "13300501 · Anticipos a proveedores", exact: true }).click();
  await expect(dialog).toBeVisible();
  const bounds=await dialog.boundingBox();
  expect(bounds!.y).toBeGreaterThanOrEqual(0);
  expect(bounds!.y+bounds!.height).toBeLessThanOrEqual(844);
  await dialog.getByRole("button", { name: "Siguiente", exact: true }).scrollIntoViewIfNeeded();
  await expect(dialog.getByRole("button", { name: "Siguiente", exact: true })).toBeVisible();
  expect(requests).toHaveLength(6);
  await page.keyboard.press("Escape");
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole("button", { name: "Libro auxiliar por cuentas", exact: true }).click();
  const ranged = page.getByRole("dialog", { name: "Libro auxiliar por cuenta", exact: true });
  await expect(ranged.getByText("201 movimientos", { exact: true })).toBeVisible();
  expect(requests).toHaveLength(7);
  await ranged.getByRole("combobox", { name: "Filtrar auxiliar por centro" }).click();
  await page.getByRole("option", { name: "CENTRO · Centro contable", exact: true }).click();
  await expect.poll(()=>requests.length).toBe(8);
  await ranged.getByRole("combobox", { name: "Filtrar auxiliar por tercero" }).click();
  await page.getByRole("option", { name: /Proveedor principal/ }).click();
  await expect.poll(()=>requests.length).toBe(9);
  await ranged.getByRole("textbox", { name: "Cuenta desde", exact: true }).fill("11050501");
  await ranged.getByRole("textbox", { name: "Cuenta hasta", exact: true }).fill("13300501");
  expect(requests).toHaveLength(9); // Capturing a range does not query on every keystroke.
  await ranged.getByRole("button", { name: "Aplicar rango de cuentas", exact: true }).click();
  await expect(ranged.getByText("203 movimientos", { exact: true })).toBeVisible();
  expect(requests).toHaveLength(10);
  expect(requests[9].searchParams.get("accountFrom")).toBe("11050501");
  expect(requests[9].searchParams.get("accountTo")).toBe("13300501");
  expect(requests[9].searchParams.get("accountRootCode")).toBeNull();
  expect(requests[9].searchParams.get("page")).toBe("1");
  expect(requests[9].searchParams.get("partyId")).toBe(partyId);
  expect(requests[9].searchParams.get("costCenterId")).toBe(centerId);
  const summary = ranged.getByRole("table", { name: "Resumen por cuenta de todo el período" });
  await expect(summary.getByRole("cell", { name: "11050501 · Caja principal", exact: true })).toBeVisible();
  await expect(summary.getByRole("cell", { name: "13300501 · Anticipos a proveedores", exact: true })).toBeVisible();
  await expect(ranged.getByRole("columnheader", { name: /11050501 · Caja principal.*Movimientos de esta página/ })).toBeVisible();
  await ranged.screenshot({ path: test.info().outputPath("accounting-auxiliary-range.png") });
  await ranged.getByRole("button", { name: "Siguiente", exact: true }).click();
  await expect(ranged.getByText("Página 2 de 5", { exact: true })).toBeVisible();
  await expect(summary).toContainText("251,1234");
  expect(requests).toHaveLength(11);
  await ranged.getByRole("button", { name: "Imprimir / exportar libro completo", exact: true }).click();
  await expect(page.getByText(/El libro auxiliar supera el límite/)).toBeVisible();
  await expect(ranged.getByRole("textbox", { name: "Cuenta desde", exact: true })).toHaveValue("11050501");
  await expect(ranged.getByText("Página 2 de 5", { exact: true })).toBeVisible();
  expect(printRequests).toHaveLength(1);
  printFail = false;
  await ranged.getByRole("button", { name: "Imprimir / exportar libro completo", exact: true }).click();
  const report = page.getByRole("dialog", { name: "Libro auxiliar para imprimir y exportar", exact: true });
  await expect(report).toBeVisible();
  await expect(report.getByText("NIT 900123456-0", { exact: true })).toBeVisible();
  await expect(report.getByPlaceholder("Buscar dentro del reporte")).toHaveCount(0);
  await expect(report.getByText("DOC-203 · FACTURA-ORIGINAL", { exact: true })).toBeVisible();
  await expect(report.getByText("Total cuenta 13300501", { exact: true })).toBeVisible();
  await expect(report.getByText("No registrada", { exact: true })).toHaveCount(205);
  expect(printRequests).toHaveLength(2);
  expect(printRequests[1].searchParams.get("partyId")).toBe(partyId);
  expect(printRequests[1].searchParams.get("costCenterId")).toBe(centerId);
  expect(printRequests[1].searchParams.get("accountFrom")).toBe("11050501");
  expect(printRequests[1].searchParams.get("page")).toBeNull();
  expect(requests).toHaveLength(11);
  const csvEvent = page.waitForEvent("download");
  await report.getByRole("button", { name: "Exportar CSV", exact: true }).click();
  const csv = await csvEvent;
  const csvText = await readFile((await csv.path())!, "utf8");
  expect(csvText).toContain("CONT-203");
  expect(csvText).toContain("Total cuenta 13300501");
  for (const [button, extension] of [["Descargar PDF", "pdf"], ["Exportar Excel", "xlsx"]] as const) {
    const event = page.waitForEvent("download");
    await report.getByRole("button", { name: button, exact: true }).click();
    const download = await event;
    expect(download.suggestedFilename()).toMatch(new RegExp(`\\.${extension}$`));
    await download.saveAs(`../work/libro-auxiliar-fixture.${extension}`);
  }
  await page.evaluate(() => { window.print = () => document.documentElement.setAttribute("data-report-printed", "1"); });
  await report.getByRole("button", { name: "Imprimir", exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-report-printed", "1");
  await page.pdf({ path: "../work/libro-auxiliar-impresion-fixture.pdf", preferCSSPageSize: true, printBackground: true });
  expect(printRequests).toHaveLength(2);
  expect(requests).toHaveLength(11);
  await report.getByRole("button", { name: "Volver", exact: true }).click();
  await expect(ranged.getByText("Página 2 de 5", { exact: true })).toBeVisible();
  expect(requests).toHaveLength(11);
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Libros y estados", exact: true }).click();
  expect(requests).toHaveLength(11);
  expect(centerReads).toBe(1);
});
