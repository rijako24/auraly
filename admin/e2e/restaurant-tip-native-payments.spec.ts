import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";
import { paymentCatalogFixture } from "./support/payment-catalog";

test.use({ timezoneId: "Asia/Tokyo" });

for (const method of ["Cash", "Transfer"] as const) {
  test(`propinas: entrega por la segunda forma ${method} sin recargas`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    scenario.user.permissions.push("cash.close");
    await installFinancialVoucherContext(page, baseURL!, scenario);
    const first = paymentCatalogFixture().forms.find(form => form.methodCode === method)!;
    const second = { ...first, paymentMethodId: "10000000-0000-0000-0000-000000000006",
      code: "TIP-SECOND", name: "Destino propinas B", bankAccountId: method === "Transfer" ? "tip-bank-second" : null };
    const partyId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    let pendingReads = 0, historyReads = 0;
    const requests: Record<string, unknown>[] = [];
    await page.route("**/api/commerce/v1/parties/role-options**", async route => route.fulfill({ json: {
      items: [{ partyId, roleId: "employee-role", role: "Employee", displayName: "Trabajador propinas",
        identification: "QA-TIP" }], page: 1, pageSize: 10, totalPages: 1, totalCount: 1 } }));
    await page.route("**/api/commerce/v1/payment-methods/**", async route => {
      const query = new URL(route.request().url()).searchParams;
      expect(query.get("usage")).toBe("TipPayments"); expect(query.get("methodCode")).toBe(method);
      await route.fulfill({ json: { items: [first, second], page: 1, pageSize: 10, totalCount: 2 } });
    });
    await page.route("**/api/commerce/v1/restaurant/tips/**", async route => {
      if (route.request().url().endsWith("/pending")) {
        pendingReads++;
        await route.fulfill({ json: { items: [{ collectionId: "tip-collection", accountId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
          capturedAt: "2026-11-15T12:00:00Z", amount: 5000 }], totalCount: 1, totalAmount: 5000 } });
      } else if (route.request().method() === "GET") {
        historyReads++; await route.fulfill({ json: [] });
      } else {
        const body = route.request().postDataJSON() as Record<string, unknown>; requests.push(body);
        await route.fulfill({ json: { ...body, businessId: scenario.businessId, totalAmount: 5000,
          createdAt: "2026-11-15T13:00:00Z", recipients: [{ partyId, amount: 5000 }] } });
      }
    });
    await page.goto("/dashboard/restaurant/tips");
    await expect(page.getByRole("button", { name: /Cuenta cccccccc/ })).toContainText(/0?7:00/);
    await page.getByRole("button", { name: /Cuenta cccccccc/ }).click();
    await page.getByRole("combobox", { name: "Seleccionar trabajador", exact: true }).click();
    await page.getByRole("option", { name: /Trabajador propinas/ }).click();
    if (method === "Transfer") await page.getByRole("button", { name: "Transferencia", exact: true }).click();
    const submit = page.getByRole("button", { name: /^Liquidar / });
    await expect(submit).toBeDisabled();
    await page.getByRole("combobox", { name: "Forma de entrega de propinas", exact: true }).click();
    await page.getByRole("option", { name: /Destino propinas B/ }).click();
    if (method === "Transfer") await page.getByRole("textbox", { name: "Referencia de transferencia", exact: true }).fill("QA-TIP-REF");
    await expect(submit).toBeEnabled(); await submit.click();
    await expect(page.getByRole("heading", { name: /Entrega registrada/ })).toBeVisible();
    await expect(page.getByText("No hay propinas pendientes.", { exact: true })).toBeVisible();
    expect(requests).toHaveLength(1);
    expect(requests[0]).toMatchObject({ collectionIds: ["tip-collection"], paymentMethodCode: method,
      paymentMethodId: second.paymentMethodId, paymentMethodVersion: second.version,
      bankAccountId: second.bankAccountId });
    expect(pendingReads).toBe(1); expect(historyReads).toBe(1);
  });
}
