import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";
import { paymentCatalogFixture } from "./support/payment-catalog";

test("recaudo asistido de dos facturas conserva una forma al cambiar de medio", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario(), customerId = scenario.draft.mainPartyId!;
  scenario.user.permissions.push("receivables.read", "receivables.payments.create");
  const invoices = [scenario.positionId, "99999999-9999-9999-9999-999999999998"].map((id, index) => ({
    receivableId: id, customerId, customerName: "Cliente de atribución", partySiteId: null, partySiteName: null,
    documentNumber: `FV-${index + 1}`, currencyCode: "COP", originalAmount: 100, outstandingAmount: 100,
    dueDate: "2026-10-01T17:00:00Z", status: "Open", isOverdue: false, createdAt: "2026-09-01T17:00:00Z",
  }));
  let configurations = 0, commands = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/receivables/customers")) {
      await route.fulfill({ json: { items: [{ customerId, customerName: "Cliente de atribución", identification: "1001",
        invoiceCount: 2, originalAmount: 200, paidAmount: 0, outstandingAmount: 200, overdueAmount: 0 }],
        page: 1, pageSize: 20, totalCount: 1, totalPages: 1, totalOutstanding: 200, totalOverdue: 0, totalInvoiceCount: 2 } }); return;
    }
    if (path.endsWith("/receivables")) {
      await route.fulfill({ json: { items: invoices, page: 1, pageSize: 20, totalCount: 2, totalPages: 1,
        totalOutstanding: 200, totalOverdue: 0, referenceDate: "2026-11-16" } }); return;
    }
    if (path.endsWith("/receivable-payments")) {
      await route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 } }); return;
    }
    if (path.endsWith("/reference-options/payment-method")) {
      await route.fulfill({ json: [{ id: "cash", code: "Cash", label: "Efectivo", sortOrder: 1 },
        { id: "debit", code: "DebitCard", label: "Tarjeta débito", sortOrder: 2 }] }); return;
    }
    if (path.endsWith("/reference-options/card-franchise")) { await route.fulfill({ json: [{ id: "visa", code: "Visa", label: "Visa" }] }); return; }
    if (path.endsWith("/accounting/settlement-configuration")) {
      configurations++; await route.fulfill({ json: { isAccountingEnabled: false, bankAccounts: [], paymentMethods: paymentCatalogFixture() } }); return;
    }
    if (path.endsWith("/receivable-payments/confirm")) commands++;
    await route.fallback();
  });
  await page.goto("/dashboard/receivables");
  await page.getByRole("row", { name: /Cliente de atribución/ }).click();
  const dialog = page.getByRole("dialog", { name: "Abono a cartera", exact: true });
  await dialog.getByRole("checkbox", { name: "Seleccionar FV-1", exact: true }).check();
  await dialog.getByRole("checkbox", { name: "Seleccionar FV-2", exact: true }).check();
  await dialog.getByRole("button", { name: "Ir a pagar", exact: true }).click();
  await page.getByRole("menuitem", { name: "Pago directo", exact: true }).click();
  await expect(dialog.getByText("Para varias facturas se usa un solo medio de pago.", { exact: true })).toBeVisible();
  await dialog.getByRole("textbox", { name: "Valor recibido en Efectivo", exact: true }).fill("60");
  await page.keyboard.press("F2");
  const card = page.getByRole("dialog", { name: "Datos de la tarjeta", exact: true });
  await card.getByPlaceholder("Aprobación del datáfono").fill("AP-UNA-FORMA");
  await card.getByRole("button", { name: /Guardar datos/ }).click();
  await expect(dialog.getByRole("combobox", { name: "Medio de pago", exact: true })).toHaveCount(1);
  await expect(dialog.getByRole("textbox", { name: "Valor recibido en Efectivo", exact: true })).toHaveCount(0);
  await expect(dialog.getByRole("textbox", { name: "Valor recibido en Tarjeta débito", exact: true })).toHaveValue("60");
  await expect(dialog.getByRole("button", { name: /^Confirmar pago/ })).toBeDisabled();
  expect(configurations).toBe(1); expect(commands).toBe(0);
});
