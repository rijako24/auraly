import { expect, test, type Locator, type Page } from "@playwright/test";

const user = process.env.AURALY_E2E_USERNAME ?? "admin";
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
  await page.getByRole("option").first().click();
}

test.describe("conteo y detalle de inventario", () => {
  test.beforeEach(async ({ page }) => login(page));
  test.setTimeout(120_000);

  test("preparar conteo conserva el saldo base retornado por el servidor", async ({ page }) => {
    await page.route("**/api/commerce/v1/stock-counts/start", async (route) => {
      const request = route.request().postDataJSON() as {
        documentId: string;
        productIds: string[];
      };
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          documentId: request.documentId,
          status: "Draft",
          baseInventorySequence: 25,
          lines: request.productIds.map((productId, index) => ({
            lineNumber: index + 1,
            direction: "COUNT",
            productId,
            productCode: `PRD-${index + 1}`,
            description: "Producto contado",
            quantity: 0,
            systemQuantityAtBase: 10,
            explicitUnitCost: null,
            allocationWeight: null,
          })),
        }),
      });
    });

    await page.goto("/dashboard/inventory");
    await page.getByRole("tab", { name: /Nueva operaci.n/ }).click();
    await page.getByRole("button", { name: /Conteo f.sico/ }).click();
    await selectFirst(page, page.locator("main"), "Bodega");

    const search = page.getByTestId("inventory-product-search");
    await search.click();
    const product = page.getByRole("option").first();
    await expect(product).toBeVisible({ timeout: 20_000 });
    await product.click();

    const row = page.locator("table tbody tr").filter({
      has: page.getByTestId("inventory-quantity-0"),
    });
    await page.getByTestId("inventory-quantity-0").fill("3");
    await page.getByRole("button", { name: "Preparar conteo" }).click();

    await expect(row.getByText("10", { exact: true })).toBeVisible({ timeout: 20_000 });
    await expect(page.getByTestId("inventory-quantity-0")).toHaveValue("3");
    await expect(page.getByText(/saldo base qued. congelado/i)).toBeVisible();
  });

  test("historial abre detalle con motivo y todas las lineas", async ({ page }) => {
    const documentId = "55555555-5555-5555-5555-555555555555";
    await page.route(/\/api\/commerce\/v1\/inventory\/operations\?.*/, async (route) => {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          items: [{
            documentId,
            documentType: "StockCount",
            documentNumber: "CTI-000001",
            warehouseId: "66666666-6666-6666-6666-666666666666",
            warehouseName: "Principal",
            destinationWarehouseId: null,
            destinationWarehouseName: null,
            reasonCode: "Conteo físico",
            status: "Processed",
            occurredAt: "2026-08-14T16:00:00Z",
            lineCount: 2,
            totalValueChange: 15000,
          }],
          page: 1, pageSize: 50, totalCount: 1, totalPages: 1,
        }),
      });
    });
    await page.route(`**/api/commerce/v1/inventory/operations/${documentId}`, async (route) => {
      await route.fulfill({
        contentType: "application/json",
        body: JSON.stringify({
          documentId,
          documentType: "StockCount",
          documentNumber: "CTI-000001",
          warehouseId: "66666666-6666-6666-6666-666666666666",
          warehouseName: "Principal",
          destinationWarehouseId: null,
          destinationWarehouseName: null,
          reasonCode: "PHYSICAL_COUNT",
          reasonDescription: "Conteo físico programado",
          conversionType: null,
          baseInventorySequence: 25,
          notes: "Conteo de dos líneas",
          status: "Processed",
          occurredAt: "2026-08-14T16:00:00Z",
          createdAt: "2026-08-14T16:00:00Z",
          acceptedAt: "2026-08-14T16:01:00Z",
          processedAt: "2026-08-14T16:02:00Z",
          totalValueChange: 15000,
          lines: [
            { lineNumber: 1, direction: "COUNT", productId: "77777777-7777-7777-7777-777777777777", productCode: "PRD-000001", productName: "Aceite vegetal 3000 ml", quantity: 7, systemQuantityAtBase: 10, explicitUnitCost: null, allocationWeight: null, processedUnitCost: 5000, processedValue: -15000 },
            { lineNumber: 2, direction: "COUNT", productId: "88888888-8888-8888-8888-888888888888", productCode: "PRD-000002", productName: "Aceite vegetal 1000 ml", quantity: 12, systemQuantityAtBase: 8, explicitUnitCost: null, allocationWeight: null, processedUnitCost: 5000, processedValue: 20000 },
          ],
        }),
      });
    });

    await page.goto("/dashboard/inventory");
    await page.getByRole("tab", { name: "Historial" }).click();
    await page.getByText("CTI-000001", { exact: true }).click();

    const detail = page.getByRole("dialog", { name: "CTI-000001" });
    await expect(detail).toBeVisible();
    await expect(detail.getByText("Conteo físico programado")).toBeVisible();
    await expect(detail.getByText("Aceite vegetal 3000 ml")).toBeVisible();
    await expect(detail.getByText("Aceite vegetal 1000 ml")).toBeVisible();
    await detail.getByRole("button", { name: "Cerrar" }).click();
    await expect(detail).toBeHidden();
  });
});

