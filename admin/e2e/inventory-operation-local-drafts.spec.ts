import { expect, test, type Locator, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const primaryWarehouseId = "44444444-4444-4444-4444-444444444444";
const auxiliaryWarehouseId = "55555555-5555-5555-5555-555555555555";
const productId = "66666666-6666-6666-6666-666666666666";
const permissions = [
  "dashboard.read", "inventory.read", "inventory.physical-counts.manage",
  "inventory.physical-counts.capture", "inventory.counts.confirm",
  "inventory.adjustments.confirm", "inventory.transfers.dispatch",
  "inventory.conversions.confirm", "inventory.damages.confirm",
  "purchasing.goods-receipts.read", "purchasing.goods-receipts.create",
  "purchasing.goods-receipts.confirm", "catalog.costs.manage",
];

test.use({ serviceWorkers: "block" });

const json = (route: Route, body: unknown) => route.fulfill({
  status: 200,
  contentType: "application/json",
  body: JSON.stringify(body),
});

async function authenticate(page: Page) {
  await page.context().addCookies([{
    name: "auth_token", value: "e2e", url: "http://127.0.0.1:3000",
    httpOnly: true, sameSite: "Lax",
  }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true,
      user: { userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted },
    }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
}

async function mockApi(page: Page) {
  await page.route("**/api/**", route => {
    const url = new URL(route.request().url());
    const path = url.pathname;
    if (path.endsWith("/auth/me")) return json(route, { userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions });
    if (path.endsWith("/execution-context/tenants")) return json(route, [{ tenantId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/businesses")) return json(route, [{ tenantId, businessId, name: "Auraly" }]);
    if (path.endsWith("/execution-context/access")) return json(route, { tenantId, businessId, roles: ["Administrator"], permissions });
    if (path.endsWith("/inventory/warehouses")) return json(route, [
      { warehouseId: primaryWarehouseId, code: "PPL", name: "Principal" },
      { warehouseId: auxiliaryWarehouseId, code: "AUX", name: "Auxiliar" },
    ]);
    if (path.endsWith("/inventory/reasons")) return json(route, [{
      inventoryReasonId: "77777777-7777-7777-7777-777777777777",
      code: "TEST_REASON", name: "Motivo de prueba", operationType: url.searchParams.get("operationType") ?? "StockCount", isActive: true,
    }]);
    if (path.endsWith("/inventory/products") || path.endsWith("/inventory/conversion-products")) return json(route, {
      items: [{ productId, productCode: "PRD-001", reference: "ARROZ", productName: "Arroz premium", unitCode: "EA", quantityOnHand: 25, averageUnitCost: 2_000, saleUnitPrice: 3_000, familyRootProductId: productId, conversionFactor: 1, maximumLossPercent: 10 }],
      page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
    });
    if (path.endsWith("/inventory/balances") || path.endsWith("/inventory/movements") || path.endsWith("/inventory/operations")) return json(route, {
      items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
    });
    if (path.endsWith("/goods-receipts/options")) return json(route, {
      warehouses: [{ warehouseId: primaryWarehouseId, code: "PPL", name: "Principal" }],
      suppliers: [],
      purchaseEvidenceTypes: [
        { code: "InternalReceiptVoucher", label: "Comprobante interno" },
        { code: "ForeignCommercialInvoice", label: "Factura comercial extranjera" },
      ],
      withholdingConcepts: [], withholdingJurisdictions: [],
      purchaseCostEvidenceTypes: [
        { code: "SupplierElectronicInvoice", label: "Factura electrónica" },
        { code: "ImportDeclaration", label: "Declaración de importación" },
      ],
      purchaseCostKinds: [
        { code: "Freight", label: "Flete" },
        { code: "CustomsDuty", label: "Arancel" },
        { code: "ImportVat", label: "IVA de importación" },
        { code: "OtherDirectCost", label: "Otro costo directo" },
      ],
      purchaseCostTreatments: [
        { code: "Capitalize", label: "Mayor valor del inventario" },
        { code: "Expense", label: "Gasto del período" },
      ],
      purchaseCostAllocationMethods: [
        { code: "Value", label: "Por valor" },
        { code: "Weight", label: "Por peso" },
        { code: "Manual", label: "Manual" },
        { code: "None", label: "No aplica" },
      ],
      purchaseTaxRates: [
        { code: "0", label: "0 %" },
        { code: "19", label: "19 %" },
      ],
      purchaseTaxTreatments: [
        { code: "DeductibleInputVat", label: "IVA descontable" },
        { code: "CapitalizedCost", label: "Mayor valor del costo" },
        { code: "NotApplicable", label: "No aplica" },
      ],
      purchaseCurrencies: [
        { code: "COP", label: "Peso colombiano" },
        { code: "USD", label: "Dólar estadounidense" },
      ],
      exchangeRateSources: [
        { code: "Negotiated", label: "Tasa pactada" },
        { code: "OfficialTRM", label: "TRM oficial" },
      ],
    });
    if (path.endsWith("/goods-receipts/products")) return json(route, {
      items: [{ productId, productCode: "PRD-001", reference: "ARROZ", name: "Arroz premium", supplierProductCode: "AR-1", latestUnitCost: 2_000, averageUnitCost: 1_900, taxCode: "01", taxRate: 0, taxTreatment: "CapitalizedCost", barcodes: [], baseUnitCode: "94", isAssociated: true, purchasePresentationName: "Caja", unitsPerPresentation: 6, isPrimary: true }],
      page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
    });
    if (path.endsWith("/goods-receipts/withholding-preview") || path.endsWith("/goods-receipts/cost-withholding-preview")) return json(route, {
      grossAmount: 14_000, withholdingTotal: 0, netAmount: 14_000, lines: [],
    });
    if (path.endsWith("/parties")) return json(route, {
      items: [{ partyId: "88888888-8888-8888-8888-888888888888", supplierId: "88888888-8888-8888-8888-888888888888", displayName: "Proveedor Andino", identification: "900100200", email: null, roles: ["Supplier"], isActive: true, supplierDefaultPaymentDueDays: 30 }],
      page: 1, pageSize: 10, totalCount: 1, totalPages: 1,
    });
    if (path.endsWith("/goods-receipts") || path.endsWith("/purchase-orders")) return json(route, {
      items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0,
    });
    return json(route, []);
  });
}

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function select(page: Page, combo: Locator, option: string) {
  await combo.click();
  await page.getByRole("option", { name: option, exact: true }).click();
}

async function addProduct(page: Page, dialog: Locator) {
  const search = dialog.getByTestId("product-picker-search");
  await search.fill("Arroz");
  await page.getByRole("option", { name: /Arroz premium/ }).click();
}

async function closeAndReopen(page: Page, dialog: Locator) {
  const close = dialog.getByRole("button", { name: "Cerrar", exact: true });
  if (await close.count()) await close.last().click();
  else await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
  await page.getByRole("button", { name: "Nueva operación" }).click();
  await expect(dialog).toBeVisible();
}

test("cada operación de inventario conserva combos, productos y captura local", async ({ page }) => {
  test.setTimeout(180_000);
  await mockApi(page);
  await authenticate(page);
  await page.goto("/dashboard/inventory");
  await page.getByRole("button", { name: "Nueva operación" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva operación" });

  await select(page, field(dialog, "Bodega").getByRole("combobox"), "Principal");
  await select(page, field(dialog, "Motivo").getByRole("combobox"), "Motivo de prueba");
  await addProduct(page, dialog);
  await dialog.getByRole("textbox", { name: "Contar Arroz premium", exact: true }).fill("8");
  await field(dialog, "Observaciones").getByRole("textbox").fill("conteo persistente");
  await closeAndReopen(page, dialog);
  await expect(field(dialog, "Bodega").getByRole("combobox")).toContainText("Principal");
  await expect(field(dialog, "Motivo").getByRole("combobox")).toContainText("Motivo de prueba");
  await expect(dialog.getByRole("textbox", { name: "Contar Arroz premium", exact: true })).toHaveValue("8");
  await expect(field(dialog, "Observaciones").getByRole("textbox")).toHaveValue("conteo persistente");
  await dialog.getByRole("button", { name: "Movimientos de mercancía" }).click();

  const cases = [
    { button: "Movimientos de mercancía", warehouse: "Bodega", note: "movimiento persistente", quantity: "4", extra: async () => {
      await select(page, field(dialog, "Valorar entradas con").getByRole("combobox"), "Precio de venta");
    }, assertExtra: async () => expect(field(dialog, "Valorar entradas con").getByRole("combobox")).toContainText("Precio de venta") },
    { button: "Traslado", warehouse: "Bodega de origen", note: "traslado persistente", quantity: "3", extra: async () => {
      await select(page, field(dialog, "Bodega de destino").getByRole("combobox"), "Auxiliar");
    }, assertExtra: async () => expect(field(dialog, "Bodega de destino").getByRole("combobox")).toContainText("Auxiliar") },
    { button: "Conversión", warehouse: "Bodega", note: "conversión persistente", quantity: "2", extra: async () => {
      await select(page, field(dialog, "Tipo de conversión").getByRole("combobox"), "Varios productos en una salida");
    }, assertExtra: async () => expect(field(dialog, "Tipo de conversión").getByRole("combobox")).toContainText("Varios productos en una salida") },
    { button: "Avería", warehouse: "Bodega", note: "avería persistente", quantity: "1", extra: async () => undefined, assertExtra: async () => undefined },
  ];

  for (const item of cases) {
    if (item.button !== "Movimientos de mercancía")
      await dialog.getByRole("button", { name: item.button }).click();
    await select(page, field(dialog, item.warehouse).getByRole("combobox"), "Principal");
    await item.extra();
    await select(page, field(dialog, "Motivo").getByRole("combobox"), "Motivo de prueba");
    await addProduct(page, dialog);
    await dialog.getByRole("textbox", { name: "Cantidad de Arroz premium" }).fill(item.quantity);
    await field(dialog, "Observaciones").getByRole("textbox").fill(item.note);
    await closeAndReopen(page, dialog);
    await expect(dialog.getByRole("heading", { name: item.button })).toBeVisible();
    await expect(field(dialog, item.warehouse).getByRole("combobox")).toContainText("Principal");
    await expect(field(dialog, "Motivo").getByRole("combobox")).toContainText("Motivo de prueba");
    await expect(dialog.getByRole("textbox", { name: "Cantidad de Arroz premium" })).toHaveValue(item.quantity);
    await expect(field(dialog, "Observaciones").getByRole("textbox")).toHaveValue(item.note);
    await item.assertExtra();
    await dialog.getByRole("button", { name: "Limpiar" }).click();
  }
});

test("recepción conserva proveedor, bodega, soporte, producto y cantidades", async ({ page }) => {
  await mockApi(page);
  await authenticate(page);
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: "Recepción de compra" });
  const supplier = dialog.getByRole("combobox", { name: "Seleccionar supplier" });
  await supplier.click();
  await page.getByRole("option", { name: /Proveedor Andino/ }).click();
  await select(page, field(dialog, "Bodega").getByRole("combobox"), "Principal · PPL");
  await select(page, dialog.getByRole("combobox", { name: "Tipo de soporte" }), "Comprobante interno");
  const search = dialog.getByPlaceholder(/Escanea o busca/);
  await search.fill("Arroz");
  await page.getByRole("option", { name: /Arroz premium/ }).click();
  await dialog.getByRole("spinbutton", { name: "Cantidad en Caja" }).fill("7");
  await dialog.getByPlaceholder("Observaciones de recepción").fill("recepción persistente");
  await dialog.getByRole("button", { name: "Cerrar", exact: true }).click();
  await expect(dialog).toBeHidden();
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  await dialog.getByRole("button", { name: /Datos de la recepción/ }).click();
  await expect(dialog.getByRole("combobox", { name: "Seleccionar supplier" })).toContainText("Proveedor Andino");
  await expect(field(dialog, "Bodega").getByRole("combobox")).toContainText("Principal");
  await expect(dialog.getByRole("combobox", { name: "Tipo de soporte" })).toContainText("Comprobante interno");
  await expect(dialog.getByText("Arroz premium", { exact: true })).toBeVisible();
  await expect(dialog.getByRole("spinbutton", { name: "Cantidad en Caja" })).toHaveValue("7");
  await expect(dialog.getByPlaceholder("Observaciones de recepción")).toHaveValue("recepción persistente");
  await dialog.getByRole("button", { name: "Descartar captura" }).click();
});

test("recepción simple sigue directa y la importación carga catálogos y conceptos contables", async ({ page }) => {
  await mockApi(page);
  await authenticate(page);
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: "Recepción de compra" });

  const supplier = dialog.getByRole("combobox", { name: "Seleccionar supplier" });
  await supplier.click();
  await page.getByRole("option", { name: /Proveedor Andino/ }).click();
  await select(page, field(dialog, "Bodega").getByRole("combobox"), "Principal · PPL");
  await select(page, dialog.getByRole("combobox", { name: "Tipo de soporte" }), "Comprobante interno");
  const search = dialog.getByPlaceholder(/Escanea o busca/);
  await search.fill("Arroz");
  await page.getByRole("option", { name: /Arroz premium/ }).click();

  await expect(dialog.getByText("Opcional. Si la compra no tiene otros costos, no necesitas hacer nada aquí.")).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Confirmar entrada" })).toBeEnabled();
  await expect(dialog.getByText("Valor que entra al inventario", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Total retenciones", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Cuentas por pagar netas", { exact: true })).toBeVisible();

  await select(page, dialog.getByRole("combobox", { name: "Tipo de soporte" }), "Factura comercial extranjera");
  await select(page, field(dialog, "Moneda").getByRole("combobox"), "Dólar estadounidense");
  await expect(field(dialog, "Fuente de la tasa").getByRole("combobox")).toContainText("Tasa pactada");

  await dialog.getByRole("button", { name: "Nacionalización" }).click();
  await expect(dialog.getByRole("paragraph").filter({ hasText: "Declaración de importación" })).toBeVisible();
  await expect(dialog.getByText("Concepto 1", { exact: true })).toBeVisible();
  await expect(dialog.getByText("Concepto 2", { exact: true })).toBeVisible();
  await expect(field(dialog, "Concepto").getByRole("combobox").first()).toContainText("Arancel");
  await expect(field(dialog, "Concepto").getByRole("combobox").nth(1)).toContainText("IVA de importación");
  await expect(field(dialog, "Tratamiento del IVA").getByRole("combobox")).toContainText("IVA descontable");
  await expect(dialog.getByText(/El IVA descontable se reconoce separado/)).toBeVisible();
});
