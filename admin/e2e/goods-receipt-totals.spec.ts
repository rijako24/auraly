import { expect, test } from "@playwright/test";

test("recepción totaliza documentos asociados y conserva costo total separado en pantalla y reporte", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const documentId = "33333333-3333-3333-3333-333333333333";
  const permissions = ["dashboard.read", "purchasing.goods-receipts.read", "purchasing.goods-receipts.create"];
  const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId,
    tenantKey: "TEST", username: "test", firstName: "Prueba", lastName: "Compras",
    roles: ["ACCOUNTANT"], permissions };
  const detail = {
    documentId, documentNumber: "EMC-RESUMEN", businessId,
    status: "Processed", supplierName: "Proveedor de prueba", warehouseName: "Principal",
    receivedAt: "2026-09-08T12:00:00Z", updatedAt: "2026-09-08T12:01:00Z", supplierInvoiceDate: "2026-09-08T12:00:00Z",
    purchaseEvidenceType: "SupplierElectronicInvoice", createsPayable: true,
    currencyCode: "COP", netAmount: 4000, taxAmount: 760, grandTotal: 4760,
    functionalNetAmount: 4000, functionalTaxAmount: 760, functionalGrandTotal: 4760,
    lines: [3000, 1000].map((amount, index) => ({ lineNumber: index + 1,
      description: `Producto ${index + 1}`, quantity: 1, unitCost: amount,
      presentationName: "Unidad", presentationQuantity: 1, unitsPerPresentation: 1,
      discountAmount: 0, taxRate: 19, taxTreatment: "DeductibleInputVat",
      netAmount: amount, taxAmount: amount * 0.19, lineTotal: amount * 1.19,
      allocatedLandedCostAmount: amount / 2, recognizedInventoryCostAmount: amount * 1.5 })),
    additionalCostDocuments: [
      { costDocumentId: "freight", documentNumber: "FLETE-1", currencyCode: "COP",
        purchaseEvidenceType: "SupplierElectronicInvoice", functionalGrandTotal: 1190,
        functionalTaxAmount: 190, lines: [{ lineNumber: 1, description: "Flete", functionalAmount: 1000 }] },
      { costDocumentId: "customs", documentNumber: "ADUANA-1", currencyCode: "COP",
        purchaseEvidenceType: "ImportDeclaration", functionalGrandTotal: 2000,
        functionalTaxAmount: 0, lines: [{ lineNumber: 1, description: "Arancel y otro gasto", functionalAmount: 2000 }] },
    ], accountingStatuses: [],
  };
  await page.context().addCookies([{ name: "auth_token", value: "e2e",
    url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { tenantId, businessId, user });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Prueba" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: user.roles, permissions };
    else if (path.endsWith(`/goods-receipts/${documentId}`)) body = detail;
    else if (path.endsWith("/goods-receipts")) body = { items: [detail], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };
    else if (path.endsWith("/goods-receipts/options")) body = {
      warehouses: [], suppliers: [], purchaseEvidenceTypes: [], withholdingConcepts: [], withholdingJurisdictions: [],
      purchaseCostEvidenceTypes: [], purchaseCostKinds: [], purchaseCostTreatments: [], purchaseCostAllocationMethods: [],
      purchaseTaxRates: [], purchaseTaxTreatments: [], purchaseCurrencies: [], exchangeRateSources: [],
    };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const editor = page.getByRole("dialog", { name: "Recepción de compra" });
  await expect(editor.getByRole("columnheader", { name: "Costo unitario" })).toBeVisible();
  await expect(editor.getByRole("columnheader", { name: /Costo total/ })).toHaveCount(0);
  await expect(editor.getByRole("columnheader", { name: "Total factura · COP" })).toBeVisible();
  await editor.getByRole("button", { name: /Facturas y otros costos/ }).click();
  await expect(editor.getByRole("button", { name: "Agregar factura", exact: true })).toHaveCount(1);
  await expect(editor.getByRole("button", { name: /Agregar nacionalización/i })).toHaveCount(0);
  await page.keyboard.press("Escape");
  await page.getByText("EMC-RESUMEN", { exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "EMC-RESUMEN" });
  await expect(dialog.getByText("Total bruto de documentos", { exact: true }).locator("..")).toContainText("7.950");
  await expect(dialog.getByText("IVA descontable separado", { exact: true }).locator("..")).toContainText("0");
  await expect(dialog.getByText("Valor que entra al inventario", { exact: true }).locator("..")).toContainText("6.000");
  await expect(dialog.getByRole("columnheader", { name: "Total factura · COP" })).toBeVisible();
  await expect(dialog.getByRole("columnheader", { name: "Costo unitario" })).toBeVisible();
  await expect(dialog.getByRole("columnheader", { name: "Descuento" })).toBeVisible();
  await expect(dialog.getByText("Incluye +$ 1.500 COP prorrateado", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Costo total $ 4.500 COP", { exact: true })).toBeVisible();
  const firstProduct = dialog.getByRole("row").filter({ hasText: "Producto 1" });
  await expect(firstProduct).toContainText("$ 0");
  await expect(firstProduct).toContainText("$ 3.570");
  await page.screenshot({ path: "test-results/goods-receipt-line-costs.png", fullPage: true });
  await dialog.getByRole("button", { name: "Abrir reporte" }).click();
  await expect(page.getByRole("row").filter({ hasText: "Total bruto de documentos" })).toContainText("7.950");
  await expect(page.getByRole("row").filter({ hasText: "Valor que entra al inventario" })).toContainText("6.000");
  await expect(page.getByText(/NaN/)).toHaveCount(0);
  await page.screenshot({ path: "test-results/goods-receipt-totals.png", fullPage: true });
});
