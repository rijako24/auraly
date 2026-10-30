import { expect, test } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { realAccountingWorkspace, realPartyRequest } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";
import { proposedAccountingDate } from "../src/lib/accounting-document-date";
import type { GoodsReceiptAcceptance, GoodsReceiptDetail, GoodsReceiptOptions,
  GoodsReceiptProductPage } from "../src/services/api/goods-receipts";
import type { SupplierAcceptance } from "../src/services/api/parties";

test("una recepción confirmada muestra sus costos sin permitir agregar comprobantes", async ({ page }) => {
  test.setTimeout(150_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const supplierName = `Proveedor recepción ${randomUUID().slice(0, 8)}`;
  const supplier = await scopedJson<SupplierAcceptance>("POST", "/api/commerce/v1/suppliers", {
    ...await realPartyRequest(context, supplierName), purchaseEvidencePolicy: "InternalReceiptVoucher",
    defaultPaymentDueDays: 0,
  });
  await scopedJson("PUT", `/api/commerce/v1/taxation/counterparty-profiles/${supplier.supplierId}`, {
    businessId, counterpartyId: supplier.supplierId, appliesWithholding: false,
    responsibilities: [], jurisdictionCode: "11001",
  });
  const productName = await createRealProduct(page);
  const options = await scopedJson<GoodsReceiptOptions>("GET", "/api/commerce/v1/goods-receipts/options");
  const warehouse = options.warehouses[0];
  expect(warehouse).toBeTruthy();
  const products = await scopedJson<GoodsReceiptProductPage>("GET",
    `/api/commerce/v1/goods-receipts/products?supplierId=${supplier.supplierId}&includeUnassociated=true&search=${encodeURIComponent(productName)}&page=1&pageSize=10`);
  const product = products.items.find(item => item.name === productName);
  expect(product).toBeTruthy();
  await scopedJson("POST", "/api/commerce/v1/goods-receipts/supplier-products", {
    supplierId: supplier.supplierId, productId: product!.productId,
    supplierProductCode: null, isPrimary: true,
    purchasePresentationName: "Unidad", unitsPerPresentation: 1,
  });
  const receiptId = randomUUID();
  const receivedAt = `${proposedAccountingDate().slice(0, 4)}-01-01T10:00:00-05:00`;
  const dueDate = `${proposedAccountingDate().slice(0, 4)}-01-31T10:00:00-05:00`;
  const receipt = await scopedJson<GoodsReceiptAcceptance>("POST",
    "/api/commerce/v1/goods-receipts/confirm", {
      documentId: receiptId, businessId, warehouseId: warehouse.warehouseId,
      supplierId: supplier.supplierId, partySiteId: null,
      supplierInvoiceNumber: null, supplierInvoiceDate: receivedAt, receivedAt,
      createsPayable: true, dueDate, currencyCode: "COP", notes: null,
      lines: [{ lineNumber: 1, productId: product!.productId, description: productName,
        quantity: 5, unitCost: 10000, discountAmount: 0, taxCode: product!.taxCode,
        taxRate: product!.taxRate, taxTreatment: product!.taxTreatment, presentationName: "Unidad",
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

  await page.goto(`/dashboard/purchasing/goods-receipts?receiptId=${receiptId}`);
  const detail = page.getByRole("dialog", { name: receipt.documentNumber, exact: true });
  await expect(detail).toBeVisible();
  await expect(detail).toContainText(productName);
  await expect(detail.getByRole("button", { name: "Agregar comprobante", exact: true })).toHaveCount(0);
  await expect(detail.getByRole("button", { name: "Agregar factura", exact: true })).toHaveCount(0);
});
