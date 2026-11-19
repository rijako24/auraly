import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

for (const area of ["purchases", "expenses", "traceability"] as const) for (const authorized of [false, true]) {
  test(`${area}: estados contables y DIAN independientes, reintento ${authorized ? "autorizado" : "bloqueado"}`, async ({ page, baseURL }) => {
    const scenario = financialVoucherScenario();
    scenario.user.permissions.push("expenses.read", "purchasing.goods-receipts.read");
    if (authorized) scenario.user.permissions.push("fiscal.retry", "accounting.postings.retry");
    else scenario.user.permissions = scenario.user.permissions.filter(permission => permission !== "fiscal.manual-support.send");
    const ids = [1, 2, 3, 4].map(value => `aaaaaaaa-0000-0000-0000-${String(value).padStart(12, "0")}`);
    const states = ["DianAccepted", "DianRejected", area === "traceability" ? "AwaitingManualSubmission" : "PendingSubmission", null];
    let lists = 0, reads = 0, fiscalCommands = 0, accountingCommands = 0, manualSends = 0;
    await installFinancialVoucherContext(page, baseURL!, scenario);
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith(`/fiscal/documents/${ids[2]}/send-manual-support`)) {
        manualSends++; await route.fulfill({ json: { documentId: ids[2], businessId: scenario.businessId,
          status: "PendingGeneration", lastStatusDescription: null } }); return;
      }
      if (path.endsWith(`/fiscal/documents/${ids[1]}/retry`)) {
        fiscalCommands++; expect(route.request().method()).toBe("POST");
        if(area === "purchases" && fiscalCommands === 1) {
          await route.fulfill({status:409,json:{detail:"El proceso fiscal aún no permite reintentar."}}); return;
        }
        await route.fulfill({ json: { documentId: ids[1], businessId: scenario.businessId, status: "PendingDianResult",
          lastStatusDescription: null } }); return;
      }
      if (path.endsWith(`/accounting/postings/${ids[3]}/retry`)) {
        accountingCommands++; await route.fulfill({ json: { sourceDocumentId: ids[3], status: "Pending", errorMessage: null } }); return;
      }
      if (path.includes("/fiscal/documents/") || path.includes("/accounting/postings/")) reads++;
      if (path.endsWith(area === "purchases" ? "/goods-receipts" : area === "expenses" ? "/expenses" : "/accounting/documents")) {
        lists++;
        const items = ids.map((documentId, index) => ({ ...scenario.draft.row, documentId, expenseId: documentId,
          sourceDocumentId: documentId, documentNumber: `DOC-${index}`, sourceDocumentNumber: `DOC-${index}`,
          sourceDocumentType: "Invoice", hasManualDraft: false, supplierName: "Proveedor QA", conceptName: "Servicio",
          issuedAt: scenario.draft.occurredAt, receivedAt: scenario.draft.occurredAt, updatedAt: scenario.draft.occurredAt,
          grandTotal: 100, grossAmount: 100, withholdingAmount: 0, netPayable: 100, currencyCode: "COP",
          status: area === "traceability" ? index === 3 ? "AccountingPendingConfiguration" : "Posted" : "Processed",
          accountingStatus: index === 3 ? "AccountingPendingConfiguration" : "Posted",
          accountingErrorMessage: index === 3 ? "Falta configurar IVA descontable" : null,
          errorMessage: index === 3 ? "Falta configurar IVA descontable" : null,
          fiscalDocumentType: states[index] ? "Invoice" : null, fiscalStatus: states[index],
          fiscalErrorMessage: index === 1 ? "Identificación del adquiriente no válida" : null,
        }));
        await route.fulfill({ json: { items, page: 1, pageSize: 25, totalCount: 4, totalPages: 1, grossTotal: 400, withholdingTotal: 0, netPayableTotal: 400 } }); return;
      }
      await route.fallback();
    });
    await page.goto(area === "purchases" ? "/dashboard/purchasing/goods-receipts" : area === "expenses" ? "/dashboard/expenses" : "/dashboard/financial-traceability");
    const accepted = page.getByRole("button", { name: "DIAN: Aceptado por la DIAN", exact: true });
    await expect(accepted).toHaveClass(/text-emerald-700/);
    await expect(accepted).toHaveAttribute("aria-disabled", "true");
    const rejected = page.getByRole("button", { name: "DIAN: Rechazado por la DIAN", exact: true });
    await expect(rejected).toHaveClass(/text-red-700/);
    await rejected.hover();
    await expect(page.getByRole("tooltip")).toContainText("Identificación del adquiriente no válida");
    const pending = page.getByRole("button", { name: area === "traceability" ? "DIAN: Listo para enviar a la DIAN" : "DIAN: Pendiente de envío", exact: true });
    await expect(pending).toHaveClass(area === "traceability" ? /text-red-700/ : /text-amber-700/);
    if(area !== "traceability" || !authorized)await expect(pending).toHaveAttribute("aria-disabled", "true");
    const accounting = page.getByRole("button", { name: "Contabilidad: Requiere configuración contable", exact: true });
    if (authorized) {
      await rejected.click();
      if(area === "purchases") {
        await expect(page.getByRole("alert").filter({hasText:"El proceso fiscal aún no permite reintentar."})).toBeVisible();
        await expect(rejected).toHaveClass(/text-red-700/);
        await rejected.click();
      }
      await expect(page.getByRole("button", { name: "DIAN: En proceso en la DIAN", exact: true })).toBeVisible();
      await accounting.click();
      await expect(page.getByRole("button", { name: "Contabilidad: Pendiente de contabilización", exact: true })).toBeVisible();
      expect(fiscalCommands).toBe(area === "purchases" ? 2 : 1); expect(accountingCommands).toBe(1);
      if(area === "traceability") {
        await pending.click();
        await expect(page.getByRole("button", { name: "DIAN: Pendiente de generación", exact: true })).toBeVisible();
        expect(manualSends).toBe(1);
      }
    } else {
      await expect(rejected).toHaveAttribute("aria-disabled", "true");
      await expect(accounting).toHaveAttribute("aria-disabled", "true");
      await rejected.focus(); await page.keyboard.press("Enter");
      await accounting.focus(); await page.keyboard.press("Enter");
      expect(fiscalCommands).toBe(0); expect(accountingCommands).toBe(0);
    }
    expect(reads).toBe(0); expect(lists).toBe(1);
    await expect(page.getByRole("dialog")).toHaveCount(0);
  });
}
