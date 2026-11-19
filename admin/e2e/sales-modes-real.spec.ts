import { randomUUID } from "node:crypto";
import { writeFileSync } from "node:fs";
import { expect, test, type Page, type Response } from "@playwright/test";
import type { AccountingEntry } from "../src/services/api/accounting";
import type { CatalogProductDetail } from "../src/services/api/products";
import type { CustomerAcceptance, PartyWorkspaceDetail } from "../src/services/api/parties";
import type { SellerCatalogPage, SellerOrderResult } from "../src/services/api/seller-orders";
import type { CommerceOrderDetail, InvoiceOrdersResponse } from "../src/services/orders/commerce-orders-client";
import { realAccountingContext, realPartyRequest, selectRealCashForm, waitForRealPosting } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";

test.use({ serviceWorkers: "block", actionTimeout: 40_000 });
type Context = Awaited<ReturnType<typeof realAccountingContext>>;

async function checkoutResult(response: Response, context: Context) {
  expect(response.ok(), await response.text()).toBe(true);
  const result = await response.json() as { receipt: { documentId: string }; isDuplicate?: boolean; isReplay?: boolean };
  expect(result.receipt.documentId).toBeTruthy();
  await waitForRealPosting(context, result.receipt.documentId);
  const entry = await context.scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${result.receipt.documentId}`);
  expect(entry.debitTotal).toBeGreaterThan(0);
  expect(entry.debitTotal).toBe(entry.creditTotal);
  const command = response.request();
  const replay = await context.scopedJson<typeof result>("POST", new URL(response.url()).pathname, command.postDataJSON(),
    { "Idempotency-Key": command.headers()["idempotency-key"] });
  expect(replay.receipt.documentId).toBe(result.receipt.documentId);
  expect(replay.isDuplicate || replay.isReplay).toBe(true);
  return result.receipt.documentId;
}

async function finishCash(page: Page, context: Context, amount: number) {
  await page.getByRole("button", { name: /^Cobrar/ }).click();
  const payment = page.getByRole("dialog", { name: "Finalizar venta", exact: true });
  await selectRealCashForm(page, payment);
  await payment.getByRole("textbox", { name: "Valor recibido en Efectivo", exact: true }).fill(String(amount));
  const completed = page.waitForResponse(r => r.request().method() === "POST" && /\/pos\/drafts\/[^/]+\/complete$/.test(new URL(r.url()).pathname));
  await payment.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const documentId = await checkoutResult(await completed, context);
  await expect(payment).toBeHidden();
  return documentId;
}

for (const mode of ["Store", "RestaurantBar"] as const) test(`${mode}: factura y recupera, modifica y factura pedidos reales`, async ({ page, browser }) => {
  test.skip(process.env.AURALY_E2E_TARGET !== "dev", "Usa exclusivamente las sedes aprobadas de AURALY DEV.");
  test.setTimeout(420_000);
  const businessId = mode === "Store" ? "2b8de478-3bce-4f28-b66d-3e53e5dc2c36" : "b24a3ab4-a226-4dab-bc0c-4d634e34c3fb";
  // Independent browser storage keeps setup credentials and branch selection
  // out of the cashier journey; each page has a single context initializer.
  const adminPage = await browser.newPage({ baseURL: test.info().project.use.baseURL, serviceWorkers: "block" });
  const admin = await realAccountingContext(adminPage);
  const roles = await admin.scopedJson<{ items: Array<{ roleId: string; name: string; isActive: boolean }> }>("GET", "/api/roles?page=1&pageSize=50");
  const role = roles.items.find(r => r.isActive && ["Administrador", "Administrator", "ADMIN"].includes(r.name));
  expect(role, "Reutiliza el rol existente; no crea permisos ni modifica roles de otros usuarios.").toBeTruthy();
  const suffix = randomUUID().slice(0, 8);
  const credentials = { tenantKey: process.env.AURALY_E2E_TENANT_KEY!, username: `qa.mode.${suffix}`, password: `T!${randomUUID()}a9` };
  const actor = await admin.scopedJson<{ userId: string }>("POST", "/api/users", {
    username: credentials.username, email: `${credentials.username}@example.test`, password: credentials.password,
    firstName: "QA", lastName: mode, phoneNumber: null, roles: [{ roleId: role!.roleId, businessId }],
  });
  const documents: string[] = [];
  try {
    const context = await realAccountingContext(page, businessId, credentials);
    console.info(`${mode}: acceso real del usuario de prueba confirmado`);
    const api = "/api/commerce/v1";
    const workspace = (await context.scopedJson<Array<{ businessId: string; warehouseId: string; pointOfSaleMode: string }>>("GET", `${api}/pos/workspace/options`))
      .find(b => b.businessId === businessId && b.pointOfSaleMode === mode);
    expect(workspace).toBeTruthy();
    const productCreated = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`${api}/products`));
    const productName = await createRealProduct(page);
    const product = await (await productCreated).json() as CatalogProductDetail;
    const reasons = await context.scopedJson<Array<{ code: string; isActive: boolean }>>("GET", `${api}/reasons?reasonType=InventoryAdjustment`);
    const reason = reasons.find(r => r.code === "INITIAL_BALANCE" && r.isActive); expect(reason).toBeTruthy();
    const opening = randomUUID();
    await context.scopedJson("POST", `${api}/inventory-adjustments/confirm`, {
      documentId: opening, businessId, warehouseId: workspace!.warehouseId, occurredAt: new Date().toISOString(),
      reasonCode: reason!.code, costCenterId: null, notes: `E2E-MODE-${suffix}`,
      lines: [{ lineNumber: 1, productId: product.productId, quantityChange: 20, explicitUnitCost: 10000 }],
    }, { "Idempotency-Key": `inventory-adjustment-${opening}` });
    await waitForRealPosting(context, opening);
    console.info(`${mode}: producto y existencias preparados por los motores actuales`);
    const customer = await context.scopedJson<CustomerAcceptance>("POST", `${api}/customers`, await realPartyRequest(context, `Cliente E2E modo ${suffix}`));
    const party = await context.scopedJson<PartyWorkspaceDetail>("GET", `${api}/parties/${customer.partyId}`);
    const site = party.sites?.find(s => s.isActive) ?? party.primarySite; expect(site?.isActive).toBe(true);
    const catalog = await context.scopedJson<SellerCatalogPage>("POST", `${api}/seller-orders/catalog`, {
      businessId, warehouseId: workspace!.warehouseId, customerId: customer.customerId, search: productName, skip: 0, take: 10,
    });
    const priced = catalog.items.find(p => p.productId === product.productId); expect(priced?.unitPrice).toBeGreaterThan(0);
    const balances = () => context.scopedJson<{ items: Array<{ quantityOnHand: number }> }>("GET", `${api}/inventory/balances?warehouseId=${workspace!.warehouseId}&productId=${product.productId}&page=1&pageSize=10`);
    let tableCode: string | null = null;
    if (mode === "RestaurantBar") {
      const rooms = await context.scopedJson<{ items: Array<{ roomId: string }> }>("GET", `${api}/restaurant/rooms?page=1&pageSize=20`);
      expect(rooms.items.length).toBeGreaterThan(0); tableCode = `QA-${suffix}`;
      await context.scopedJson("POST", `${api}/restaurant/tables`, { tableId: randomUUID(), businessId, roomId: rooms.items[0].roomId, code: tableCode, name: "Regresión de facturación", capacity: 2 });
    }
    await page.goto("/pos?workspace=change");
    await page.getByRole("button", { name: "Comprobante de venta", exact: true }).click();
    await page.getByRole("button", { name: "Continuar a ventas", exact: true }).click();
    if (mode === "Store") {
      const search = page.getByRole("textbox", { name: "Buscar producto en tiendas", exact: true });
      await search.fill(product.productCode);
      await page.getByRole("button", { name: `Agregar ${productName}`, exact: true }).click();
      const quantity = page.getByRole("spinbutton", { name: `Cantidad de ${productName}`, exact: true });
      await quantity.fill("2"); await quantity.press("Enter"); await expect(quantity).toHaveValue("2");
      documents.push(await finishCash(page, context, priced!.unitPrice * 2));
    } else {
      await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?", exact: true })).toBeVisible();
      await page.getByRole("button", { name: "Lista de mesas", exact: true }).click();
      await page.getByRole("button", { name: new RegExp(tableCode!) }).click();
      await page.getByRole("textbox", { name: "Buscar producto", exact: true }).fill(product.productCode);
      await page.getByRole("button", { name: `Agregar ${productName}`, exact: true }).click();
      await page.getByRole("button", { name: `Agregar uno de ${productName}`, exact: true }).click();
      await page.getByRole("button", { name: "Facturar mesa", exact: true }).click();
      const payment = page.getByRole("dialog", { name: "Cobrar mesa", exact: true });
      if (await payment.getByText("Factura electrónica", { exact: true }).count()) {
        await payment.getByRole("button", { name: "Cambiar documento", exact: true }).click();
        await page.getByRole("dialog", { name: "Tipo de documento", exact: true }).getByRole("button", { name: /Comprobante de venta/ }).click();
      }
      await payment.locator("#restaurant-actual-tip").fill("0");
      await payment.getByRole("combobox", { name: "Medio de pago", exact: true }).click();
      await page.getByRole("option", { name: "Tarjeta débito", exact: true }).click();
      await payment.getByRole("combobox", { name: "Forma de pago", exact: true }).click();
      await page.getByRole("option").first().click();
      await payment.getByRole("combobox", { name: "Franquicia", exact: true }).click();
      await page.getByRole("option").first().click();
      await payment.getByRole("textbox", { name: "Número de aprobación", exact: true }).fill(`MODE-${suffix}`);
      await payment.getByLabel("Valor cobrado por consumo", { exact: true }).fill(String(priced!.unitPrice * 2));
      await payment.getByRole("button", { name: "Sin imprimir", exact: true }).click();
      const completed = page.waitForResponse(r => r.request().method() === "POST" && /\/restaurant\/accounts\/[^/]+\/complete$/.test(new URL(r.url()).pathname));
      await payment.getByRole("button", { name: "Cobrar y emitir documento", exact: true }).click();
      documents.push(await checkoutResult(await completed, context));
      await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?", exact: true })).toBeVisible();
    }
    await expect.poll(async () => (await balances()).items[0]?.quantityOnHand, { timeout: 30_000, intervals: [500, 1000, 2000] }).toBe(18);
    console.info(`${mode}: venta emitida y reintento sin duplicados verificados`);
    let order: SellerOrderResult;
    if (mode === "Store") {
      await page.getByRole("textbox", { name: "Buscar producto en tiendas", exact: true }).fill(product.productCode);
      await page.getByRole("button", { name: `Agregar ${productName}`, exact: true }).click();
      await page.getByRole("button", { name: "Buscar cliente", exact: true }).click();
      const picker = page.getByRole("dialog", { name: "Buscar cliente", exact: true });
      await picker.getByPlaceholder("Nombre o identificación").fill(party.displayName);
      await picker.locator("button").filter({ hasText: party.displayName }).click();
      const saved = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith(`${api}/seller-orders`));
      await page.getByRole("button", { name: /^Guardar pedido/ }).click();
      const response = await saved; expect(response.ok(), await response.text()).toBe(true); order = await response.json();
    } else {
      order = await context.scopedJson<SellerOrderResult>("POST", `${api}/seller-orders`, {
        businessId, warehouseId: workspace!.warehouseId, customerId: customer.customerId, partySiteId: site!.partySiteId,
        routeId: null, routeStopId: null, capturedOffline: false, notes: `E2E-MODE-${suffix}`, idempotencyKey: randomUUID(),
        lines: [{ productId: product.productId, description: productName, quantity: 1, unitPrice: priced!.unitPrice,
          discountAmount: 0, priceSource: priced!.priceSource, documentUnitCost: priced!.documentUnitCost }],
      });
      // Restaurant's order drawer belongs to the account header. Open an
      // empty QA table after the preceding account was fully invoiced.
      const rooms = await context.scopedJson<{ items: Array<{ roomId: string }> }>("GET", `${api}/restaurant/rooms?page=1&pageSize=20`);
      const orderTableCode = `QAO-${suffix}`;
      await context.scopedJson("POST", `${api}/restaurant/tables`, { tableId: randomUUID(), businessId, roomId: rooms.items[0].roomId,
        code: orderTableCode, name: "Regresión de recuperación", capacity: 2 });
      await page.goto("/pos/restaurant");
      await page.getByRole("button", { name: "Lista de mesas", exact: true }).click();
      await page.getByRole("button", { name: new RegExp(orderTableCode) }).click();
    }
    expect(order.status).toBe("Confirmed");
    console.info(`${mode}: pedido creado y confirmado`);
    await page.getByRole("button", { name: "Ver pedidos", exact: true }).click();
    const orders = page.getByRole("dialog", { name: mode === "Store" ? "Ventas y pedidos" : "Pedidos", exact: true });
    await orders.getByPlaceholder("Número de pedido", { exact: true }).fill(order.orderNumber);
    await orders.getByRole("button", { name: /^Recuperar/ }).click();
    const quantity = page.getByRole("spinbutton", { name: `Cantidad de ${productName}`, exact: true });
    await expect(quantity).toHaveValue("1"); await quantity.fill("2"); await quantity.press("Enter");
    const updating = page.waitForResponse(r => r.request().method() === "PUT" && r.url().endsWith(`/seller-orders/${order.orderId}`));
    await page.getByRole("button", { name: /^Guardar cambios/ }).click();
    await page.getByRole("alertdialog", { name: "¿Actualizar el pedido recuperado?", exact: true }).getByRole("button", { name: "Sí, actualizar", exact: true }).click();
    expect((await updating).ok()).toBe(true);
    const edited = await context.scopedJson<CommerceOrderDetail>("GET", `${api}/orders/${order.orderId}`);
    expect(edited.lines[0].quantity).toBe(2); expect(edited.claim).toBeNull();
    console.info(`${mode}: recuperación, edición y actualización del pedido verificadas`);
    await page.goto("/dashboard/orders");
    await page.getByRole("button", { name: `Recuperar ${order.orderNumber}`, exact: true }).click();
    await expect(quantity).toHaveValue("2"); await page.reload(); await expect(quantity).toHaveValue("2");
    documents.push(await finishCash(page, context, priced!.unitPrice * 2));
    const invoiced = await context.scopedJson<CommerceOrderDetail>("GET", `${api}/orders/${order.orderId}`);
    expect(invoiced.invoiceDocumentId).toBe(documents[1]);
    console.info(`${mode}: pedido recuperado facturado desde POS`);
    const pending = await context.scopedJson<SellerOrderResult>("POST", `${api}/seller-orders`, {
      businessId, warehouseId: workspace!.warehouseId, customerId: customer.customerId, partySiteId: site!.partySiteId,
      routeId: null, routeStopId: null, capturedOffline: false, notes: `E2E-MODE-BATCH-${suffix}`, idempotencyKey: randomUUID(),
      lines: [{ productId: product.productId, description: productName, quantity: 1, unitPrice: priced!.unitPrice,
        discountAmount: 0, priceSource: priced!.priceSource, documentUnitCost: priced!.documentUnitCost }],
    });
    await page.goto("/dashboard/orders");
    await page.getByRole("checkbox", { name: `Seleccionar ${pending.orderNumber}`, exact: true }).check();
    await page.getByRole("button", { name: "Comprobante de venta", exact: true }).click();
    await page.getByRole("checkbox", { name: "Imprimir al facturar", exact: true }).uncheck();
    await page.getByRole("combobox", { name: "Forma de pago del lote", exact: true }).click(); await page.getByRole("option").first().click();
    const batchResponse = page.waitForResponse(r => r.request().method() === "POST" && r.url().endsWith("/orders/invoice"));
    await page.getByRole("button", { name: "Emitir de contado (1)", exact: true }).click();
    const emitted = await batchResponse; expect(emitted.ok(), await emitted.text()).toBe(true);
    const result = await emitted.json() as InvoiceOrdersResponse;
    expect(result.results[0].status).toBe("Invoiced"); documents.push(result.results[0].documentId!);
    await waitForRealPosting(context, documents[2]);
    await expect.poll(async () => (await balances()).items[0]?.quantityOnHand, { timeout: 30_000, intervals: [500, 1000, 2000] }).toBe(15);
    writeFileSync(test.info().outputPath("sales-mode-proof.json"), JSON.stringify({ mode, businessId, warehouseId: workspace!.warehouseId,
      actorId: actor.userId, productId: product.productId, documents, recoveredOrderId: order.orderId, batchOrderId: pending.orderId,
      openingStock: 20, finalStock: 15, realLogin: true, originalRolesUnchanged: true, productionWrites: 0 }, null, 2));
    await page.screenshot({ path: test.info().outputPath("sales-mode-final.png"), fullPage: true });
  } finally {
    writeFileSync(test.info().outputPath("sales-mode-attempt.json"), JSON.stringify({ mode, businessId, actorId: actor.userId, documents, productionWrites: 0 }, null, 2));
    try { await admin.scopedJson("POST", `/api/users/${actor.userId}/deactivate`, {}); }
    finally { credentials.password = ""; await adminPage.close(); }
  }
});
