import { expect, test, type Page, type Route } from "@playwright/test";
import type { GoodsReceiptDetail, GoodsReceiptOptions } from "../src/services/api/goods-receipts";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const supplierId = "44444444-4444-4444-4444-444444444444";
const supplierSiteId = "66666666-6666-6666-6666-666666666666";
const permissions = ["dashboard.read", "purchasing.goods-receipts.read", "purchasing.goods-receipts.create", "purchasing.goods-receipts.confirm", "accounting.read"];

const json = (route: Route, body: unknown) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
const receiptOptions: GoodsReceiptOptions = {
  warehouses: [{ warehouseId: "55555555-5555-5555-5555-555555555555", code: "PPL", name: "Principal" }],
  suppliers: [], purchaseEvidenceTypes: [{ code: "InternalReceiptVoucher", label: "Comprobante interno", description: null }],
  withholdingConcepts: [], withholdingJurisdictions: [], purchaseCostEvidenceTypes: [], purchaseCostKinds: [],
  purchaseCostTreatments: [], purchaseCostAllocationMethods: [], purchaseTaxRates: [], purchaseTaxTreatments: [],
  purchaseCurrencies: [{ code: "COP", label: "Peso colombiano", description: null }],
  exchangeRateSources: [{ code: "FunctionalCurrency", label: "Moneda funcional", description: null }],
};
const supplierOption = { partyId: supplierId, roleId: supplierId, partySiteId: supplierSiteId, siteName: "Principal",
  displayName: "Proveedor Andino", identification: "900100200", isPrimary: true, supplierPurchaseEvidencePolicy: "InternalReceiptVoucher",
  supplierDefaultPaymentDueDays: 30 };

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: {
      userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test",
      firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted,
    } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
}

test.beforeEach(async ({ page }) => {
  await page.route("**/api/**", route => json(route, []));
  await page.route("**/api/auth/me", route => json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/goods-receipts?**", route => json(route, { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/commerce/v1/goods-receipts/options", route => json(route, receiptOptions));
  await page.route("**/api/commerce/v1/purchase-orders?**", route => json(route, { items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/tenant-commercial/subscription", route => json(route, { dianDocumentMonthlyLimit: 100, dianDocumentsUsed: 0 }));
  await page.route("**/api/commerce/v1/portfolio/parties/site-options?**", route => json(route, {
    items: [supplierOption],
    page: 1, pageSize: 10, totalCount: 1, totalPages: 1,
  }));

  await authenticate(page);
});

test("una recepción confirmada muestra el costo y no permite agregar facturas ni comprobantes", async ({ page }) => {
  const documentId = "88888888-8888-8888-8888-888888888888";
  const detail: GoodsReceiptDetail = {
    documentId, documentNumber: "EMC00-00000088", status: "Processed",
    warehouseId: receiptOptions.warehouses[0].warehouseId, warehouseName: "Principal",
    supplierId, supplierName: "Proveedor Andino", supplierInvoiceNumber: null,
    supplierInvoiceDate: "2026-10-28T10:00:00-05:00", receivedAt: "2026-10-28T10:00:00-05:00",
    createsPayable: true, dueDate: "2026-11-28T10:00:00-05:00", currencyCode: "COP",
    notes: null, netAmount: 10_000, taxAmount: 0, grandTotal: 10_000,
    acceptedAt: "2026-10-28T10:00:00-05:00", processedAt: "2026-10-28T10:01:00-05:00",
    lines: [{ lineNumber: 1, productId: "77777777-7777-7777-7777-777777777777",
      description: "Producto recibido", quantity: 1, unitCost: 10_000,
      discountAmount: 0, taxCode: "00", taxRate: 0, taxTreatment: "NotApplicable",
      presentationName: "Unidad", presentationQuantity: 1, unitsPerPresentation: 1,
      netAmount: 10_000, taxAmount: 0, lineTotal: 10_000, recognizedInventoryCostAmount: 10_000 }],
    purchaseEvidenceType: "InternalReceiptVoucher", purchaseOrderId: null, withholding: null,
    exchangeRate: 1, exchangeRateDate: null, exchangeRateSource: "FunctionalCurrency",
    functionalNetAmount: 10_000, functionalTaxAmount: 0, functionalGrandTotal: 10_000,
    additionalCostDocuments: null, referencedCostDocuments: null, accountingStatuses: null,
  };
  await page.route(`**/api/commerce/v1/goods-receipts/${documentId}`, route => json(route, detail));
  await page.goto(`/dashboard/purchasing/goods-receipts?receiptId=${documentId}`);
  const receipt = page.getByRole("dialog", { name: detail.documentNumber, exact: true });
  await expect(receipt).toContainText("Producto recibido");
  await expect(receipt.getByRole("button", { name: "Agregar comprobante", exact: true })).toHaveCount(0);
  await expect(receipt.getByRole("button", { name: "Agregar factura", exact: true })).toHaveCount(0);
});

test("emisión y vencimiento se pueden seleccionar y conservan el vencimiento propuesto", async ({ page }) => {
  let savedRequest: Record<string, unknown> | undefined;
  await page.route("**/api/commerce/v1/goods-receipts/drafts/*", route => {
    savedRequest = route.request().postDataJSON();
    return json(route, { ...savedRequest, updatedAt: new Date().toISOString(), concurrencyToken: "AQ==" });
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await dialog.getByRole("combobox", { name: "Seleccionar proveedor y sede" }).click();
  await page.getByRole("option", { name: /Proveedor Andino/ }).click();

  const issue = dialog.getByText("Fecha de emisión", { exact: true }).locator("..").getByRole("button").first();
  const initialIssue = await issue.textContent();
  await issue.click();
  await page.locator('[data-radix-popper-content-wrapper]:visible button[aria-label*=" de "]:not(:disabled)').first().click();
  await expect(issue).not.toHaveText(initialIssue ?? "");

  const due = dialog.getByText("Fecha de vencimiento", { exact: true }).locator("..").getByRole("button").first();
  const proposedDue = await due.textContent();
  await expect(due).toBeEnabled();
  await due.click();
  await page.locator('[data-radix-popper-content-wrapper]:visible button[aria-label*=" de "]:not(:disabled)').first().click();
  await expect(due).not.toHaveText(proposedDue ?? "");
  await expect(dialog.getByText("La compra genera su cuenta por pagar.", { exact: false })).toBeVisible();
  await expect(dialog.getByText("Contado", { exact: true })).toHaveCount(0);
  await dialog.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  expect(savedRequest?.createsPayable).toBe(true);
  expect(savedRequest?.dueDate).toBeTruthy();
});

for (const withProducts of [true, false]) {
test(`recuperar un cargo antiguo de contado conserva sus valores (${withProducts ? "con productos" : "captura incompleta"})`, async ({ page }) => {
  const documentId = "88888888-8888-8888-8888-888888888888";
  const costDocumentId = "99999999-9999-9999-9999-999999999999";
  let saved: { additionalCostDocuments: Array<{ createsPayable: boolean; dueDate: string; lines: Array<{ amount: number }> }> } | undefined;
  await page.addInitScript(({ businessId, supplierId, supplierSiteId, documentId, costDocumentId, withProducts }) => {
    localStorage.setItem(`auraly.goods-receipt.entry.${businessId}`, JSON.stringify({
      draftId: documentId, warehouseId: "55555555-5555-5555-5555-555555555555", supplierId, partySiteId: supplierSiteId,
      supplierInvoiceNumber: "COMPRA-ANTERIOR", supplierInvoiceDate: "2026-10-01", purchaseEvidenceType: "InternalReceiptVoucher",
      receivedAt: "2026-10-02T10:00", dueDate: "2026-10-01", notes: "Captura anterior", concurrencyToken: null,
      lines: withProducts ? [{ lineNumber: 1, productId: "77777777-7777-7777-7777-777777777777",
        description: "Producto de la captura", quantity: 1, unitCost: 1000, discountAmount: 0,
        taxCode: "00", taxRate: 0, taxTreatment: "NotApplicable", presentationName: "Unidad",
        presentationQuantity: 1, unitsPerPresentation: 1 }] : [],
      currencyCode: "COP", exchangeRate: 1, exchangeRateDate: "2026-10-01", exchangeRateSource: "FunctionalCurrency",
      additionalCostDocuments: [{ costDocumentId, supplierId, partySiteId: supplierSiteId, purchaseEvidenceType: "InternalReceiptVoucher",
        documentNumber: "FLETE-ANTERIOR", issuedAt: "2026-10-01", createsPayable: false, dueDate: null,
        currencyCode: "COP", exchangeRate: 1, exchangeRateDate: "2026-10-01", exchangeRateSource: "FunctionalCurrency",
        lines: [{ lineNumber: 1, costKind: "Freight", description: "Flete anterior", amount: 5000,
          taxableBaseAmount: 0, taxCode: "00", taxRate: 0, taxAmount: 0, taxTreatment: "NotApplicable",
          costTreatment: "Capitalize", allocationMethod: "Value" }] }],
    }));
  }, { businessId, supplierId, supplierSiteId, documentId, costDocumentId, withProducts });
  await page.route("**/api/commerce/v1/goods-receipts/withholding-preview", route =>
    json(route, { grossAmount: 1000, withholdingTotal: 0, netAmount: 1000, lines: [] }));
  await page.route("**/api/commerce/v1/goods-receipts/cost-withholding-preview", route =>
    json(route, { grossAmount: 5000, withholdingTotal: 0, netAmount: 5000, lines: [] }));
  await page.route("**/api/commerce/v1/goods-receipts/drafts/*", route => {
    saved = route.request().postDataJSON();
    return json(route, { ...saved, updatedAt: new Date().toISOString(), concurrencyToken: "AQ==" });
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await expect(dialog.locator("textarea")).toHaveValue("Captura anterior");
  if (!withProducts) {
    await expect(dialog.getByText("Los cargos se conservan en el borrador.", { exact: false })).toBeVisible();
    await dialog.getByRole("button", { name: "Confirmar entrada", exact: true }).click();
    await expect(page.getByText("Selecciona proveedor, sede, bodega, tipo de soporte y agrega al menos un producto.", { exact: true })).toBeVisible();
    await expect(dialog.locator("textarea")).toHaveValue("Captura anterior");
    expect(saved).toBeUndefined();
    return;
  }
  await dialog.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  expect(saved?.additionalCostDocuments[0].createsPayable).toBe(true);
  expect(saved?.additionalCostDocuments[0].dueDate.slice(0,10)).toBe("2026-10-01");
  expect(saved?.additionalCostDocuments[0].lines[0].amount).toBe(5000);
});
}

test("el descuento comercial porcentual recalcula al cambiar la cantidad y se guarda sin duplicar el importe", async ({ page }) => {
  let savedRequest: { lines: Array<{ commercialDiscountPercent: number; discountAmount: number }> } | undefined;
  await page.route("**/api/commerce/v1/goods-receipts/withholding-preview", route => {
    const line = route.request().postDataJSON().lines[0] as { quantity: number; commercialDiscountPercent?: number };
    const amount = line.quantity === 2 ? 1800 : line.commercialDiscountPercent === 10 ? 900 : 1000;
    return json(route, { grossAmount: amount, withholdingTotal: 0, netAmount: amount, lines: [] });
  });
  await page.route("**/api/commerce/v1/goods-receipts/products?**", route => json(route, {
    items: [{ productId: "77777777-7777-7777-7777-777777777777", productCode: "PR-1",
      name: "Producto con descuento", barcodes: [], isAssociated: true, latestUnitCost: 1000,
      averageUnitCost: 1000, taxCode: "00", taxRate: 0, taxTreatment: "NotApplicable",
      baseUnitCode: "NIU", purchasePresentationName: "Unidad", unitsPerPresentation: 1 }],
    page: 1, pageSize: 10, totalCount: 1, totalPages: 1,
  }));
  await page.route("**/api/commerce/v1/goods-receipts/drafts/*", route => {
    savedRequest = route.request().postDataJSON();
    return json(route, { ...savedRequest, concurrencyToken: "AQ==", updatedAt: new Date().toISOString() });
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await dialog.getByRole("combobox", { name: "Seleccionar proveedor y sede" }).click();
  await page.getByRole("option", { name: /Proveedor Andino/ }).click();
  await dialog.getByPlaceholder("Escanea o busca por código, referencia, nombre o código del proveedor").fill("Producto");
  const addProduct = dialog.getByRole("button", { name: "Agregar", exact: true });
  await expect(addProduct).toBeEnabled();
  await addProduct.click();
  const percentage = dialog.getByRole("textbox", { name: "Descuento comercial (%) de Producto con descuento", exact: true });
  await percentage.fill("10");
  await percentage.press("Tab");
  const row = dialog.getByRole("row").filter({ hasText: "Producto con descuento" });
  await expect(row).toContainText("900");
  await row.getByRole("spinbutton", { name: "Cantidad en Unidad", exact: true }).fill("2");
  await percentage.click();
  await expect(row).toContainText("1.800");
  await dialog.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  expect(savedRequest?.lines[0].commercialDiscountPercent).toBe(10);
  expect(savedRequest?.lines[0].discountAmount).toBe(0);
});

test("un conflicto al guardar conserva la captura y no sobrescribe otra versión", async ({ page }) => {
  const draftRequests: string[] = [];
  await page.route("**/api/commerce/v1/goods-receipts/drafts/*", route => {
    draftRequests.push(route.request().method());
    return route.fulfill({ status: 409, contentType: "application/problem+json",
      body: JSON.stringify({ status: 409, detail: "El borrador cambió en otra sesión." }) });
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await dialog.locator("textarea").fill("Conservar mi captura sin sobrescribir otra sesión");
  await dialog.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  await expect(page.getByText("El borrador cambió en otra sesión. Ábrelo desde la lista para revisar la versión actual antes de guardar.")).toBeVisible();
  await expect(dialog.locator("textarea")).toHaveValue("Conservar mi captura sin sobrescribir otra sesión");
  expect(draftRequests).toEqual(["PUT"]);
});


test("sin cupo DIAN conserva la recepción y permite volver al comprobante", async ({ page }) => {
  let quotaReads = 0;
  await page.route("**/api/tenant-commercial/subscription", route => {
    quotaReads += 1;
    return json(route, { dianDocumentMonthlyLimit: 100, dianDocumentsUsed: 0, hasDianDocumentQuota: false });
  });
  await page.route("**/api/commerce/v1/goods-receipts/options", route => json(route, {
    ...receiptOptions, purchaseEvidenceTypes: [
      ...receiptOptions.purchaseEvidenceTypes,
      { code: "BuyerElectronicSupportDocument", label: "Documento soporte electrónico", description: null },
    ],
  }));
  await page.route("**/api/commerce/v1/portfolio/parties/site-options?**", route => json(route, {
    items: [{ ...supplierOption, supplierPurchaseEvidencePolicy: null }],
    page: 1, pageSize: 10, totalCount: 1, totalPages: 1,
  }));
  await page.goto("/dashboard/purchasing/goods-receipts");
  await expect(page.getByRole("button", { name: "Nueva entrada" })).toBeVisible();
  expect(quotaReads).toBe(0);
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const editor = page.getByRole("dialog", { name: /Recepción de compra/ });
  await editor.getByRole("combobox", { name: "Seleccionar proveedor y sede" }).click();
  await page.getByRole("option", { name: /Proveedor Andino/ }).click();
  await editor.locator("textarea").fill("Recepción que debe conservarse");
  const evidence = editor.getByRole("combobox", { name: "Tipo de soporte", exact: true });
  await evidence.click();
  await page.getByRole("option", { name: "Documento soporte electrónico", exact: true }).click();
  const notice = page.getByRole("dialog", { name: "No tienes documentos DIAN disponibles" });
  await expect(notice).toBeVisible();
  await notice.getByRole("button", { name: "Volver al comprobante" }).click();
  await expect(editor.locator("textarea")).toHaveValue("Recepción que debe conservarse");
  await evidence.click();
  await page.getByRole("option", { name: "Comprobante interno", exact: true }).click();
  await expect(evidence).toHaveText("Comprobante interno");
  expect(quotaReads).toBe(1);
});


for (const distribute of [false, true]) {
  test(`comprobante existente conserva el cargo y guarda ${distribute ? "un reparto parcial" : "solo la asociación"}`, async ({ page }) => {
    const entryId = "91000000-0000-0000-0000-000000000001";
    const sourceId = "91000000-0000-0000-0000-000000000002";
    let voucherReads = 0;
    let candidateReads = 0;
    let budgetUnavailable = false;
    let expectedVoucherReads = 1;
    let saved: { referencedCostDocuments: Array<{ entryId: string; mode: string; lines: Array<{ functionalAmount: number }> }>; additionalCostDocuments: unknown[] } | undefined;
    await page.route("**/api/commerce/v1/goods-receipts/options", route => json(route, {
      ...receiptOptions,
      purchaseCostAssociationModes: [
        { code: "Associate", label: "Solo asociar", description: "Sin modificar el costo" },
        { code: "Distribute", label: "Distribuir entre los productos", description: "Repartir el valor elegido" },
      ],
      purchaseCostAllocationMethods: [{ code: "Value", label: "Por valor", description: null }, { code: "Weight", label: "Por peso", description: null }],
    }));
    await page.route("**/api/commerce/v1/parties/role-options?**", route => json(route, {
      items: [{ ...supplierOption, role: "Supplier" }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1,
    }));
    await page.route("**/api/commerce/v1/goods-receipts/cost-vouchers?**", route => {
      candidateReads += 1;
      expect(new URL(route.request().url()).searchParams.get("partyId")).toBe(supplierId);
      expect(new URL(route.request().url()).searchParams.get("pageSize")).toBe("10");
      return json(route, { items: [{ entryId, entryNumber: "CC-FLETE", documentNumber: "FLETE-5000000",
        occurredAt: "2026-09-30T12:00:00-05:00", partyName: "Proveedor Andino", description: "Flete ya reconocido",
        accountCode: "5195", accountName: "Fletes", concept: "Transporte", eligibleLineCount: 1,
        eligibleFunctionalAmount: 5000000, assignedFunctionalAmount: 0 }],
        page: 1, pageSize: 10, totalCount: 1, totalPages: 1 });
    });
    await page.route(`**/api/commerce/v1/goods-receipts/cost-vouchers/${entryId}`, route => {
      voucherReads += 1;
      if (budgetUnavailable) return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ title: "Consulta no disponible" }) });
      return json(route, { entryId, entryNumber: "CC-FLETE", sourceDocumentId: sourceId, sourceDocumentType: "Expense", lines: [
        { lineNumber: 1, accountId: "cost", accountCode: "5195", accountName: "Fletes", partyId: supplierId,
          partyName: "Proveedor Andino", description: "Transporte", debit: 5000000, credit: 0, eligibleFunctionalAmount: 5000000, assignedFunctionalAmount: 0 },
        { lineNumber: 2, accountId: "vat", accountCode: "2408", accountName: "IVA descontable", partyId: supplierId,
          partyName: "Proveedor Andino", description: "IVA", debit: 950000, credit: 0, eligibleFunctionalAmount: 0, assignedFunctionalAmount: 0 },
        { lineNumber: 3, accountId: "payable", accountCode: "2205", accountName: "Proveedores", partyId: supplierId,
          partyName: "Proveedor Andino", description: "Obligación", debit: 0, credit: 5950000, eligibleFunctionalAmount: 0, assignedFunctionalAmount: 0 },
      ] });
    });
    await page.route("**/api/commerce/v1/goods-receipts/withholding-preview", route => json(route, { grossAmount: 1000, withholdingTotal: 0, netAmount: 1000, lines: [] }));
    await page.route("**/api/commerce/v1/goods-receipts/drafts/*", route => {
      saved = route.request().postDataJSON();
      return json(route, { ...saved, updatedAt: new Date().toISOString(), concurrencyToken: "AQ==" });
    });
    await page.addInitScript(({ businessId, supplierId, supplierSiteId }) => {
      localStorage.setItem(`auraly.goods-receipt.entry.${businessId}`, JSON.stringify({
        draftId: "91000000-0000-0000-0000-000000000003", warehouseId: "55555555-5555-5555-5555-555555555555",
        supplierId, partySiteId: supplierSiteId, supplierInvoiceDate: "2026-10-01", purchaseEvidenceType: "InternalReceiptVoucher",
        receivedAt: "2026-10-01T10:00", dueDate: "2026-10-31", notes: "Mercancía con cargo existente", concurrencyToken: null,
        currencyCode: "COP", exchangeRate: 1, exchangeRateDate: "2026-10-01", exchangeRateSource: "FunctionalCurrency", additionalCostDocuments: [],
        lines: [{ lineNumber: 1, productId: "77777777-7777-7777-7777-777777777777", description: "Producto de prueba", quantity: 1,
          unitCost: 1000, discountAmount: 0, taxCode: "00", taxRate: 0, taxTreatment: "NotApplicable", presentationName: "Unidad", presentationQuantity: 1, unitsPerPresentation: 1 }],
      }));
    }, { businessId, supplierId, supplierSiteId });
    await page.goto("/dashboard/purchasing/goods-receipts");
    expect(voucherReads).toBe(0);
    await page.getByRole("button", { name: "Nueva entrada" }).click();
    const editor = page.getByRole("dialog", { name: /Recepción de compra/ });
    await expect(editor.locator("textarea")).toHaveValue("Mercancía con cargo existente");
    await editor.getByRole("button", { name: /Facturas y otros costos/ }).click();
    await expect(editor.getByRole("button", { name: "Agregar factura", exact: true })).toBeVisible();
    await editor.getByRole("button", { name: "Agregar comprobante", exact: true }).click();
    const modal = page.getByRole("dialog", { name: "Agregar comprobante a la compra" });
    await modal.getByRole("combobox", { name: "Tercero de los comprobantes de compra" }).click();
    await page.getByRole("option", { name: /Proveedor Andino/ }).click();
    await expect(modal.getByRole("combobox", { name: "Comprobante para asociar a la compra" })).toHaveCount(0);
    await expect(modal.getByText("Flete ya reconocido")).toBeVisible();
    await modal.getByRole("button", { name: "Seleccionar documento FLETE-5000000" }).click();
    await expect(modal.getByText("5195 · Fletes", { exact: true }).last()).toBeVisible();
    await expect(modal.getByText("2408 · IVA descontable")).toHaveCount(0);
    await expect(modal.getByText("2205 · Proveedores")).toHaveCount(0);
    if (distribute) {
      await modal.getByRole("combobox", { name: "Cómo aplicar el comprobante" }).click();
      await page.getByRole("option", { name: "Distribuir entre los productos" }).click();
      await modal.getByRole("textbox", { name: "Importe a repartir de 5195" }).fill("3000000");
      await modal.getByRole("combobox", { name: "Método de reparto de 5195" }).click();
      await page.getByRole("option", { name: "Por peso" }).click();
      await expect(modal.getByRole("button", { name: "Agregar comprobante", exact: true })).toBeDisabled();
      await expect(modal.getByRole("alert")).toContainText("cada producto necesita un peso positivo");
      await modal.getByLabel("Peso total de Producto de prueba").fill("2");
      await expect(modal.getByRole("button", { name: "Agregar comprobante", exact: true })).toBeEnabled();
    }
    await modal.getByRole("button", { name: "Agregar comprobante", exact: true }).click();
    await expect(modal).toBeHidden();
    await expect(editor.getByText("CC-FLETE", { exact: true })).toBeVisible();
    if (distribute) {
      await expect(editor.locator("p").filter({ hasText: /^Costo total.*3\.001\.000 COP$/ })).toBeVisible();
      const financialDiscount = editor.getByRole("button", { name: "Descuento financiero acordado con el proveedor" });
      await expect(financialDiscount).toHaveAttribute("aria-expanded", "false");
      await financialDiscount.click();
      await expect(financialDiscount).toHaveAttribute("aria-expanded", "true");
      await expect(editor.getByLabel("Descuento financiero (%)", { exact: true })).toHaveValue("");
      await editor.getByLabel("Descuento financiero (%)", { exact: true }).fill("10");
      await editor.getByText("Aplicarlo al costo al confirmar la compra", { exact: true }).click();
      await expect(editor.getByText(/Descuento financiero -.*100/)).toBeVisible();
      await expect(editor.locator("p").filter({ hasText: /^Costo total.*3\.000\.900 COP$/ })).toBeVisible();
    }
    if (distribute) {
      budgetUnavailable = true;
      await editor.getByRole("button", { name: "Revisar comprobante", exact: true }).click();
      await expect(modal.getByRole("alert")).toContainText("No se pudo consultar el comprobante");
      await expect(modal.getByRole("button", { name: "Actualizar comprobante", exact: true })).toBeDisabled();
      const failedReads = voucherReads;
      expect(failedReads).toBeGreaterThanOrEqual(2);
      expect(failedReads).toBeLessThanOrEqual(3);
      budgetUnavailable = false;
      await modal.getByRole("button", { name: "Reintentar", exact: true }).click();
      await expect(modal.getByRole("button", { name: "Actualizar comprobante", exact: true })).toBeEnabled();
      expectedVoucherReads = failedReads + 1;
      expect(voucherReads).toBe(expectedVoucherReads);
      await modal.getByRole("button", { name: "Actualizar comprobante", exact: true }).click();
      await expect(modal).toBeHidden();
    }
    await editor.getByRole("button", { name: "Guardar borrador", exact: true }).click();
    await expect(editor).toBeHidden();
    expect(saved!.additionalCostDocuments).toHaveLength(0);
    expect(saved!.referencedCostDocuments).toHaveLength(1);
    expect(saved!.referencedCostDocuments[0].entryId).toBe(entryId);
    expect(saved!.referencedCostDocuments[0].mode).toBe(distribute ? "Distribute" : "Associate");
    expect(saved!.referencedCostDocuments[0].lines).toEqual(distribute ? [expect.objectContaining({ functionalAmount: 3000000, allocationMethod: "Weight" })] : []);
    expect(voucherReads).toBe(expectedVoucherReads);
    expect(candidateReads).toBe(1);
  });
}
