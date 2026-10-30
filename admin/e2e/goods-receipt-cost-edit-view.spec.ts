import { expect, test } from "@playwright/test";

test("la recepción confirmada abre edición de costos sin habilitar cantidades", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const documentId = "33333333-3333-3333-3333-333333333333";
  const supplierId = "44444444-4444-4444-4444-444444444444";
  const warehouseId = "55555555-5555-5555-5555-555555555555";
  const permissions = ["dashboard.read", "purchasing.goods-receipts.read",
    "purchasing.goods-receipts.create", "purchasing.goods-receipts.confirm", "accounting.read"];
  const user = { userId: "66666666-6666-6666-6666-666666666666", tenantId,
    tenantKey: "TEST", username: "test", firstName: "Prueba", lastName: "Compras",
    roles: ["ACCOUNTANT"], permissions };
  const receivedAt = "2026-10-20T12:00:00Z";
  const detail = { documentId, documentNumber: "EMC-EDITAR", status: "Processed",
    businessId, supplierId, supplierName: "Proveedor de prueba", warehouseId,
    warehouseName: "Principal", partySiteId: null, supplierInvoiceNumber: null,
    supplierInvoiceDate: receivedAt, receivedAt, updatedAt: receivedAt,
    acceptedAt: receivedAt, processedAt: receivedAt, dueDate: receivedAt,
    purchaseEvidenceType: "InternalReceiptVoucher", createsPayable: true,
    currencyCode: "COP", exchangeRate: 1, exchangeRateSource: "FunctionalCurrency",
    notes: null, netAmount: 50000, taxAmount: 0, grandTotal: 50000,
    functionalNetAmount: 50000, functionalTaxAmount: 0, functionalGrandTotal: 50000,
    lines: [{ lineNumber: 1, productId: "77777777-7777-7777-7777-777777777777",
      description: "Producto de prueba", quantity: 5, unitCost: 10000,
      presentationName: "Unidad", presentationQuantity: 5, unitsPerPresentation: 1,
      discountAmount: 0, taxCode: "IVA-0", taxRate: 0, taxTreatment: "NotApplicable",
      netAmount: 50000, taxAmount: 0, lineTotal: 50000,
      functionalNetAmount: 50000, functionalTaxAmount: 0, functionalLineTotal: 50000,
      allocatedLandedCostAmount: 0, recognizedInventoryCostAmount: 50000 }],
    additionalCostDocuments: [], referencedCostDocuments: [], accountingStatuses: [],
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
    if (path.includes("/goods-receipts/drafts/") && route.request().method() === "PUT") {
      const submitted = route.request().postDataJSON() as {
        draftId: string; costEditOfReceiptId: string; baseCostRevisionNumber: number;
        lines: { quantity: number }[];
      };
      expect(submitted.costEditOfReceiptId).toBe(documentId);
      expect(submitted.baseCostRevisionNumber).toBe(0);
      expect(submitted.lines[0].quantity).toBe(5);
      await route.fulfill({ status: 200, contentType: "application/json",
        body: JSON.stringify({ draftId: submitted.draftId, concurrencyToken: "AQIDBA==" }) });
      return;
    }
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Prueba" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Prueba" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: user.roles, permissions };
    else if (path.endsWith(`/goods-receipts/${documentId}/cost-edit`))
      body = { goodsReceiptId: documentId, baseCostRevisionNumber: 0, draft: null };
    else if (path.endsWith(`/goods-receipts/${documentId}`)) body = detail;
    else if (path.endsWith("/goods-receipts/withholding-preview"))
      body = { lines: [], adjustments: [], withholdingTotal: 0, netAmount: 50000 };
    else if (path.endsWith("/goods-receipts"))
      body = { items: [detail], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 };
    else if (path.endsWith("/goods-receipts/options")) body = {
      warehouses: [{ warehouseId, name: "Principal" }], suppliers: [],
      purchaseEvidenceTypes: [], withholdingConcepts: [], withholdingJurisdictions: [],
      purchaseCostEvidenceTypes: [], purchaseCostKinds: [], purchaseCostTreatments: [],
      purchaseCostAllocationMethods: [], purchaseTaxRates: [], purchaseTaxTreatments: [],
      purchaseCurrencies: [], exchangeRateSources: [], purchaseCostAssociationModes: [],
    };
    else if (path.endsWith("/purchase-orders"))
      body = { items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByText("EMC-EDITAR", { exact: true }).click();
  const detailDialog = page.getByRole("dialog", { name: "EMC-EDITAR" });
  await detailDialog.getByRole("button", { name: "Editar costos" }).click();
  const editor = page.getByRole("dialog", { name: "Editar costos de la recepción" });
  await expect(editor).toBeVisible();
  await expect(editor.getByRole("columnheader", { name: "Cantidad recibida" })).toBeVisible();
  await expect(editor.getByRole("row").filter({ hasText: "Producto de prueba" })
    .locator('input[type="number"]').first()).toHaveAttribute("readonly", "");
  await editor.getByRole("button", { name: /Facturas y otros costos/ }).click();
  await expect(editor.getByRole("button", { name: "Agregar factura" })).toBeVisible();
  await expect(editor.getByRole("button", { name: "Agregar comprobante" })).toBeVisible();
  await expect(editor.getByRole("button", { name: "Agregar producto" })).toHaveCount(0);
  await editor.getByRole("button", { name: "Guardar borrador" }).click();
  await expect(editor).toBeHidden();
});
