import { expect, test, type Page, type Route } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const userId = "33333333-3333-3333-3333-333333333333";
const warehouseId = "44444444-4444-4444-4444-444444444444";
const permissions = [
  "dashboard.read", "inventory.read", "inventory.physical-counts.manage",
  "inventory.physical-counts.capture", "inventory.counts.confirm",
  "inventory.adjustments.confirm",
  "purchasing.goods-receipts.read", "purchasing.goods-receipts.create",
  "purchasing.goods-receipts.confirm",
  "purchasing.purchase-orders.read", "purchasing.purchase-orders.create",
];
const json = (route: Route, body: unknown) => route.fulfill({
  status: 200, contentType: "application/json", body: JSON.stringify(body),
});

async function prepare(page: Page) {
  await page.context().addCookies([{
    name: "auth_token", value: "e2e",
    url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000",
    httpOnly: true, sameSite: "Lax",
  }]);
  await page.addInitScript(({ tenant, business, user, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: {
      isAuthenticated: true,
      user: { userId: user, tenantId: tenant, tenantKey: "AURALY", username: "e2e",
        email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E",
        avatarUrl: null, roles: ["Administrator"], permissions: granted },
    }, version: 0 }));
  }, { tenant: tenantId, business: businessId, user: userId, granted: permissions });
  await page.route("**/api/auth/me", route => json(route, {
    userId, tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test",
    firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions,
  }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, {
    tenantId, businessId, roles: ["Administrator"], permissions,
  }));
  await page.route("**/api/tenant-commercial/subscription", route => json(route, {}));
  await page.route("**/api/commerce/v1/parties/role-options?**", route => json(route, { items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }));
  await page.route("**/api/commerce/v1/inventory/warehouses", route => json(route, [
    { warehouseId, code: "PPL", name: "Principal" },
  ]));
  await page.route("**/api/commerce/v1/inventory/reasons?**", route => json(route, [
    { inventoryReasonId: "55555555-5555-5555-5555-555555555555", code: "PHYSICAL_COUNT", name: "Conteo físico", isActive: true },
  ]));
  await page.route(/\/api\/commerce\/v1\/inventory\/(balances|movements|operations)(?:\?.*)?$/, route => json(route, {
    items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0,
  }));
  await page.route("**/api/commerce/v1/goods-receipts?**", route => json(route, {
    items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0,
  }));
  await page.route("**/api/commerce/v1/goods-receipts/options", route => json(route, {
    warehouses: [{ warehouseId, code: "PPL", name: "Principal" }],
    suppliers: [], purchaseEvidenceTypes: [], withholdingConcepts: [], withholdingJurisdictions: [],
    purchaseCostEvidenceTypes: [], purchaseCostKinds: [], purchaseCostTreatments: [],
    purchaseCostAllocationMethods: [], purchaseTaxRates: [], purchaseTaxTreatments: [],
    purchaseCurrencies: [{ code: "COP", label: "Peso colombiano", description: null }],
    exchangeRateSources: [],
  }));
  await page.route("**/api/commerce/v1/purchase-orders?**", route => json(route, {
    items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0,
  }));
  await page.route("**/api/commerce/v1/purchase-orders/suggestions", route => json(route, []));
}

async function writePurchasingDraft(page: Page, storeName: string, key: string, value: unknown) {
  await page.evaluate(({ storeName, key, value }) => new Promise<void>((resolve, reject) => {
    const request = indexedDB.open("auraly-purchasing-work", 2);
    request.onupgradeneeded = () => {
      for (const name of ["goods-receipt-drafts", "purchase-order-drafts"])
        if (!request.result.objectStoreNames.contains(name)) request.result.createObjectStore(name, { keyPath: "key" });
    };
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result;
      const transaction = db.transaction(storeName, "readwrite");
      transaction.objectStore(storeName).put({ key, value, updatedAt: new Date().toISOString() });
      transaction.oncomplete = () => { db.close(); resolve(); };
      transaction.onerror = () => { db.close(); reject(transaction.error); };
    };
  }), { storeName, key, value });
}

async function readPurchasingDraft(page: Page, storeName: string, key: string): Promise<unknown> {
  return page.evaluate(({ storeName, key }) => new Promise((resolve, reject) => {
    const request = indexedDB.open("auraly-purchasing-work", 2);
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result;
      const transaction = db.transaction(storeName, "readonly");
      const get = transaction.objectStore(storeName).get(key);
      get.onsuccess = () => resolve(get.result?.value ?? null);
      transaction.oncomplete = () => db.close();
      transaction.onerror = () => { db.close(); reject(transaction.error); };
    };
  }), { storeName, key });
}

async function writeInventoryDraft(page: Page, key: string, draft: unknown) {
  await page.evaluate(({ key, draft }) => new Promise<void>((resolve, reject) => {
    const request = indexedDB.open("auraly-offline-work", 1);
    request.onupgradeneeded = () => {
      request.result.createObjectStore("operation-drafts", { keyPath: "key" });
    };
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result;
      const transaction = db.transaction("operation-drafts", "readwrite");
      transaction.objectStore("operation-drafts").put({ ...draft as object, key });
      transaction.oncomplete = () => { db.close(); resolve(); };
      transaction.onerror = () => { db.close(); reject(transaction.error); };
    };
  }), { key, draft });
}

async function readInventoryDraft(page: Page, key: string): Promise<unknown> {
  return page.evaluate(key => new Promise((resolve, reject) => {
    const request = indexedDB.open("auraly-offline-work", 1);
    request.onerror = () => reject(request.error);
    request.onsuccess = () => {
      const db = request.result;
      const transaction = db.transaction("operation-drafts", "readonly");
      const get = transaction.objectStore("operation-drafts").get(key);
      get.onsuccess = () => resolve(get.result ?? null);
      transaction.oncomplete = () => db.close();
      transaction.onerror = () => { db.close(); reject(transaction.error); };
    };
  }), key);
}

test("recepción conserva la última edición al cerrar por la X sin enviar un borrador al servidor", async ({ page }) => {
  await prepare(page);
  const writes: string[] = [];
  page.on("request", request => {
    if (["POST", "PUT", "PATCH"].includes(request.method()) && request.url().includes("goods-receipts"))
      writes.push(request.url());
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  const notes = dialog.getByPlaceholder("Observaciones de recepción");
  await notes.fill("Primera versión");
  await notes.fill("Última versión antes de cerrar");
  const started = Date.now();
  await dialog.getByRole("button", { name: "Close" }).click();
  await expect(dialog).toBeHidden();
  expect(Date.now() - started).toBeLessThan(1_000);
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  await expect(dialog.getByPlaceholder("Observaciones de recepción"))
    .toHaveValue("Última versión antes de cerrar");
  await dialog.getByRole("button", { name: "Close" }).click();
  await page.reload();
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  await expect(dialog.getByPlaceholder("Observaciones de recepción"))
    .toHaveValue("Última versión antes de cerrar");
  expect(writes).toEqual([]);
});

test("inventario conserva su captura al cerrar por la X y reabrir inmediatamente", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/inventory");
  await page.getByRole("button", { name: "Nueva operación" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva operación" });
  await dialog.getByRole("textbox", { name: "Observaciones" }).fill("Conteo interrumpido");
  const started = Date.now();
  await dialog.getByRole("button", { name: "Close" }).click();
  await expect(dialog).toBeHidden();
  expect(Date.now() - started).toBeLessThan(1_000);
  await page.getByRole("button", { name: "Nueva operación" }).click();
  await expect(dialog.getByRole("textbox", { name: "Observaciones" }))
    .toHaveValue("Conteo interrumpido");
  await dialog.getByRole("button", { name: "Close" }).click();
  await page.reload();
  await page.getByRole("button", { name: "Nueva operación" }).click();
  await expect(dialog.getByRole("textbox", { name: "Observaciones" }))
    .toHaveValue("Conteo interrumpido");
});

test("orden de compra conserva la última edición local al cerrar por la X", async ({ page }) => {
  await prepare(page);
  const writes: string[] = [];
  page.on("request", request => {
    if (["POST", "PUT", "PATCH"].includes(request.method()) && request.url().includes("purchase-orders"))
      writes.push(request.url());
  });
  await page.goto("/dashboard/purchasing/purchase-orders");
  await page.getByRole("button", { name: "Nueva orden" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva orden de compra" });
  const notes = dialog.getByPlaceholder("Observaciones");
  await notes.fill("Primera versión");
  await notes.fill("Última versión de la orden");
  const started = Date.now();
  await dialog.getByRole("button", { name: "Close" }).click();
  await expect(dialog).toBeHidden();
  expect(Date.now() - started).toBeLessThan(1_000);
  await page.getByRole("button", { name: "Nueva orden" }).click();
  await expect(dialog.getByPlaceholder("Observaciones")).toHaveValue("Última versión de la orden");
  await dialog.getByRole("button", { name: "Close" }).click();
  await page.reload();
  await page.getByRole("button", { name: "Nueva orden" }).click();
  await expect(dialog.getByPlaceholder("Observaciones")).toHaveValue("Última versión de la orden");
  expect(writes).toEqual([]);
});

test("descartar una recepción elimina la captura que se recuperaría al reabrir", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await dialog.getByPlaceholder("Observaciones de recepción").fill("Debe eliminarse");
  await dialog.getByRole("button", { name: "Descartar borrador" }).click();
  await expect(dialog).toBeHidden();
  await page.reload();
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  await expect(dialog.getByPlaceholder("Observaciones de recepción")).toHaveValue("");
});

test("descartar un conteo elimina su captura local", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/inventory");
  await page.getByRole("button", { name: "Nueva operación" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva operación" });
  await dialog.getByRole("textbox", { name: "Observaciones" }).fill("Debe eliminarse");
  await dialog.getByRole("button", { name: "Descartar borrador" }).click();
  await expect(dialog).toBeHidden();
  await page.reload();
  await page.getByRole("button", { name: "Nueva operación" }).click();
  await expect(dialog.getByRole("textbox", { name: "Observaciones" })).toHaveValue("");
});

test("descartar una orden elimina su captura local", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/purchasing/purchase-orders");
  await page.getByRole("button", { name: "Nueva orden" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva orden de compra" });
  await dialog.getByPlaceholder("Observaciones").fill("Debe eliminarse");
  await dialog.getByRole("button", { name: "Descartar captura" }).click();
  await expect(dialog).toBeHidden();
  await page.reload();
  await page.getByRole("button", { name: "Nueva orden" }).click();
  await expect(dialog.getByPlaceholder("Observaciones")).toHaveValue("");
});

test("confirmar una orden borra la copia local solo después de la aceptación", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/purchasing/purchase-orders");
  const key = `purchase-order:${userId}:${businessId}`;
  const id = "66666666-6666-6666-6666-666666666666";
  const draft = {
    id, warehouseId, supplierId: "77777777-7777-7777-7777-777777777777",
    orderedAt: "2026-09-28T09:00", expectedAt: "2026-09-29T09:00",
    targetCoverageDays: 7, notes: "Confirmación de prueba", token: null,
    suggestionsInitialized: true,
    lines: [{ lineId: "88888888-8888-8888-8888-888888888888", lineNumber: 1,
      productId: "99999999-9999-9999-9999-999999999999", description: "Producto de prueba",
      orderedQuantity: 1, unitCost: 1000, discountAmount: 0, taxCode: "00", taxRate: 0,
      taxTreatment: "Excluded", presentationName: "Unidad", presentationQuantity: 1,
      unitsPerPresentation: 1 }],
  };
  await writePurchasingDraft(page, "purchase-order-drafts", key, draft);
  let attempts = 0;
  await page.route("**/api/commerce/v1/purchase-orders/confirm", route => {
    attempts++;
    return attempts === 1
      ? route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ message: "Fallo simulado" }) })
      : json(route, { purchaseOrderId: id, documentNumber: "OC-1", status: "Open" });
  });
  await page.getByRole("button", { name: "Nueva orden" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva orden de compra" });
  await expect(dialog.getByPlaceholder("Observaciones")).toHaveValue("Confirmación de prueba");
  await dialog.getByRole("button", { name: "Confirmar orden" }).click();
  await expect.poll(() => attempts).toBe(1);
  await expect(dialog).toBeVisible();
  expect(await readPurchasingDraft(page, "purchase-order-drafts", key)).toMatchObject({ id });
  await dialog.getByRole("button", { name: "Confirmar orden" }).click();
  await expect.poll(() => attempts).toBe(2);
  await expect(dialog).toBeHidden();
  expect(await readPurchasingDraft(page, "purchase-order-drafts", key)).toBeNull();
});

test("confirmar una recepción conserva la captura ante fallo y la borra al aceptar", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/purchasing/goods-receipts");
  const key = `goods-receipt:${userId}:${businessId}`;
  const id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
  await writePurchasingDraft(page, "goods-receipt-drafts", key, {
    draftId: id, warehouseId, supplierId: "77777777-7777-7777-7777-777777777777",
    supplierInvoiceNumber: "", supplierInvoiceDate: "2026-09-28",
    purchaseEvidenceType: "BuyerElectronicSupportDocument", receivedAt: "2026-09-28T09:00",
    createsPayable: false, dueDate: "", notes: "Recepción de prueba", concurrencyToken: null,
    withholdingConceptCode: "", withholdingJurisdictionCode: "", purchaseOrderId: "",
    currencyCode: "COP", exchangeRate: 1, exchangeRateDate: "2026-09-28",
    exchangeRateSource: "FunctionalCurrency", additionalCostDocuments: [],
    lines: [{ lineNumber: 1, productId: "99999999-9999-9999-9999-999999999999",
      description: "Producto de prueba", quantity: 1, unitCost: 1000, discountAmount: 0,
      taxCode: "00", taxRate: 0, taxTreatment: "NotApplicable", presentationName: "Unidad",
      baseUnitCode: "UND", presentationQuantity: 1, unitsPerPresentation: 1 }],
  });
  let attempts = 0;
  await page.route("**/api/commerce/v1/goods-receipts/confirm", route => {
    attempts++;
    return attempts === 1
      ? route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ message: "Fallo simulado" }) })
      : json(route, { documentId: id, movementId: crypto.randomUUID(), documentNumber: "REC-1",
        status: "Accepted", processingSequence: 1, idempotentReplay: false });
  });
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await expect(dialog.getByPlaceholder("Observaciones de recepción")).toHaveValue("Recepción de prueba");
  await dialog.getByRole("button", { name: "Confirmar entrada" }).click();
  await expect.poll(() => attempts).toBe(1);
  await expect(dialog).toBeVisible();
  expect(await readPurchasingDraft(page, "goods-receipt-drafts", key)).toMatchObject({ draftId: id });
  await dialog.getByRole("button", { name: "Confirmar entrada" }).click();
  await expect.poll(() => attempts).toBe(2);
  await expect(dialog).toBeHidden();
  expect(await readPurchasingDraft(page, "goods-receipt-drafts", key)).toBeNull();
});

test("aplicar un conteo conserva la captura ante fallo y la borra al aceptar", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/inventory");
  const key = `inventory:${businessId}:physical-count-capture`;
  const id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
  await writeInventoryDraft(page, key, {
    businessId, kind: "count", documentId: id, warehouseId, destinationId: "",
    reason: "PHYSICAL_COUNT", notes: "Conteo de prueba", countDocumentId: null,
    conversionType: "SPLIT", countCaptureStage: "Count", updatedAt: new Date().toISOString(),
    lines: [{ productId: "99999999-9999-9999-9999-999999999999", productCode: "P1",
      productName: "Producto de prueba", unitCode: "UND", stock: 1, quantity: "1",
      count: "1", recount: "", preCount: "", cost: "", direction: "INPUT",
      systemQuantity: 1 }],
  });
  let attempts = 0;
  await page.route("**/api/commerce/v1/stock-counts/apply", route => {
    attempts++;
    return attempts === 1
      ? route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ message: "Fallo simulado" }) })
      : json(route, { documentId: id, documentNumber: "CNT-1", status: "Accepted" });
  });
  await page.getByRole("button", { name: "Nueva operación" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva operación" });
  await expect(dialog.getByRole("textbox", { name: "Observaciones" })).toHaveValue("Conteo de prueba");
  await dialog.getByRole("button", { name: "Aplicar inventario" }).click();
  await expect.poll(() => attempts).toBe(1);
  await expect(dialog).toBeVisible();
  expect(await readInventoryDraft(page, key)).toMatchObject({ documentId: id });
  await dialog.getByRole("button", { name: "Aplicar inventario" }).click();
  await expect.poll(() => attempts).toBe(2);
  await expect(dialog).toBeHidden();
  expect(await readInventoryDraft(page, key)).toBeNull();
});

test("confirmar un movimiento elimina su borrador solo tras aceptarlo", async ({ page }) => {
  await prepare(page);
  await page.goto("/dashboard/inventory");
  const key = `inventory:${businessId}:adjustment`;
  const id = "cccccccc-cccc-cccc-cccc-cccccccccccc";
  await writeInventoryDraft(page, key, {
    businessId, kind: "adjustment", documentId: id, warehouseId, destinationId: "",
    reason: "PHYSICAL_COUNT", notes: "Ajuste de prueba", countDocumentId: null,
    conversionType: "SPLIT", valuationBasis: "Cost", updatedAt: new Date().toISOString(),
    lines: [{ productId: "99999999-9999-9999-9999-999999999999", productCode: "P1",
      productName: "Producto de prueba", unitCode: "UND", stock: 1, quantity: "-1",
      cost: "", direction: "INPUT", systemQuantity: null }],
  });
  let attempts = 0;
  await page.route("**/api/commerce/v1/inventory-adjustments/confirm", route => {
    attempts++;
    return attempts === 1
      ? route.fulfill({ status: 500, contentType: "application/json", body: JSON.stringify({ message: "Fallo simulado" }) })
      : json(route, { documentId: id, documentNumber: "AJ-1", status: "Accepted" });
  });
  await page.getByRole("button", { name: "Nueva operación" }).click();
  const dialog = page.getByRole("dialog", { name: "Nueva operación" });
  await dialog.getByRole("button", { name: /Movimientos de mercancía/ }).click();
  await expect(dialog.locator("textarea")).toHaveValue("Ajuste de prueba");
  await dialog.getByRole("button", { name: "Confirmar movimientos de mercancía" }).click();
  await expect.poll(() => attempts).toBe(1);
  await expect(dialog).toBeVisible();
  expect(await readInventoryDraft(page, key)).toMatchObject({ documentId: id });
  await dialog.getByRole("button", { name: "Confirmar movimientos de mercancía" }).click();
  await expect.poll(() => attempts).toBe(2);
  await expect(dialog).toBeHidden();
  expect(await readInventoryDraft(page, key)).toBeNull();

  await page.getByRole("button", { name: "Nueva operación" }).click();
  await dialog.getByRole("button", { name: /Movimientos de mercancía/ }).click();
  await dialog.locator("textarea").fill("Captura para descartar");
  await dialog.getByRole("button", { name: "Descartar borrador" }).click();
  await expect(dialog).toBeHidden();
  await page.reload();
  expect(await readInventoryDraft(page, key)).toBeNull();
});
