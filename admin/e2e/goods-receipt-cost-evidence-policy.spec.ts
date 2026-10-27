import { expect, test } from "@playwright/test";
import { realAccountingWorkspace } from "./support/real-accounting-workspace";

test("el soporte de cada factura adicional sigue la configuración de su proveedor", async ({ page }) => {
  await realAccountingWorkspace(page);

  const supplier = {
    partyId: "11111111-1111-1111-1111-111111111111",
    roleId: "22222222-2222-2222-2222-222222222222",
    partySiteId: "33333333-3333-3333-3333-333333333333",
    displayName: "Transportador de prueba", identification: "900123456",
    siteName: "Principal", isPrimary: true,
    supplierPurchaseEvidencePolicy: "InternalReceiptVoucher",
    supplierDefaultPaymentDueDays: 30,
  };
  await page.route("**/api/commerce/v1/portfolio/parties/site-options?*", async (route) => {
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({
      items: [supplier], page: 1, pageSize: 25, totalCount: 1, totalPages: 1,
    }) });
  });
  await page.goto("/dashboard/purchasing/goods-receipts");
  await page.getByRole("button", { name: "Nueva entrada" }).click();
  await page.getByRole("button", { name: /Facturas y otros costos/ }).click();
  await page.getByRole("button", { name: "Agregar factura", exact: true }).click();

  const document = page.getByRole("dialog", { name: "Agregar documento adicional" });
  const supplierField = document.getByText("Proveedor · sede", { exact: true }).locator("..");
  await supplierField.getByRole("combobox").click();
  await page.getByRole("option", { name: /Transportador de prueba/ }).click();

  const evidenceField = document.getByText("Soporte", { exact: true }).locator("..");
  await expect(evidenceField.getByRole("combobox")).toContainText("Comprobante interno");
  await evidenceField.getByRole("combobox").click();
  await expect(page.getByRole("option", { name: /Factura electrónica del proveedor/ })).toHaveCount(0);
  await expect(page.getByRole("option", { name: /Comprobante interno/ })).toBeVisible();
});
