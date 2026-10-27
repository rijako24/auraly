import { expect, test } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { realAccountingWorkspace, realPartyRequest } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";
import { proposedAccountingDate } from "../src/lib/accounting-document-date";
import type { AccountingEntry } from "../src/services/api/accounting";
import type { ConfirmExpense, ExpenseDetail, ExpenseOptions } from "../src/services/api/expenses";
import type { GoodsReceiptAcceptance, GoodsReceiptCostVoucher, GoodsReceiptDetail,
  GoodsReceiptOptions, GoodsReceiptProductPage } from "../src/services/api/goods-receipts";
import type { SupplierAcceptance } from "../src/services/api/parties";

test("agrega desde la vista un comprobante a una compra procesada sin cambiar la deuda", async ({ page }) => {
  test.setTimeout(150_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const supplierName = `Proveedor ajuste ${randomUUID().slice(0, 8)}`;
  const supplier = await scopedJson<SupplierAcceptance>("POST", "/api/commerce/v1/suppliers", {
    ...await realPartyRequest(context, supplierName), purchaseEvidencePolicy: "InternalReceiptVoucher",
    defaultPaymentDueDays: 0,
  });
  await scopedJson("PUT", `/api/commerce/v1/taxation/counterparty-profiles/${supplier.supplierId}`, {
    businessId, counterpartyId: supplier.supplierId, appliesWithholding: false,
    responsibilities: [], jurisdictionCode: "11001",
  });
  const issuedAt = `${proposedAccountingDate().slice(0, 4)}-01-01T10:00:00-05:00`;
  const dueDate = `${proposedAccountingDate().slice(0, 4)}-01-31T10:00:00-05:00`;
  const laterIssuedAt = `${proposedAccountingDate().slice(0, 4)}-03-01T10:00:00-05:00`;
  const laterDueDate = `${proposedAccountingDate().slice(0, 4)}-03-31T10:00:00-05:00`;
  const concept = (await scopedJson<ExpenseOptions>("GET",
    "/api/commerce/v1/expenses/options?includeDirectories=false")).concepts.find(item => item.isActive)!;
  expect(concept).toBeTruthy();
  const expense: ConfirmExpense = {
    expenseId: randomUUID(), businessId, supplierId: supplier.supplierId, partySiteId: null,
    conceptId: concept.conceptId, costCenterId: null, supplierDocumentNumber: null,
    issuedAt: laterIssuedAt, dueDate: laterDueDate, currencyCode: "COP", description: "Flete contabilizado",
    taxExclusiveAmount: 5000, vatAmount: 0, withholdingJurisdictionCode: null,
    evidenceUrl: null, purchaseEvidenceType: "InternalReceiptVoucher",
  };
  await scopedJson("POST", "/api/commerce/v1/expenses/confirm", expense,
    { "Idempotency-Key": expense.expenseId });
  await expect.poll(async () => (await scopedJson<ExpenseDetail>("GET",
    `/api/commerce/v1/expenses/${expense.expenseId}`)).status,
  { timeout: 45_000 }).toBe("Processed");
  const source = await scopedJson<AccountingEntry>("GET",
    `/api/commerce/v1/accounting/entries/by-document/${expense.expenseId}`);

  const productName = await createRealProduct(page);
  const options = await scopedJson<GoodsReceiptOptions>("GET", "/api/commerce/v1/goods-receipts/options");
  const warehouse = options.warehouses[0];
  expect(warehouse).toBeTruthy();
  const products = await scopedJson<GoodsReceiptProductPage>("GET",
    `/api/commerce/v1/goods-receipts/products?supplierId=${supplier.supplierId}&includeUnassociated=true&search=${encodeURIComponent(productName)}&page=1&pageSize=10`);
  const product = products.items.find(item => item.name === productName)!;
  expect(product).toBeTruthy();
  await scopedJson("POST", "/api/commerce/v1/goods-receipts/supplier-products", {
    supplierId: supplier.supplierId, productId: product.productId,
    supplierProductCode: null, isPrimary: true,
    purchasePresentationName: "Unidad", unitsPerPresentation: 1,
  });
  const receiptId = randomUUID();
  const receipt = await scopedJson<GoodsReceiptAcceptance>("POST",
    "/api/commerce/v1/goods-receipts/confirm", {
      documentId: receiptId, businessId, warehouseId: warehouse.warehouseId,
      supplierId: supplier.supplierId, partySiteId: null,
      supplierInvoiceNumber: null, supplierInvoiceDate: issuedAt, receivedAt: issuedAt,
      createsPayable: true, dueDate, currencyCode: "COP", notes: null,
      lines: [{ lineNumber: 1, productId: product.productId, description: productName,
        quantity: 5, unitCost: 10000, discountAmount: 0, taxCode: product.taxCode,
        taxRate: product.taxRate, taxTreatment: product.taxTreatment, presentationName: "Unidad",
        presentationQuantity: 5, unitsPerPresentation: 1 }],
      draftConcurrencyToken: null, withholdingConceptCode: null,
      withholdingJurisdictionCode: null, purchaseEvidenceType: "InternalReceiptVoucher",
      purchaseOrderId: null, exchangeRate: 1, exchangeRateDate: null,
      exchangeRateSource: "FunctionalCurrency", additionalCostDocuments: null,
      referencedCostDocuments: null,
    }, { "Idempotency-Key": receiptId });
  expect(receipt.documentId).toBe(receiptId);
  await expect.poll(async () => (await scopedJson<GoodsReceiptDetail>("GET",
    `/api/commerce/v1/goods-receipts/${receiptId}`)).status,
  { timeout: 45_000 }).toBe("Processed");
  const original = await scopedJson<GoodsReceiptDetail>("GET",
    `/api/commerce/v1/goods-receipts/${receiptId}`);
  expect(original.payable?.outstandingAmount).toBeGreaterThan(0);
  await page.goto(`/dashboard/purchasing/goods-receipts?receiptId=${receiptId}`);
  const detail = page.getByRole("dialog", { name: receipt.documentNumber, exact: true });
  await expect(detail.getByRole("button", { name: "Agregar comprobante", exact: true })).toBeVisible();
  await detail.getByRole("button", { name: "Agregar comprobante", exact: true }).click();
  const modal = page.getByRole("dialog", { name: "Agregar comprobante a la compra" });
  await expect(modal).toBeVisible();
  await modal.getByRole("combobox", { name: "Tercero de los comprobantes de compra" }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(supplierName);
  await page.getByRole("option").filter({ hasText: supplierName }).click();
  const sourceChoice = modal.getByRole("row").filter({ hasText: source.entryNumber });
  await sourceChoice.getByRole("button", { name: /Seleccionar documento/ }).click();
  const voucher = await scopedJson<GoodsReceiptCostVoucher>("GET",
    `/api/commerce/v1/goods-receipts/cost-vouchers/${source.entryId}`);
  const costLine = voucher.lines.find(line => line.eligibleFunctionalAmount > 0)!;
  await modal.getByRole("combobox", { name: "Cómo aplicar el comprobante" }).click();
  await page.getByRole("option", { name: "Distribuir entre los productos", exact: true }).click();
  await modal.getByRole("textbox", { name: `Importe a repartir de ${costLine.accountCode}` }).fill("3000");
  const revising = page.waitForResponse(response => response.request().method() === "POST"
    && response.url().endsWith(`/goods-receipts/${receiptId}/cost-revisions`));
  await modal.getByRole("button", { name: "Agregar comprobante", exact: true }).click();
  const revisedResponse = await revising;
  expect(revisedResponse.ok(), await revisedResponse.text()).toBe(true);
  const revision = await revisedResponse.json() as { revisionId: string; revisionNumber: number };
  await expect(detail).toContainText(`Ajuste posterior ${revision.revisionNumber}`);
  await expect.poll(async () => (await scopedJson<GoodsReceiptDetail>("GET",
    `/api/commerce/v1/goods-receipts/${receiptId}`)).accountingStatuses
      ?.find(item => item.sourceDocumentId === revision.revisionId)?.status,
  { timeout: 45_000 }).toBe("Posted");
  const after = await scopedJson<GoodsReceiptDetail>("GET",
    `/api/commerce/v1/goods-receipts/${receiptId}`);
  expect(after.payable?.outstandingAmount).toBe(original.payable?.outstandingAmount);
  expect(after.lines[0].recognizedInventoryCostAmount).toBe(
    (original.lines[0].recognizedInventoryCostAmount ?? 0) + 3000);
  expect(after.referencedCostDocuments).toEqual([expect.objectContaining({
    entryId: source.entryId, costRevisionNumber: 1, costRevisionStatus: "Processed",
  })]);
});
