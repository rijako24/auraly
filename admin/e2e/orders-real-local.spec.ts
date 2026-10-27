import { randomUUID } from "node:crypto";
import { expect, test, type Response } from "@playwright/test";
import type { AccountingEntry } from "../src/services/api/accounting";
import type { CustomerAcceptance, PartyWorkspaceDetail } from "../src/services/api/parties";
import type { SellerCatalogPage, SellerOrderResult } from "../src/services/api/seller-orders";
import type { CommerceOrderDetail, InvoiceOrdersResponse } from "../src/services/orders/commerce-orders-client";
import type { SalesWorkspaceOption } from "../src/services/pos/online-pos-client";
import type { CatalogProductDetail } from "../src/services/api/products";
import type { InventoryBalanceItem, InventoryReasonItem, Page as InventoryPage } from "../src/services/api/inventory";
import { realAccountingWorkspace, realPartyRequest } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";

test("pedidos reales conservan la edición y facturan por el motor de ventas y contabilidad", async ({ page }) => {
  test.setTimeout(180_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const api = "/api/commerce/v1";
  const workspace = (await scopedJson<SalesWorkspaceOption[]>("GET", `${api}/pos/workspace/options`))
    .find(item => item.businessId === businessId && item.pointOfSaleMode === "DirectSale");
  expect(workspace, "La base aislada necesita una bodega habilitada para venta directa.").toBeTruthy();
  const session = await scopedJson<{ workSessionId: string }>("POST", `${api}/work-sessions/current`, {
    businessId, warehouseId: workspace!.warehouseId, deviceId: null,
  });
  const active = await scopedJson<{ draftId: string; version: number; sourceOrderId: string | null; lines: unknown[] }>("POST", `${api}/pos/drafts/active`, {
    context: { businessId, warehouseId: workspace!.warehouseId, workSessionId: session.workSessionId },
  });
  if (active.lines.length) {
    expect(active.sourceOrderId, "No se borra una venta ajena a esta regresión.").toBeTruthy();
    const abandoned = await scopedJson<CommerceOrderDetail>("GET", `${api}/orders/${active.sourceOrderId}`);
    expect(abandoned.notes, "Solo se limpia el pedido de un intento previo de esta prueba local.").toMatch(/^E2E-PED-/);
    await scopedJson("POST", `${api}/pos/drafts/${active.draftId}/reset`, { expectedVersion: active.version }, { "Idempotency-Key": randomUUID() });
  }
  const customer = await scopedJson<CustomerAcceptance>("POST", `${api}/customers`,
    await realPartyRequest(context, `Cliente E2E ${randomUUID().slice(0, 8)}`));
  const party = await scopedJson<PartyWorkspaceDetail>("GET", `${api}/parties/${customer.partyId}`);
  const site = party.sites?.find(item => item.isActive) ?? party.primarySite;
  expect(site?.isActive, "El cliente necesita una sede activa explícita.").toBe(true);
  const productResponse = page.waitForResponse(response => response.request().method() === "POST" &&
    response.url().endsWith(`${api}/products`));
  const productName = await createRealProduct(page);
  const createdProduct = await (await productResponse).json() as CatalogProductDetail;
  expect(createdProduct.manageInventory).toBe(true);
  const reasons = await scopedJson<InventoryReasonItem[]>("GET", `${api}/reasons?reasonType=InventoryAdjustment`);
  const openingReason = reasons.find(reason => reason.code === "INITIAL_BALANCE" && reason.isActive);
  expect(openingReason, "La fixture local necesita su motivo activo de entrada inicial.").toBeTruthy();
  const stockDocumentId = randomUUID();
  await scopedJson("POST", `${api}/inventory-adjustments/confirm`, {
    documentId: stockDocumentId, businessId, warehouseId: workspace!.warehouseId,
    occurredAt: new Date().toISOString(), reasonCode: openingReason!.code,
    costCenterId: null, notes: "Existencias de la regresión local de pedidos",
    lines: [{ lineNumber: 1, productId: createdProduct.productId, quantityChange: 20, explicitUnitCost: 10000 }],
  }, { "Idempotency-Key": `inventory-adjustment-${stockDocumentId}` });
  const catalog = await scopedJson<SellerCatalogPage>("POST", `${api}/seller-orders/catalog`, {
    businessId, warehouseId: workspace!.warehouseId, customerId: customer.customerId, search: productName, skip: 0, take: 10,
  });
  const product = catalog.items.find(item => item.name === productName);
  expect(product).toBeTruthy();
  expect(product!.manageStock).toBe(true);
  expect(product!.unitPrice).toBeGreaterThan(0);
  expect(product!.documentUnitCost).toBeGreaterThan(0);
  const tag = `E2E-PED-${randomUUID()}`;
  const orders: SellerOrderResult[] = [];
  for (let index = 0; index < 2; index++) {
    orders.push(await scopedJson<SellerOrderResult>("POST", `${api}/seller-orders`, {
      businessId, warehouseId: workspace!.warehouseId, customerId: customer.customerId, partySiteId: site!.partySiteId,
      routeId: null, routeStopId: null, capturedOffline: false, notes: `${tag}-${index}`,
      idempotencyKey: randomUUID(), lines: [{ productId: product!.productId, description: product!.name,
        quantity: 1, unitPrice: product!.unitPrice, discountAmount: 0, priceSource: product!.priceSource,
        documentUnitCost: product!.documentUnitCost }],
    }));
  }
  expect(orders.every(order => order.status === "Confirmed")).toBe(true);
  await page.goto("/dashboard/orders");
  const card = page.locator("article").filter({ has: page.getByRole("checkbox", { name: `Seleccionar ${orders[0].orderNumber}`, exact: true }) });
  await card.getByRole("button", { name: "Detalle", exact: true }).click();
  await page.getByRole("dialog", { name: `Pedido ${orders[0].orderNumber}`, exact: true })
    .getByRole("button", { name: "Editar pedido completo", exact: true }).click();
  const editing = page.getByRole("dialog", { name: `Editar ${orders[0].orderNumber}`, exact: true });
  await editing.getByRole("button", { name: "1", exact: true }).click();
  const quantity = page.getByRole("dialog", { name: "Cantidad", exact: true });
  await quantity.getByRole("textbox").fill("2");
  await quantity.getByRole("button", { name: "Aplicar", exact: true }).click();
  const changedNote = `${tag}-EDITADO`;
  await editing.getByPlaceholder("Nota para este pedido (opcional)").fill(changedNote);
  const updating = page.waitForResponse(response => response.request().method() === "PUT" && response.url().endsWith(`/seller-orders/${orders[0].orderId}`));
  await editing.getByRole("button", { name: /^Actualizar pedido/ }).click();
  const updated = await updating;
  expect(updated.ok(), await updated.text()).toBe(true);
  await expect(editing).toBeHidden();
  const afterEdit = await scopedJson<CommerceOrderDetail>("GET", `${api}/orders/${orders[0].orderId}`);
  expect(afterEdit.notes).toBe(changedNote);
  expect(afterEdit).toMatchObject({ customerId: customer.customerId, partySiteId: site!.partySiteId });
  expect(afterEdit.lines).toHaveLength(1);
  expect(afterEdit.lines[0]).toMatchObject({ productId: product!.productId, quantity: 2, unitPrice: product!.unitPrice });
  expect(afterEdit.total).toBe(product!.unitPrice * 2);
  await page.getByRole("button", { name: `Recuperar ${orders[0].orderNumber}`, exact: true }).click();
  await expect(page).toHaveURL(/\/pos(?:\?|$)/);
  const posQuantity = page.getByRole("spinbutton", { name: `Cantidad de ${productName}`, exact: true });
  await expect(posQuantity).toHaveValue("2", { timeout: 30_000 });
  await posQuantity.fill("3");
  await posQuantity.press("Enter");
  await page.getByRole("button", { name: /^Guardar cambios/ }).click();
  const confirmation = page.getByRole("alertdialog", { name: "¿Actualizar el pedido recuperado?", exact: true });
  const savingRecovered = page.waitForResponse(response => response.request().method() === "PUT" && response.url().endsWith(`/seller-orders/${orders[0].orderId}`));
  await confirmation.getByRole("button", { name: "Sí, actualizar", exact: true }).click();
  const recoveredSaved = await savingRecovered;
  expect(recoveredSaved.ok(), await recoveredSaved.text()).toBe(true);
  await expect(posQuantity).toHaveCount(0);
  const afterRecovery = await scopedJson<CommerceOrderDetail>("GET", `${api}/orders/${orders[0].orderId}`);
  expect(afterRecovery.orderId).toBe(orders[0].orderId);
  expect(afterRecovery).toMatchObject({ customerId: customer.customerId, partySiteId: site!.partySiteId });
  expect(afterRecovery.claim).toBeNull();
  expect(afterRecovery.lines[0]).toMatchObject({ productId: product!.productId, quantity: 3, unitPrice: product!.unitPrice });
  expect(afterRecovery.total).toBe(product!.unitPrice * 3);
  await page.goto("/dashboard/orders");
  for (const order of orders) await page.getByRole("checkbox", { name: `Seleccionar ${order.orderNumber}`, exact: true }).check();
  await page.getByRole("button", { name: "Comprobante de venta", exact: true }).click();
  await page.getByRole("checkbox", { name: "Imprimir al facturar", exact: true }).uncheck();
  const invoiceResponses: Response[] = [];
  page.on("response", response => {
    if (response.request().method() === "POST" && response.url().endsWith("/orders/invoice")) {
      invoiceResponses.push(response);
    }
  });
  await page.getByRole("button", { name: "Emitir en efectivo (2)", exact: true }).click();
  await expect(page.getByText("2 pedidos emitidos correctamente.", { exact: true })).toBeVisible({ timeout: 60_000 });
  const emissions = await Promise.all(invoiceResponses.map(async response => {
    expect(response.ok(), await response.text()).toBe(true);
    return await response.json() as InvoiceOrdersResponse;
  }));
  expect(emissions).toHaveLength(2);
  const results = emissions.flatMap(item => item.results);
  expect(results.map(item => item.orderId).sort()).toEqual(orders.map(item => item.orderId).sort());
  expect(results.every(item => item.status === "Invoiced" && item.documentId)).toBe(true);
  expect(new Set(results.map(item => item.documentId)).size).toBe(2);
  for (const [index, result] of results.entries()) {
    const detail = await scopedJson<CommerceOrderDetail>("GET", `${api}/orders/${result.orderId}`);
    expect(detail.invoiceDocumentId).toBe(result.documentId);
    expect(detail).toMatchObject({ customerId: customer.customerId, partySiteId: site!.partySiteId });
    const entry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${result.documentId}`);
    expect(entry.debitTotal).toBeGreaterThan(0);
    expect(entry.debitTotal).toBe(entry.creditTotal);
    const command = invoiceResponses[index].request();
    const replay = await scopedJson<InvoiceOrdersResponse>("POST", `${api}/orders/invoice`,
      command.postDataJSON(), { "Idempotency-Key": command.headers()["idempotency-key"] });
    expect(replay.isReplay).toBe(true);
    expect(replay.results[0].documentId).toBe(result.documentId);
  }
  const balances = await scopedJson<InventoryPage<InventoryBalanceItem>>("GET",
    `${api}/inventory/balances?warehouseId=${workspace!.warehouseId}&productId=${product!.productId}&page=1&pageSize=50`);
  expect(balances.items).toHaveLength(1);
  expect(balances.items[0].quantityOnHand).toBe(16);
  await page.screenshot({ path: "../work/pedidos-reales-facturados.png", fullPage: true });
});
