import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";
import { paymentCatalogFixture } from "./support/payment-catalog";

for (const mode of ["retry", "empty", "forbidden"] as const) {
  test(`servicios: forma nativa, ${mode}`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    scenario.user.permissions.push("service-invoices.read", "service-invoices.create");
    if (mode !== "forbidden") scenario.user.permissions.push("service-invoices.issue");
    await installFinancialVoucherContext(page, baseURL!, scenario);
    const cash = paymentCatalogFixture().forms[0];
    const second = { ...cash, paymentMethodId: "10000000-0000-0000-0000-000000000005",
      name: "Caja de servicios B", code: "SERVICE-CASH-B" };
    let catalogReads = 0;
    const requests: { body: Record<string, unknown>; key: string | undefined }[] = [];
    await page.route("**/api/commerce/v1/payment-methods/**", async route => {
      const query = new URL(route.request().url()).searchParams;
      expect(query.get("usage")).toBe("ServiceInvoices");
      expect(query.get("pageSize")).toBe("10");
      catalogReads++;
      await route.fulfill({ json: { items: mode === "empty" ? [] : [cash, second],
        page: 1, pageSize: 10, totalCount: mode === "empty" ? 0 : 2 } });
    });
    await page.route("**/api/commerce/v1/service-invoices/**", async route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith("/services/search")) {
        await route.fulfill({ json: { items: [{ billableServiceId: "service-1", code: "SERV-1",
          name: "Servicio de prueba", unitLabel: "Unidad", ublUnitCode: "94", unitPrice: 10000,
          taxCode: "IVA", taxName: "IVA", taxRate: 19 }], page: 1, pageSize: 100, total: 1 } });
      } else if (path.endsWith("/customers/search")) {
        await route.fulfill({ json: { items: [{ customerId: "customer-1", identification: "QA-1",
          displayName: "Cliente de servicios", partySiteId: "site-1", partySiteName: "Principal",
          partySiteAddress: "Dirección de prueba" }], page: 1, pageSize: 50, total: 1 } });
      } else if (path.endsWith("/issue")) {
        requests.push({ body: route.request().postDataJSON(), key: route.request().headers()["idempotency-key"] });
        if (requests.length === 1) await route.fulfill({ status: 503, json: { title: "Respuesta interrumpida" } });
        else await route.fulfill({ json: { documentId: "service-invoice-1", documentNumber: "FSV-1",
          fiscalNumber: "FSV-1", cufe: "cufe-qa", untaxedAmount: 10000, taxAmount: 1900,
          payableAmount: 11900, creditAmount: 0, fiscalStatus: "Pending", isReplay: true } });
      } else await route.fallback();
    });
    await page.goto("/dashboard/service-invoices");
    await page.getByRole("button", { name: /Cliente de servicios/ }).click();
    await page.getByRole("button", { name: /Servicio de prueba/ }).click();
    const picker = page.getByRole("combobox", { name: "Forma de pago", exact: true });
    const review = page.getByRole("button", { name: "Revisar y emitir", exact: true });
    await expect(review).toBeDisabled();
    if (mode === "forbidden") {
      await expect(picker).toBeDisabled();
      expect(catalogReads).toBe(0); expect(requests).toHaveLength(0); return;
    }
    await picker.click();
    if (mode === "empty") {
      await expect(page.getByText("No hay resultados.", { exact: true })).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(review).toBeDisabled(); expect(requests).toHaveLength(0); return;
    }
    await page.getByRole("option", { name: /Caja de servicios B/ }).click();
    await expect(review).toBeEnabled();
    await review.click();
    const confirmation = page.getByRole("dialog", { name: "Confirmar factura electrónica", exact: true });
    await confirmation.getByRole("button", { name: "Emitir factura", exact: true }).click();
    await expect(page.getByText("Respuesta interrumpida", { exact: false })).toBeVisible();
    await confirmation.getByRole("button", { name: "Emitir factura", exact: true }).click();
    await expect(page.getByRole("dialog", { name: "Pago y factura registrados", exact: true })).toBeVisible();
    expect(requests).toHaveLength(2);
    expect(requests[0].key).toBeTruthy(); expect(requests[1]).toEqual(requests[0]);
    expect(requests[0].body).toMatchObject({ paymentMethodId: second.paymentMethodId,
      paymentMethodVersion: second.version, paymentMethodCode: "Cash" });
    expect(catalogReads).toBe(1);
  });
}
