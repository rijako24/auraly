import { expect, test, type Locator, type Page } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { realAccountingAuxiliary, realAccountingWorkspace, realPartyRequest } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";
import type { AccountingAccount, AccountingEntry, AccountingOpenPositionPage, SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";
import type { GoodsReceiptAcceptance, GoodsReceiptDetail, GoodsReceiptDraft, GoodsReceiptCostVoucher } from "../src/services/api/goods-receipts";
import type { ConfirmExpense, ExpenseDetail, ExpenseOptions } from "../src/services/api/expenses";
import type { SupplierAcceptance } from "../src/services/api/parties";
import { proposedAccountingDate } from "../src/lib/accounting-document-date";
import type { PayablePage } from "../src/services/api/payables";
import type { WarehouseMasterItem } from "../src/services/api/inventory";
import type { PurchaseReturnAcceptance, ReturnableReceipt } from "../src/services/api/purchase-returns";

function field(scope: Locator, label: RegExp | string) {
  return scope.getByText(label, { exact: typeof label === "string" }).locator("..");
}


async function selectFirst(page: Page, dialog: Locator, label: RegExp | string) {
  const combo = field(dialog, label).getByRole("combobox");
  await expect(combo).toBeEnabled({ timeout: 20_000 });
  await combo.click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
}

async function chooseAnotherDate(page: Page, scope: Locator, label: string) {
  const trigger = field(scope, label).getByRole("button").first();
  const before = await trigger.textContent();
  await trigger.click();
  const days = page.locator('[data-radix-popper-content-wrapper]:visible button[aria-label*=" de "]:not(:disabled)');
  await expect(days.first()).toBeVisible();
  const anotherDate = await days.evaluateAll((buttons, previous) =>
    buttons.map(button => button.getAttribute("aria-label")).find(label => label && label !== previous?.trim()), before);
  expect(anotherDate, "El calendario debe ofrecer una fecha habilitada distinta de la actual.").toBeTruthy();
  await page.getByRole("button", { name: anotherDate!, exact: true }).click();
  await expect(trigger).not.toHaveText(before ?? "");
}

for (const unallocatedAsset of [false, true]) {
test(`recibe compra por UI, conserva foco y descuento y retoma el cargo existente sin duplicar su deuda (${unallocatedAsset ? "activo por distribuir" : "gasto"})`, async ({ page }) => {
  test.setTimeout(180_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const supplierName = `Proveedor de cargos ${randomUUID().slice(0, 8)}`;
  const supplier = await scopedJson<SupplierAcceptance>("POST", "/api/commerce/v1/suppliers", {
    ...await realPartyRequest(context, supplierName), purchaseEvidencePolicy: "InternalReceiptVoucher", defaultPaymentDueDays: 0,
  });
  await scopedJson("PUT", `/api/commerce/v1/taxation/counterparty-profiles/${supplier.supplierId}`, {
    businessId, counterpartyId: supplier.supplierId, appliesWithholding: false, responsibilities: [], jurisdictionCode: "11001",
  });
  const issuedAt = `${proposedAccountingDate().slice(0, 4)}-01-01T10:00:00-05:00`;
  const dueDate = `${proposedAccountingDate().slice(0, 4)}-01-31T10:00:00-05:00`;
  let sourceId: string, sourceAccountCode: string, sourceAccountName: string;
  if (unallocatedAsset) {
    const parentId = randomUUID();
    const parentCode = `1398${BigInt(`0x${parentId.replaceAll("-", "").slice(0, 12)}`)}`;
    const parentName = `Agrupación de costos ${parentId.slice(0, 8)}`;
    await scopedJson("POST", "/api/commerce/v1/accounting/accounts", {
      accountId: parentId, tenantId: process.env.AURALY_E2E_TENANT_ID, code: parentCode,
      name: parentName, accountType: "Asset", allowsPosting: false, requiresParty: false,
    });
    await page.goto("/dashboard/accounting");
    await page.getByRole("button", { name: "Plan de cuentas", exact: true }).click();
    await page.getByRole("button", { name: "Nueva cuenta", exact: true }).click();
    const editor = page.getByRole("dialog", { name: "Nueva cuenta PUC", exact: true });
    await editor.getByLabel("Código PUC", { exact: true }).fill(`${parentCode}01`);
    await editor.getByLabel("Nombre", { exact: true }).fill(`Flete por distribuir ${parentId.slice(0, 8)}`);
    await editor.getByLabel("Tipo de cuenta", { exact: true }).click();
    await page.getByRole("option", { name: "Activo", exact: true }).click();
    await editor.getByRole("combobox", { name: "Cuenta padre", exact: true }).click();
    await page.getByPlaceholder("Buscar agrupación por código o nombre…", { exact: true }).fill(parentCode);
    await page.getByRole("option", { name: `${parentCode} · ${parentName}`, exact: true }).click();
    await editor.getByLabel("Función financiera", { exact: true }).click();
    await page.getByRole("option", { name: "Costos de compra por distribuir", exact: true }).click();
    await editor.getByLabel("Naturaleza del saldo", { exact: true }).click();
    await page.getByRole("option", { name: "Débito", exact: true }).click();
    await expect(editor.getByLabel("Cuenta bancaria vinculada", { exact: true })).toHaveCount(0);
    await expect(editor.getByLabel("Medio de pago del auxiliar", { exact: true })).toHaveCount(0);
    const creating = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/accounting/accounts"));
    await editor.getByRole("button", { name: "Crear cuenta", exact: true }).click();
    const createdResponse = await creating;
    expect(createdResponse.ok(), await createdResponse.text()).toBe(true);
    const asset = await createdResponse.json() as AccountingAccount;
    expect(asset.semantics).toMatchObject({ financialFunction: "UnallocatedPurchaseCost", balanceSide: "Debit", bankAccountId: null, paymentMethodOptionId: null });
    sourceAccountCode = asset.code; sourceAccountName = asset.name;
    const control = await realAccountingAuxiliary(context, "2298", "Liability", "Payable", "Credit", null, true);
    sourceId = randomUUID();
    const capture: SaveVoucherDraft = {
      documentId: sourceId, documentType: "ManualAccountingVoucher", payloadVersion: 4, occurredAt: issuedAt,
      voucherTypeCode: "Accounting", mainPartyId: supplier.partyId, conceptCode: "MANUAL_VOUCHER",
      description: "Flete todavía no incorporado al inventario", reference: `FLETE-${sourceId.slice(0, 8)}`, adjustment: null, rowVersion: null,
      lines: [
        { accountId: asset.accountId, partyId: supplier.partyId, costCenterId: null, description: "Flete por distribuir", debit: 5000, credit: 0, lineId: randomUUID() },
        { accountId: control.accountId, partyId: supplier.partyId, costCenterId: null, description: "Obligación del flete", debit: 0, credit: 5000, lineId: randomUUID(), dueDate },
      ],
    };
    const draft = await scopedJson<VoucherDraft>("PUT", `/api/commerce/v1/accounting/manual/drafts/${sourceId}`, capture);
    const sent = await scopedJson<VoucherDraft>("POST", `/api/commerce/v1/accounting/manual/drafts/${sourceId}/send`, { rowVersion: draft.rowVersion });
    expect(sent.status, sent.row.errorMessage ?? undefined).toBe("Posted");
  } else {
    const expenseOptions = await scopedJson<ExpenseOptions>("GET", "/api/commerce/v1/expenses/options?includeDirectories=false");
    const concept = expenseOptions.concepts.find(item => item.isActive);
    expect(concept, "El gasto asistido necesita un concepto con su cuenta automática.").toBeTruthy();
    const expense: ConfirmExpense = {
      expenseId: randomUUID(), businessId, supplierId: supplier.supplierId, partySiteId: null, conceptId: concept!.conceptId,
      costCenterId: null, supplierDocumentNumber: null, issuedAt, dueDate, currencyCode: "COP", description: "Flete ya reconocido",
      taxExclusiveAmount: 5000, vatAmount: 0, withholdingJurisdictionCode: null, evidenceUrl: null, purchaseEvidenceType: "InternalReceiptVoucher",
    };
    await scopedJson("POST", "/api/commerce/v1/expenses/confirm", expense, { "Idempotency-Key": expense.expenseId });
    await expect.poll(async () => (await scopedJson<ExpenseDetail>("GET", `/api/commerce/v1/expenses/${expense.expenseId}`)).status,
      { timeout: 45_000 }).toBe("Processed");
    sourceId = expense.expenseId; sourceAccountCode = concept!.expenseAccountCode; sourceAccountName = concept!.expenseAccountName;
  }
  async function originalDebt() {
    const page = await scopedJson<AccountingOpenPositionPage>("GET", `/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&partyId=${supplier.partyId}&page=1&pageSize=10`);
    const position = page.items.find(item => item.sourceDocumentId === sourceId);
    expect(position).toBeTruthy();
    return position!;
  }
  const sourceEntry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${sourceId}`);
  let budgetReads = 0;
  page.on("request", request => {
    if (request.method() === "GET" && request.url().endsWith(`/goods-receipts/cost-vouchers/${sourceEntry.entryId}`)) budgetReads += 1;
  });
  const warehouses = await scopedJson<WarehouseMasterItem[]>("GET", "/api/commerce/v1/inventory/warehouse-masters");
  if (!warehouses.some(item => item.isActive && item.useForGoodsReceipts)) {
    await scopedJson("POST", "/api/commerce/v1/inventory/warehouse-masters", {
      name: "Bodega de regresión local", allowNegativeStockSales: false,
      priceFormationCostBasis: "LatestReceiptCost", useForSales: true, isActive: true,
    });
  }
  const productName = await createRealProduct(page);
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  await expect(page.getByRole("heading", { name: "Recepción de compra" })).toBeVisible();
  const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
  await field(dialog, "Proveedor · sede").getByRole("combobox").click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(supplierName);
  await page.getByRole("option").filter({ hasText: supplierName }).click();
  await selectFirst(page, dialog, "Bodega");
  const evidence = dialog.getByRole("combobox", { name: "Tipo de soporte" });
  await evidence.click();
  await page.getByRole("option", { name: "Comprobante interno" }).click();
  await expect(field(dialog, "Fecha de emisión").locator("button")).toBeEnabled();
  await chooseAnotherDate(page, dialog, "Fecha de emisión");
  await expect(field(dialog, "Fecha de vencimiento").locator("button")).toBeEnabled();
  await chooseAnotherDate(page, dialog, "Fecha de vencimiento");

  const search = dialog.getByPlaceholder(/Escanea o busca por c.digo/);
  await dialog.getByRole("button", { name: /Buscar en todo el cat.logo/ }).click();
  await search.fill(productName);
  await expect(dialog.getByRole("option", { name: new RegExp(productName) })).toBeVisible({ timeout: 20_000 });
  await search.press("Enter");

  const quantity = dialog.getByLabel(/Cantidad en/).first();
  const associate = dialog.getByRole("button", { name: "Asociar y agregar", exact: true });
  await expect(quantity.or(associate)).toBeVisible({ timeout: 20_000 });
  if (await associate.isVisible()) {
    await associate.click();
    await expect(associate).toBeHidden({ timeout: 20_000 });
  }
  await expect(quantity).toBeFocused({ timeout: 20_000 });
  await dialog.getByLabel(`Costo unitario de ${productName}`).fill("10000");
  const commercialDiscount = dialog.getByLabel(`Descuento comercial (%) de ${productName}`);
  const discountColumn = await dialog.getByLabel(`Descuento de ${productName}`, { exact: true }).evaluate(input => input.closest("td")?.cellIndex);
  expect(await commercialDiscount.evaluate(input => input.closest("td")?.cellIndex)).toBe(discountColumn);
  expect(await dialog.getByLabel(`Costo unitario de ${productName}`).evaluate(input => input.closest("td")?.cellIndex)).not.toBe(discountColumn);
  await commercialDiscount.fill("10");
  await quantity.fill("10");
  await quantity.press("Enter");
  await expect(search).toBeFocused();
  await expect(dialog.getByText(/10 .* = 10/).first()).toBeVisible();
  const notes = dialog.getByPlaceholder("Observaciones de recepción");
  await expect(notes).toHaveCSS("resize", "none");
  await dialog.getByText("Descuento financiero acordado con el proveedor", { exact: true }).click();
  await dialog.getByLabel("Descuento financiero (%)", { exact: true }).fill("5");
  await expect(dialog.getByText(/Descuento financiero pactado: .*no reduce la obligación/)).toBeVisible();

  expect(budgetReads).toBe(0);
  await expect(dialog.getByRole("button", { name: "Confirmar entrada", exact: true })).toBeEnabled();
  const costsToggle = dialog.getByRole("button", { name: /Facturas y otros costos/ });
  await expect(costsToggle).toHaveAttribute("aria-expanded", "false");
  await costsToggle.click();
  await expect(costsToggle).toHaveAttribute("aria-expanded", "true");
  await expect(dialog.getByRole("button", { name: "Agregar factura", exact: true })).toBeVisible();
  await dialog.getByRole("button", { name: "Agregar comprobante", exact: true }).click();
  const costDialog = page.getByRole("dialog", { name: "Agregar comprobante a la compra" });
  await expect(costDialog.getByRole("combobox", { name: "Tercero de los comprobantes de compra" })).toContainText(supplierName);
  const sourceChoice = costDialog.getByRole("row").filter({ hasText: sourceAccountCode });
  await expect(sourceChoice).toContainText(sourceAccountName);
  await sourceChoice.getByRole("button", { name: /Seleccionar documento/ }).click();
  await expect(costDialog.getByText(`${sourceAccountCode} · ${sourceAccountName}`, { exact: true }).last()).toBeVisible();
  await costDialog.getByRole("combobox", { name: "Cómo aplicar el comprobante" }).click();
  await page.getByRole("option", { name: "Distribuir entre los productos", exact: true }).click();
  await costDialog.getByRole("textbox", { name: `Importe a repartir de ${sourceAccountCode}`, exact: true }).fill("3000");
  await costDialog.screenshot({ path: `../work/recepcion-comprobante-costos-${unallocatedAsset ? "activo" : "gasto"}-real.png` });
  await costDialog.getByRole("button", { name: "Agregar comprobante", exact: true }).click();
  await expect(costDialog).toBeHidden();
  const saving = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/goods-receipts/drafts/"));
  await dialog.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  const saveResponse = await saving;
  expect(saveResponse.ok(), await saveResponse.text()).toBe(true);
  const saved = await saveResponse.json() as GoodsReceiptDraft;
  expect(saved.referencedCostDocuments).toEqual([expect.objectContaining({ entryId: sourceEntry.entryId, mode: "Distribute",
    lines: [expect.objectContaining({ functionalAmount: 3000 })] })]);
  expect(saved.financialDiscount).toMatchObject({ percent: 5, anticipateInCost: false });
  await expect(dialog).toBeHidden();
  await page.getByRole("row").filter({ hasText: supplierName }).click();
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel("Descuento financiero (%)", { exact: true })).toHaveValue(/5/);
  await dialog.getByText("Descuento financiero acordado con el proveedor", { exact: true }).click();
  await costsToggle.click();
  await expect(dialog.getByText(sourceEntry.entryNumber, { exact: true })).toBeVisible();
  expect(budgetReads).toBe(1);

  const confirm = dialog.getByRole("button", { name: "Confirmar entrada" });
  await dialog.screenshot({ path: "../work/recepcion-real-descuento.png" });
  const confirming = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/goods-receipts/confirm"));
  await confirm.focus();
  await page.keyboard.press("Enter");
  const confirmed = await confirming;
  expect(confirmed.ok(), await confirmed.text()).toBe(true);
  const accepted = await confirmed.json() as GoodsReceiptAcceptance;
  expect(confirmed.request().postDataJSON().lines[0].commercialDiscountPercent).toBe(10);
  const toast = page.getByText(/fue confirmada y enviada al motor/).last();
  await expect(toast).toBeVisible({ timeout: 20_000 });
  const documentNumber = accepted.documentNumber;
  await expect(toast).toContainText(documentNumber);
  await expect(dialog).toBeHidden({ timeout: 20_000 });
  const row = page.getByRole("row").filter({ hasText: documentNumber });
  await expect(row).toContainText("Procesada", { timeout: 45_000 });
  const detail = await scopedJson<GoodsReceiptDetail>("GET", `/api/commerce/v1/goods-receipts/${accepted.documentId}`);
  expect(detail.lines[0]).toMatchObject({ quantity: 10, unitCost: 10000, commercialDiscountPercent: 10, discountAmount: 10000, netAmount: 90000 });
  expect(detail.createsPayable).toBe(true);
  expect(detail.lines[0].allocatedLandedCostAmount).toBe(3000);
  expect(detail.referencedCostDocuments).toEqual([expect.objectContaining({ entryId: sourceEntry.entryId, mode: "Distribute" })]);
  expect(await originalDebt()).toMatchObject({ originalAmount: 5000, outstandingAmount: 5000 });
  expect(budgetReads).toBe(1);
  const available = await scopedJson<GoodsReceiptCostVoucher>("GET", `/api/commerce/v1/goods-receipts/cost-vouchers/${sourceEntry.entryId}`);
  expect(available.lines.reduce((sum, line) => sum + line.assignedFunctionalAmount, 0)).toBe(3000);
  const entry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${accepted.documentId}`);
  expect(entry.debitTotal).toBeGreaterThan(0);
  expect(entry.debitTotal).toBe(entry.creditTotal);
  const payables = await scopedJson<PayablePage>("GET", `/api/commerce/v1/payables?page=1&pageSize=25&search=${encodeURIComponent(accepted.documentNumber)}`);
  expect(payables.items).toHaveLength(1);
  expect(payables.items[0]).toMatchObject({ documentNumber: accepted.documentNumber, status: "Open", paidAmount: 0 });
  expect(payables.items[0].originalAmount).toBe(detail.withholding?.netAmount ?? detail.functionalGrandTotal);
  expect(payables.items[0].outstandingAmount).toBe(payables.items[0].originalAmount);

  // The same received goods and original debt also traverse the unchanged
  // purchase-return screen; no second fixture or alternate financial writer.
  await page.goto("/dashboard/purchasing/purchase-returns");
  await page.getByPlaceholder("Entrada, factura, proveedor, bodega o producto").fill(documentNumber);
  const received = page.getByRole("row").filter({ hasText: documentNumber });
  await expect(received).toBeVisible();
  await received.click();
  const returning = page.getByRole("dialog", { name: "Nueva devolución", exact: true });
  await expect(returning).toContainText(productName);
  await returning.getByRole("spinbutton").fill("4");
  await returning.getByRole("combobox").click();
  await page.getByRole("option").first().click();
  const submissions: unknown[] = [];
  let acceptedReturn: PurchaseReturnAcceptance | undefined;
  await page.route("**/api/commerce/v1/purchase-returns/confirm", async route => {
    submissions.push(route.request().postDataJSON());
    if (submissions.length === 1) {
      const upstream = await route.fetch();
      expect(upstream.ok(), await upstream.text()).toBe(true);
      acceptedReturn = await upstream.json() as PurchaseReturnAcceptance;
      await route.fulfill({ status: 503, contentType: "application/problem+json",
        body: JSON.stringify({ detail: "La respuesta de confirmación es incierta. Reintenta el mismo comprobante." }) });
    } else await route.continue();
  });
  const uncertain = page.waitForResponse(response => response.request().method() === "POST"
    && response.url().endsWith("/purchase-returns/confirm"));
  await returning.getByRole("button", { name: "Confirmar devolución", exact: true }).click();
  expect((await uncertain).status()).toBe(503);
  await expect(returning).toBeVisible();
  await expect(page.getByText("La respuesta de confirmación es incierta. Reintenta el mismo comprobante.", { exact: true })).toBeVisible();
  const returnResponse = page.waitForResponse(response => response.request().method() === "POST"
    && response.url().endsWith("/purchase-returns/confirm"));
  await returning.getByRole("button", { name: "Confirmar devolución", exact: true }).click();
  const returnedResponse = await returnResponse;
  expect(returnedResponse.ok(), await returnedResponse.text()).toBe(true);
  const returned = await returnedResponse.json() as PurchaseReturnAcceptance;
  expect(submissions).toHaveLength(2);
  expect(submissions[1]).toEqual(submissions[0]);
  expect(returned.returnId).toBe(acceptedReturn?.returnId);
  expect(returned.idempotentReplay).toBe(true);
  await expect(returning).toBeHidden();
  await expect(page.getByText(`Devolución ${returned.documentNumber} confirmada y enviada al motor.`, { exact: true })).toBeVisible();
  const returnable = await scopedJson<ReturnableReceipt>("GET", `/api/commerce/v1/purchase-returns/receipts/${accepted.documentId}`);
  expect(returnable.lines).toEqual([expect.objectContaining({ receivedQuantity: 10, returnedQuantity: 4, availableQuantity: 6 })]);
  const returnEntry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${returned.returnId}`);
  expect(returnEntry.debitTotal).toBe(returnEntry.creditTotal);
  expect(returnEntry.debitTotal).toBeGreaterThan(0);
  const remaining = await scopedJson<PayablePage>("GET", `/api/commerce/v1/payables?page=1&pageSize=25&search=${encodeURIComponent(documentNumber)}`);
  expect(remaining.items[0]).toMatchObject({ originalAmount: 90000, outstandingAmount: 54000 });
  expect(await originalDebt()).toMatchObject({ originalAmount: 5000, outstandingAmount: 5000 });
  await page.screenshot({ path: "../work/devolucion-compra-real.png", fullPage: true });

  let receiptReads = 0;
  const detailPath = `/api/commerce/v1/goods-receipts/${accepted.documentId}`;
  page.on("request", request => {
    if (request.method() === "GET" && new URL(request.url()).pathname === detailPath) receiptReads++;
  });
  await page.goto(`/dashboard/purchasing/goods-receipts?receiptId=${accepted.documentId}`);
  const receiptDialog = page.getByRole("dialog", { name: documentNumber, exact: true });
  await expect(receiptDialog).toContainText("54.000");
  expect(receiptReads).toBe(1);
  await receiptDialog.getByRole("button", { name: "Registrar egreso", exact: true }).click();
  await expect(receiptDialog).toBeHidden();
  await page.getByLabel(`Abono para ${documentNumber}`, { exact: true }).fill("1000");
  await page.getByRole("button", { name: "Ir a pagar", exact: true }).click();
  const checkout = page.getByRole("dialog", { name: "Pago a proveedores", exact: true });
  const paying = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/payable-payments/confirm"));
  await checkout.getByRole("button", { name: "Sin imprimir", exact: true }).click();
  const paymentResponse = await paying;
  expect(paymentResponse.ok(), await paymentResponse.text()).toBe(true);
  const payment = await paymentResponse.json() as { paymentId: string; documentNumber: string; status: string };
  expect(payment.status).toBe("Accepted");
  await expect(page.getByText(`${payment.documentNumber} recibido y pendiente de aplicar. El abono aún no está confirmado.`, { exact: true })).toBeVisible();
  expect(receiptReads).toBe(1);
  const paymentKey = paymentResponse.request().headers()["idempotency-key"];
  expect(paymentKey).toBeTruthy();
  const replay = await scopedJson<{ paymentId: string; idempotentReplay: boolean }>("POST", "/api/commerce/v1/payable-payments/confirm",
    paymentResponse.request().postDataJSON(), { "Idempotency-Key": paymentKey });
  expect(replay).toMatchObject({ paymentId: payment.paymentId, idempotentReplay: true });
  await page.getByRole("row").filter({ hasText: documentNumber }).click();
  await expect(receiptDialog).toContainText("53.000");
  expect(receiptReads).toBe(2);
  const paymentEntry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${payment.paymentId}`);
  expect(paymentEntry.debitTotal).toBe(1000);
  expect(paymentEntry.creditTotal).toBe(1000);
  expect(paymentEntry.lines.some(line => line.partyId === supplier.partyId)).toBe(true);
  await receiptDialog.screenshot({ path: "../work/recepcion-compra-egreso-real.png" });
});
}
