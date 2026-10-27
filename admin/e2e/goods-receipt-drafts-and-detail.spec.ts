import { expect, test, type Locator, type Page } from "@playwright/test";
import { realAccountingWorkspace } from "./support/real-accounting-workspace";
import type { GoodsReceiptDraft } from "../src/services/api/goods-receipts";
import type { WarehouseMasterItem } from "../src/services/api/inventory";

function field(scope: Locator, label: string) {
  return scope.getByText(label, { exact: true }).locator("..");
}

async function selectFirst(page: Page, scope: Locator, label: string) {
  const combo = field(scope, label).getByRole("combobox");
  await combo.click();
  const option = page.getByRole("option").first();
  await expect(option).toBeVisible({ timeout: 20_000 });
  await option.click();
}
test.describe("borradores y detalle de recepciones de compra", () => {
  test.setTimeout(120_000);

  test("guardar cierra el modal y el borrador se puede recuperar y eliminar", async ({ page }) => {
    const { scopedJson } = await realAccountingWorkspace(page);
    const marker = `Prueba E2E ${Date.now()}`;
    const warehouse = await scopedJson<WarehouseMasterItem>("POST", "/api/commerce/v1/inventory/warehouse-masters", {
      name: marker, allowNegativeStockSales: false, priceFormationCostBasis: "LatestReceiptCost", useForSales: true, isActive: true,
    });
    await page.goto("/dashboard/purchasing/goods-receipts");
    await page.getByRole("button", { name: "Nueva entrada" }).click();
    const dialog = page.getByRole("dialog", { name: /Recepción de compra/ });
    await selectFirst(page, dialog, "Proveedor · sede");
    await field(dialog, "Bodega").getByRole("combobox").click();
    await page.getByRole("option").filter({ hasText: warehouse.name }).click();
    await dialog.getByPlaceholder("Observaciones de recepción").fill(marker);
    const saving = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/goods-receipts/drafts/"));
    await dialog.getByRole("button", { name: "Guardar borrador" }).click();

    const saveResponse = await saving;
    expect(saveResponse.ok(), await saveResponse.text()).toBe(true);
    const saved = await saveResponse.json() as GoodsReceiptDraft;
    await expect(dialog).toBeHidden({ timeout: 20_000 });
    const draftRow = page.getByRole("row").filter({ hasText: warehouse.name });
    await expect(draftRow).toBeVisible({ timeout: 20_000 });
    await draftRow.click();

    const recovered = page.getByRole("dialog", { name: /Recepción de compra/ });
    await expect(recovered).toBeVisible();
    await expect(recovered.getByPlaceholder("Observaciones de recepción")).toHaveValue(marker);
    const deleting = page.waitForResponse(response => response.request().method() === "DELETE" && response.url().includes(`/goods-receipts/drafts/${saved.draftId}`));
    await recovered.getByRole("button", { name: "Descartar borrador", exact: true }).click();
    const deleteResponse = await deleting;
    expect(deleteResponse.ok(), await deleteResponse.text()).toBe(true);
    await expect(recovered).toBeHidden({ timeout: 20_000 });
    await expect(draftRow).toHaveCount(0);
  });

  test("una entrada confirmada abre su detalle inmutable", async ({ page }) => {
    await realAccountingWorkspace(page);
    const documentId = "11111111-1111-1111-1111-111111111111";
    await page.route(/\/api\/commerce\/v1\/goods-receipts\?.*/, async (route) => {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          items: [{
            documentId,
            documentNumber: "EM-000001",
            status: "Processed",
            warehouseId: "22222222-2222-2222-2222-222222222222",
            warehouseName: "Principal",
            supplierId: "33333333-3333-3333-3333-333333333333",
            supplierName: "Proveedor de prueba",
            supplierInvoiceNumber: "FV-9001",
            receivedAt: "2026-08-14T15:00:00Z",
            grandTotal: 119000,
            updatedAt: "2026-08-14T15:01:00Z",
          }],
          page: 1, pageSize: 25, totalCount: 1, totalPages: 1,
        }),
      });
    });
    await page.route(`**/api/commerce/v1/goods-receipts/${documentId}`, async (route) => {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          documentId,
          documentNumber: "EM-000001",
          status: "Processed",
          warehouseId: "22222222-2222-2222-2222-222222222222",
          warehouseName: "Principal",
          supplierId: "33333333-3333-3333-3333-333333333333",
          supplierName: "Proveedor de prueba",
          supplierInvoiceNumber: "FV-9001",
          supplierInvoiceDate: "2026-08-14T00:00:00Z",
          receivedAt: "2026-08-14T15:00:00Z",
          createsPayable: true,
          dueDate: "2026-09-14T00:00:00Z",
          currencyCode: "COP",
          notes: "Mercancía recibida completa",
          netAmount: 100000,
          taxAmount: 19000,
          grandTotal: 119000,
          acceptedAt: "2026-08-14T15:00:30Z",
          processedAt: "2026-08-14T15:01:00Z",
          lines: [{
            lineNumber: 1,
            productId: "44444444-4444-4444-4444-444444444444",
            description: "Aceite vegetal 3000 ml",
            quantity: 10,
            unitCost: 10000,
            discountAmount: 0,
            taxCode: "IVA19",
            taxRate: 19,
            taxTreatment: "DeductibleInputVat",
            netAmount: 100000,
            taxAmount: 19000,
            lineTotal: 119000,
            presentationName: "Unidad",
            presentationQuantity: 10,
            unitsPerPresentation: 1,
          }],
        }),
      });
    });

    await page.goto("/dashboard/purchasing/goods-receipts");
    await page.getByText("EM-000001", { exact: true }).click();

    const detail = page.getByRole("dialog", { name: "EM-000001" });
    await expect(detail).toBeVisible();
    await expect(detail.getByText("Aceite vegetal 3000 ml")).toBeVisible();
    await expect(detail.getByText("IVA descontable")).toBeVisible();
    await expect(detail.getByText("Proveedor de prueba")).toBeVisible();
    await expect(detail.getByRole("button", { name: "Registrar egreso", exact: true })).toHaveCount(0);
    await detail.getByRole("button", { name: "Cerrar" }).click();
    await expect(detail).toBeHidden();
  });
});

