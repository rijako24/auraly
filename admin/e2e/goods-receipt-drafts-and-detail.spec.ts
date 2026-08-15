import { expect, test, type Locator, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin2222";
const password = process.env.AURALY_E2E_PASSWORD ?? "Admin123!";

async function login(page: Page) {
  await page.goto("/login");
  await page.locator("#username").fill(user);
  await page.locator("#password").fill(password);
  await page.getByRole("button", { name: /Iniciar sesi.n/ }).click();
  await expect(page).toHaveURL(/\/dashboard(?:\/|$)/, { timeout: 60_000 });
}

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
async function cleanupTestDrafts(page: Page) {
  const response = await page.request.get(
    "/api/commerce/v1/goods-receipts?status=Draft&page=1&pageSize=100",
  );
  if (!response.ok()) return;
  const result = await response.json() as { items: Array<{ documentId: string }> };
  for (const item of result.items) {
    const draftResponse = await page.request.get(
      `/api/commerce/v1/goods-receipts/drafts/${item.documentId}`,
    );
    if (!draftResponse.ok()) continue;
    const draft = await draftResponse.json() as { notes?: string; concurrencyToken: string };
    if (!draft.notes?.startsWith("Prueba E2E ")) continue;
    await page.request.delete(
      `/api/commerce/v1/goods-receipts/drafts/${item.documentId}` +
      `?concurrencyToken=${encodeURIComponent(draft.concurrencyToken)}`,
    );
  }
  await page.reload();
}


test.describe("borradores y detalle de entradas de mercancia", () => {
  test.beforeEach(async ({ page }) => login(page));
  test.setTimeout(120_000);

  test("guardar cierra el modal y el borrador se puede recuperar y eliminar", async ({ page }) => {
    await page.goto("/dashboard/purchasing/goods-receipts");
    await page.getByRole("button", { name: "Nueva entrada" }).click();
    const dialog = page.getByRole("dialog", { name: /Entrada de mercanc.a/ });
    await selectFirst(page, dialog, "Proveedor");
    await selectFirst(page, dialog, "Bodega");

    const marker = `Prueba E2E ${Date.now()}`;
    const invoiceMarker = `E2E-${Date.now()}`;
    await field(dialog, "Factura del proveedor").getByRole("textbox").fill(invoiceMarker);
    await field(dialog, "Notas").getByRole("textbox").fill(marker);
    await dialog.getByRole("button", { name: "Guardar borrador" }).click();

    await expect(dialog).toBeHidden({ timeout: 20_000 });
    await expect(page.getByText("Borrador guardado y disponible para recuperar.")).toBeVisible();

    const draftRow = page.getByText(`Factura proveedor ${invoiceMarker}`)
      .locator("xpath=ancestor::tr");
    await expect(draftRow).toBeVisible({ timeout: 20_000 });
    await draftRow.click();

    const recovered = page.getByRole("dialog", { name: /Entrada de mercanc.a/ });
    await expect(recovered).toBeVisible();
    await expect(field(recovered, "Notas").getByRole("textbox")).toHaveValue(marker);
    await recovered.getByRole("button", { name: "Eliminar borrador" }).click();
    await expect(recovered).toBeHidden({ timeout: 20_000 });
    await cleanupTestDrafts(page);
  });

  test("una entrada confirmada abre su detalle inmutable", async ({ page }) => {
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
    await detail.getByRole("button", { name: "Cerrar" }).click();
    await expect(detail).toBeHidden();
  });
});

