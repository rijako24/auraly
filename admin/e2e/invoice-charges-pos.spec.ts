import { expect, test } from "@playwright/test";
import type { AddInvoiceCharge, AppliedInvoiceCharge } from "../src/services/api/invoice-charges";

test("cargos POS: proveedor único, valor manual, total autoritativo y eliminación sin recargar el catálogo", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222", warehouseId = "33333333-3333-3333-3333-333333333333";
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, tenantKey: "TEST", username: "cajero", firstName: "Cajero", lastName: "Prueba", roles: ["ADMIN"], permissions: ["pos.sales.create"] };
  const workspace = { businessId, warehouseId, businessName: "Sede prueba", warehouseName: "Principal", warehouseCode: "B01", warehouseAllowsNegativeStockSales: true, hasActiveEdgeEnrollment: false };
  const supplier = { supplierId: "55555555-5555-5555-5555-555555555555", name: "Domiciliario de prueba", identification: "TEST", defaultPaymentDueDays: 0, isActive: true };
  const charge = { chargeId: "66666666-6666-6666-6666-666666666666", businessId, version: 1, code: "AGOTADOS", name: "Domicilio · Agotados", calculationMode: "Manual", value: 5000, inclusionMode: "Never", isActive: true, suppliers: [supplier] };
  const draft = { draftId: "draft", ...workspace, userId: user.userId, workSessionId: "session", status: "Active", version: 1,
    lines: [{ lineId: "line", productId: "product", productCode: "PRD", description: "Producto de prueba", unitCode: "EA", taxCode: "01", taxRate: 0, quantity: 1, baseUnitPrice: 80000, publicUnitPrice: 80000, unitPrice: 80000, currencyCode: "COP", priceSource: "Public", discount: 0, documentUnitCost: 10000, net: 80000, tax: 0, total: 80000 }],
    charges: [] as AppliedInvoiceCharge[], untaxedAmount: 80000, taxAmount: 0, payableAmount: 80000 };
  let catalogReads = 0, draftReads = 0;
  const mutations: Array<AddInvoiceCharge & { expectedVersion: number }> = [];
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, warehouseId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    localStorage.setItem(`auraly.pos.sales-workspace:${tenantId}:${user.userId}`, `${businessId}:${warehouseId}`);
  }, { tenantId, businessId, warehouseId, user });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/workspace/bootstrap")) body = { tenantId, tenantName: "Prueba", userId: user.userId, userDisplayName: "Cajero Prueba", options: [workspace], canEnrollPosDevice: false, activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0 };
    else if (path.endsWith("/workspace/options")) body = [workspace];
    else if (path.endsWith("/workspace/select")) body = workspace;
    else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "session" };
    else if (path.endsWith("/drafts/active")) { draftReads++; body = draft; }
    else if (path.endsWith("/invoice-charges")) { catalogReads++; body = { items: [charge], page: 1, pageSize: 100, totalPages: 1, totalCount: 1 }; }
    else if (path.includes("/charges/") && route.request().method() === "PUT") {
      const input = route.request().postDataJSON(); mutations.push(input);
      draft.version++;
      draft.charges = [{ ...charge, appliedChargeId: input.appliedChargeId, invoiceBase: 80000, amount: 6500, invoicedAmount: 0, expenseAmount: 6500, invoicedUntaxedAmount: 0, invoicedTaxAmount: 0, supplier, manualAmount: 6500 }]; body = draft;
    } else if (path.includes("/charges/") && path.endsWith("/remove")) { expect(route.request().postDataJSON().expectedVersion).toBe(2); draft.version++; draft.charges = []; body = draft; }
    else if (path.endsWith("/settlement")) body = { grossAmount: draft.payableAmount, withholdingTotal: 0, netAmount: draft.payableAmount };
    else if (path.endsWith("/settlement-configuration")) body = { isAccountingEnabled: false, bankAccounts: [] };
    else if (path.includes("/orders")) body = { items: [], totalCount: 0, totalPages: 0, page: 1, pageSize: 25 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/pos");
  await expect(page.locator("#pos-scanner")).toBeEnabled();
  await expect(page.getByRole("button", { name: "Cargos de facturación Shift + F8", exact: true })).toBeEnabled();
  const readsBefore = draftReads;
  await page.keyboard.press("Shift+F8");
  const modal = page.getByRole("dialog", { name: "Cargos de facturación" });
  await expect(modal).toBeVisible();
  await modal.getByRole("button", { name: /Domicilio · Agotados/ }).click();
  await expect(modal.getByRole("combobox", { name: "Proveedor" })).toContainText(supplier.name);
  await expect(modal.getByLabel("Valor del cargo (COP)")).toHaveValue("5000");
  await modal.getByLabel("Valor del cargo (COP)").fill("6500");
  await modal.getByRole("button", { name: "Aplicar cargo" }).click();
  await expect(modal.getByRole("region", { name: "Cargos agregados" })).toContainText("Gasto · no incrementa el cobro");
  expect(mutations).toHaveLength(1);
  expect(mutations[0]).toMatchObject({ chargeId: charge.chargeId, supplierId: supplier.supplierId, manualAmount: 6500, expectedVersion: 1 });
  await expect(modal.locator("footer")).toContainText("80.000");
  await modal.getByRole("button", { name: "Quitar Domicilio · Agotados" }).click();
  await expect(modal.getByRole("region", { name: "Cargos agregados" })).toHaveCount(0);
  expect(catalogReads).toBe(1); expect(draftReads).toBe(readsBefore);
  await page.keyboard.press("Escape");
  await expect(modal).not.toBeVisible();
  await expect(page.locator("#pos-scanner")).toBeFocused();
});
