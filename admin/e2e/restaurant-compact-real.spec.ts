import { expect, test } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { realAccountingContext, realPartyRequest } from "./support/real-accounting-workspace";
import type { CustomerAcceptance, PartyWorkspaceDetail } from "../src/services/api/parties";
import type { SellerCatalogPage, SellerOrderResult } from "../src/services/api/seller-orders";
import type { CommerceOrderDetail } from "../src/services/orders/commerce-orders-client";

test.use({ serviceWorkers: "block", actionTimeout: 60_000 });

test("restaurante real conserva cliente, dos ítems e impresión directa de precuenta", async ({ page }) => {
  test.setTimeout(180_000);
  const fixturePath = process.env.AURALY_E2E_UI_COMPOSITION_FIXTURE;
  test.skip(!fixturePath, "Requiere el catálogo aislado de pruebas locales.");
  const fixture = JSON.parse(readFileSync(fixturePath!, "utf8")) as {
    roomId: string; businesses: { RestaurantBar: { businessId: string; warehouseId: string } };
    products: Record<string, { productId: string; productCode: string; name: string }>;
  };
  const businessId = fixture.businesses.RestaurantBar.businessId;
  const context = await realAccountingContext(page, businessId);
  const suffix = randomUUID().slice(0, 6);
  const code = `UI-${suffix}`;
  const customerName = `Cliente cabecera ${suffix}`;
  const customer = await context.scopedJson<CustomerAcceptance>("POST", "/api/commerce/v1/customers", await realPartyRequest(context, customerName));
  await context.scopedJson("POST", "/api/commerce/v1/restaurant/tables", {
    tableId: randomUUID(), businessId, roomId: fixture.roomId, code, name: "Prueba de cabecera",
    capacity: 2,
  });
  await page.addInitScript(() => {
    window.print = () => document.documentElement.setAttribute("data-print-requested", "1");
  });
  await page.goto("/pos?workspace=change");
  await page.getByRole("button", { name: "Comprobante de venta", exact: true }).click();
  await page.getByRole("button", { name: "Continuar a ventas", exact: true }).click();
  await page.getByRole("button", { name: new RegExp(code) }).click();
  await expect(page.getByRole("button", { name: "Volver", exact: true })).toBeVisible();
  for (const key of ["recipeYes", "side"]) {
    const product = fixture.products[key];
    await page.getByRole("textbox", { name: "Buscar producto", exact: true }).fill(product.productCode);
    await page.getByRole("button", { name: `Agregar ${product.name}`, exact: true }).click();
    await expect(page.getByRole("button", { name: `Agregar uno de ${product.name}`, exact: true })).toBeEnabled();
  }
  await page.getByRole("button", { name: "Buscar cliente: Consumidor final", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Buscar cliente", exact: true });
  await dialog.getByPlaceholder("Nombre o identificación").fill(customerName);
  await dialog.getByRole("button", { name: new RegExp(customerName) }).click();
  await expect(page.getByRole("button", { name: `Buscar cliente: ${customerName}`, exact: true })).toBeVisible();
  const prebillResponse = page.waitForResponse(response => response.request().method() === "POST" &&
    /\/restaurant\/accounts\/[^/]+\/prebill$/.test(new URL(response.url()).pathname));
  await page.getByRole("button", { name: "Imprimir", exact: true }).click();
  const response = await prebillResponse; expect(response.ok(), await response.text()).toBe(true);
  const print = page.frameLocator('iframe[aria-hidden="true"]');
  await expect(print.locator("html")).toHaveAttribute("data-print-requested", "1");
  await expect(print.locator("h1")).toHaveText(`Mesa ${code}`);
  await expect(print.locator("h2")).toHaveText("Precuenta · No es factura");
  await expect(print.locator("tbody tr")).toHaveCount(2);
  expect(page.context().pages()).toHaveLength(1);
  await page.locator('iframe[aria-hidden="true"]').evaluate(element =>
    (element as HTMLIFrameElement).contentWindow?.dispatchEvent(new Event("afterprint")));
  await expect(page.locator('iframe[aria-hidden="true"]')).toHaveCount(0);
  const lines = page.getByRole("region", { name: "Productos de la mesa", exact: true });
  const boundary = await lines.boundingBox();
  for (const card of await lines.locator("[data-sale-item]").all()) {
    const box = await card.boundingBox();
    expect(box!.y + box!.height).toBeLessThanOrEqual(boundary!.y + boundary!.height);
  }
  await page.screenshot({ path: "test-results/restaurant-compact-real.png", fullPage: true });
  const party = await context.scopedJson<PartyWorkspaceDetail>("GET", `/api/commerce/v1/parties/${customer.partyId}`);
  const site = party.sites?.find(item => item.isActive) ?? party.primarySite;
  expect(site?.isActive).toBe(true);
  const warehouseId = fixture.businesses.RestaurantBar.warehouseId;
  const catalog = await context.scopedJson<SellerCatalogPage>("POST", "/api/commerce/v1/seller-orders/catalog", {
    businessId, warehouseId, customerId: customer.customerId, search: fixture.products.recipeYes.productCode,
    skip: 0, take: 10,
  });
  const product = catalog.items.find(item => item.productId === fixture.products.recipeYes.productId);
  expect(product).toBeTruthy();
  const balances = () => context.scopedJson<{ items: Array<{ quantityOnHand: number }> }>("GET",
    `/api/commerce/v1/inventory/balances?warehouseId=${warehouseId}&productId=${product!.productId}&page=1&pageSize=20`);
  const before = await balances(); expect(before.items).toHaveLength(1);
  const order = await context.scopedJson<SellerOrderResult>("POST", "/api/commerce/v1/seller-orders", {
    businessId, warehouseId, customerId: customer.customerId, partySiteId: site!.partySiteId,
    routeId: null, routeStopId: null, capturedOffline: false, notes: "Pedido de restaurante sin mesa",
    idempotencyKey: randomUUID(), lines: [{ productId: product!.productId, description: product!.name,
      quantity: 2, unitPrice: product!.unitPrice, discountAmount: 0, priceSource: product!.priceSource,
      documentUnitCost: product!.documentUnitCost }],
  });
  expect(order.status).toBe("Confirmed");
  await page.getByRole("button", { name: "Ver pedidos", exact: true }).click();
  const orders = page.getByRole("dialog", { name: "Pedidos", exact: true });
  await expect(orders.getByRole("tab", { name: /En espera/ })).toHaveCount(0);
  await orders.getByPlaceholder("Número de pedido", { exact: true }).fill(order.orderNumber);
  await expect(orders.getByRole("button", { name: /^Recuperar/ })).toBeDisabled();
  await expect(orders.getByRole("status")).toHaveText("La captura tiene productos. Termínala o vacíala antes de recuperar.");
  await page.keyboard.press("Escape");
  for (const key of ["side", "recipeYes"]) {
    await page.getByRole("button", { name: `Quitar uno de ${fixture.products[key].name}`, exact: true }).click();
  }
  await expect(lines.locator("[data-sale-item]")).toHaveCount(0);
  await page.getByRole("button", { name: "Ver pedidos", exact: true }).click();
  await orders.getByPlaceholder("Número de pedido", { exact: true }).fill(order.orderNumber);
  await expect(orders.getByRole("button", { name: /^Recuperar/ })).toBeEnabled();
  await orders.getByRole("button", { name: /^Recuperar/ }).click();
  await expect(page).toHaveURL(/\/pos\?workspace=orders$/);
  const quantity = page.getByRole("spinbutton", { name: `Cantidad de ${product!.name}`, exact: true });
  await expect(quantity).toHaveValue("2");
  await expect(page.getByText(customerName, { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: /^Pausar venta/ })).toHaveCount(0);
  await page.reload(); await expect(quantity).toHaveValue("2");
  await expect(page.getByText(customerName, { exact: true })).toBeVisible();
  await page.getByRole("button", { name: /^Cobrar/ }).click();
  const payment = page.getByRole("dialog", { name: "Finalizar venta", exact: true });
  await payment.getByRole("textbox", { name: "Valor recibido en Efectivo", exact: true }).fill(String(order.total));
  const completed = page.waitForResponse(response => response.request().method() === "POST" &&
    /\/pos\/drafts\/[^/]+\/complete$/.test(new URL(response.url()).pathname));
  await payment.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const completion = await completed; expect(completion.ok(), await completion.text()).toBe(true);
  const result = await completion.json() as { receipt: { documentId: string } };
  expect(result.receipt.documentId).toBeTruthy();
  const invoiced = await context.scopedJson<CommerceOrderDetail>("GET", `/api/commerce/v1/orders/${order.orderId}`);
  expect(invoiced.invoiceDocumentId).toBe(result.receipt.documentId);
  await expect.poll(async () => (await balances()).items[0].quantityOnHand,
    { timeout: 60_000, intervals: [250, 500, 1000, 2000] }).toBe(before.items[0].quantityOnHand - 2);
  await expect(page.locator("#pos-scanner")).toBeDisabled();
  writeFileSync(test.info().outputPath("restaurant-order-proof.json"), JSON.stringify({
    businessId, orderId: order.orderId, documentId: result.receipt.documentId,
  }));
  await page.screenshot({ path: "test-results/restaurant-order-invoiced-real.png", fullPage: true });
  await page.getByRole("button", { name: "Volver a restaurante", exact: true }).click();
  await expect(page.getByRole("heading", { name: "¿A qué mesa atendemos?", exact: true })).toBeVisible();
});
