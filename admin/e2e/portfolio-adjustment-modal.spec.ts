import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft } from "../src/services/api/accounting";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const partyId = "33333333-3333-3333-3333-333333333333";
const invoiceId = "44444444-4444-4444-4444-444444444444";
const user = {
  userId: "55555555-5555-5555-5555-555555555555", tenantId,
  tenantKey: "@adjustment-test", username: "adjustment-test", firstName: "Prueba", lastName: "Cartera",
  roles: [], permissions: ["payables.read", "receivables.read", "accounting.read", "accounting.manual.create",
    "accounting.manual.send", "payables.payments.create", "receivables.payments.create"],
};

for (const direction of ["payables", "receivables"] as const) {
  test(`ajuste de cartera abre modal con contexto desde ${direction}`, async ({ page, baseURL }) => {
    if (direction === "receivables") await page.setViewportSize({ width: 390, height: 844 });
    const payable = direction === "payables";
    let partyListReads = 0;
    let paymentListReads = 0;
    let invoiceFailure = false, invoiceEmpty = false;
    let nativeDetailReads = 0;
    let unapplicationReads = 0, voucherSends = 0, treasuryAccountReads = 0;
    const treasuryAccountId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    let creditUnapplicationReads = 0;
    const savedUnapplications: SaveVoucherDraft[] = [];
    const originalTransactionId = "99999999-9999-9999-9999-999999999999";
    const originalCreditTransactionId = "88888888-8888-8888-8888-888888888888";
    const originalCreditId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    const originalCreditAccountId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    const positionReads: string[] = [];
    const partyRole = payable ? "Supplier" : "Customer";
    const partyName = payable ? "Proveedor de prueba" : "Cliente de prueba";
    const documentNumber = payable ? "GTO00-20" : "FV00-20";
    const invoice = {
      documentNumber, currencyCode: "COP", originalAmount: 5000, outstandingAmount: 3500,
      dueDate: "2026-09-25T12:00:00-05:00", status: "Open", isOverdue: false,
      createdAt: "2026-09-24T12:00:00-05:00",
      ...(payable ? { payableId: invoiceId, supplierId: partyId, supplierName: partyName }
        : { receivableId: invoiceId, customerId: partyId, customerName: partyName, partySiteId: null, partySiteName: null, paidAmount: 1500 }),
    };
    const detail = {
      ...invoice, transactions: [{ transactionId: originalCreditTransactionId, type: "CreditApplication",
        amount: 1500, balanceChange: -1500, sourceDocumentId: invoiceId, occurredAt: invoice.createdAt },
        { transactionId: originalTransactionId, type: "Payment", amount: 500.0001, balanceChange: -500.0001,
          sourceDocumentId: invoiceId, occurredAt: invoice.createdAt }], sourceDocumentId: invoiceId, sourceDocumentType: "Expense",
      ...(payable ? { supplierIdentification: "1001", expenseConceptName: null, expenseDescription: "", sourceInvoiceNumber: null, goodsReceiptId: null }
        : { customerIdentification: "1001" }),
    };
    const position = { subledgerKind: payable ? "Payable" : "Receivable", subledgerId: invoiceId,
      partyId, counterpartyRoleId: partyId, partyName, partyIdentification: "1001",
      sourceDocumentId: invoiceId, sourceDocumentType: payable ? "Expense" : "PosSale", documentNumber,
      occurredAt: invoice.createdAt, dueDate: invoice.dueDate, currencyCode: "COP", originalAmount: 5000,
      outstandingAmount: 3500, pendingPaymentAmount: 500, pendingAdjustmentAmount: 0, availableAmount: 3000,
      openingEntryId: invoiceId, openingEntryLineNumber: 1, accountId: partyId,
      accountCode: payable ? "220505" : "130505", accountName: payable ? "Proveedores" : "Clientes",
      accountIsActive: true, openingDebit: payable ? 0 : 5000, openingCredit: payable ? 5000 : 0,
      openingLinkIssue: null, status: "PartiallyPaid" };

    await page.context().addCookies([{ name: "auth_token", value: "adjustment-test", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ user, businessId }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { user, businessId });

    await page.route("**/api/**", async route => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      let body: unknown = [];
      if (path === "/api/auth/me") body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Pruebas" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede pruebas" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith("/parties/role-options")) body = { items: [{ partyId, roleId: partyId, role: partyRole,
        displayName: partyName, identification: "1001", supplierPurchaseEvidencePolicy: null,
        supplierDefaultPaymentDueDays: null }], page: 1, totalPages: 1, totalCount: 1 };
      else if (path.endsWith("/accounting/open-positions")) {
        positionReads.push(url.search);
        body = { items: [position], page: 1, pageSize: Number(url.searchParams.get("pageSize")), totalCount: 1, totalOutstandingAmount: 3500 };
      }
      else if (path.endsWith("/accounting/account-options")) {
        treasuryAccountReads++;
        body = { items: [{ accountId: treasuryAccountId, tenantId, code: "11050501", name: "Caja de devoluciones",
          accountType: "Asset", isPostable: true, isActive: true, requiresThirdParty: false }],
          page: 1, pageSize: 20, totalCount: 1, totalPages: 1 };
      }
      else if (path.endsWith("/accounting/unapplication-capture")) {
        if (url.searchParams.get("transactionId") === originalCreditTransactionId) {
          creditUnapplicationReads++;
          body = { description: "Desaplicar cruce conservando el crédito original", reference: documentNumber, mainPartyId: partyId,
            mainPartyName: partyName, mainPartyIdentification: "1001", lines: [
              { value: { accountId: position.accountId, partyId, description: "Reabrir obligación",
                debit: payable ? 0 : 1500, credit: payable ? 1500 : 0,
                existingPosition: { subledgerKind: position.subledgerKind, subledgerId: invoiceId, reversesTransactionId: originalCreditTransactionId } },
                accountCode: position.accountCode, accountName: position.accountName, partyName, partyIdentification: "1001" },
              { value: { accountId: originalCreditAccountId, partyId, description: "Restituir saldo a favor original",
                debit: payable ? 1500 : 0, credit: payable ? 0 : 1500,
                existingPosition: { subledgerKind: payable ? "SupplierCredit" : "CustomerCredit", subledgerId: originalCreditId } },
                accountCode: payable ? "133005" : "280505", accountName: "Saldo a favor original", partyName, partyIdentification: "1001" },
            ] };
          await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
          return;
        }
        unapplicationReads++;
        const amount = unapplicationReads === 1 ? 500.0001 : 250.0001;
        expect(url.searchParams.get("subledgerKind")).toBe(position.subledgerKind);
        expect(url.searchParams.get("subledgerId")).toBe(invoiceId);
        expect(url.searchParams.get("transactionId")).toBe(originalTransactionId);
        body = { description: unapplicationReads === 1 ? "Desaplicar abono conservando anticipo" : "Desaplicar el importe restante", reference: documentNumber, mainPartyId: partyId,
          mainPartyName: partyName, mainPartyIdentification: "1001", lines: [
            { value: { accountId: position.accountId, partyId, costCenterId: null, description: "Reabrir obligación",
              debit: payable ? 0 : amount, credit: payable ? amount : 0,
              existingPosition: { subledgerKind: position.subledgerKind, subledgerId: invoiceId, reversesTransactionId: originalTransactionId } },
              accountCode: position.accountCode, accountName: position.accountName, partyName, partyIdentification: "1001", costCenterName: null },
            { value: { accountId: null, partyId, costCenterId: null, description: "Dinero conservado como anticipo",
              debit: payable ? amount : 0, credit: payable ? 0 : amount },
              accountCode: null, accountName: null, partyName, partyIdentification: "1001", costCenterName: null },
          ] };
      }
      else if (path.includes("/accounting/manual/drafts/") && route.request().method() === "PUT") {
        const value = route.request().postDataJSON() as SaveVoucherDraft;
        savedUnapplications.push(value);
        body = { ...value, currencyCode: "COP", rowVersion: "AAAAAAAAAAE=", sentAt: null, status: "Created", canEdit: true, canSend: false,
          mainPartyName: partyName, mainPartyIdentification: "1001",
          lines: value.lines.map((line, index) => ({ value: line, accountCode: index === 0 ? position.accountCode : line.accountId === treasuryAccountId ? "11050501" : null,
            accountName: index === 0 ? position.accountName : line.accountId === treasuryAccountId ? "Caja de devoluciones" : null, partyName, partyIdentification: "1001", costCenterName: null })),
          row: { sourceDocumentId: value.documentId, sourceDocumentType: value.documentType, sourceDocumentNumber: value.reference,
            occurredAt: value.occurredAt, status: "Created", attemptCount: 0, errorCode: null, errorMessage: null, entryId: null,
            entryNumber: null, debitTotal: 500.0001, creditTotal: 500.0001, postedAt: null, fiscalDocumentType: null,
            dianNumber: null, uniqueCodeType: null, uniqueCode: null, fiscalStatus: null } };
      }
      else if (path.includes("/accounting/manual/drafts/") && path.endsWith("/send")) voucherSends++;
      else if (path.endsWith(`/${direction}/${invoiceId}`)) { nativeDetailReads++; body = detail; }
      else if (path.endsWith(`/${direction}`)) {
        if (invoiceFailure) {
          await route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "Fallo de prueba al consultar cartera." }) });
          return;
        }
        body = { items: invoiceEmpty ? [] : [invoice], page: 1, pageSize: 20,
          totalCount: invoiceEmpty ? 0 : 1, totalPages: invoiceEmpty ? 0 : 1,
          totalOutstanding: invoiceEmpty ? 0 : 3500, totalOverdue: 0 };
      }
      else if (path.endsWith(payable ? "/payable-payments" : "/receivable-payments")) {
        paymentListReads++;
        body = { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, totalAmount: 0 };
      }
      else if (path.endsWith("/payables/suppliers") || path.endsWith("/receivables/customers")) {
        partyListReads++;
        // Searching an invoice number is not a match in the party directory.
        // Invoice totals must come from the filtered invoice response itself.
        const matched = !url.searchParams.get("search");
        body = {
        items: matched ? [{ [payable ? "supplierId" : "customerId"]: partyId,
          [payable ? "supplierName" : "customerName"]: partyName, identification: "1001",
          invoiceCount: 1, originalAmount: 5000, paidAmount: 1500, outstandingAmount: 3500,
          overdueAmount: 0, supplierCreditAmount: 0, currencyCode: "COP" }] : [],
        page: 1, pageSize: 20, totalCount: matched ? 1 : 0, totalPages: matched ? 1 : 0,
        totalOutstanding: matched ? 3500 : 0, totalOverdue: 0, totalInvoiceCount: matched ? 1 : 0,
        };
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });

    await page.goto(`/dashboard/${direction}`);
    await page.getByRole("button", { name: "Ajuste de cartera", exact: true }).click();
    const modal = page.getByRole("dialog", { name: "Ajuste de cartera" });
    await expect(modal.getByRole("heading", { name: "Ajuste de cartera", level: 2 })).toBeVisible();
    await expect(modal.getByLabel("Valor del ajuste")).toBeDisabled();
    await modal.getByRole("combobox", { name: `Seleccionar ${partyRole.toLowerCase()}` }).click();
    await page.getByRole("option", { name: new RegExp(partyName) }).click();
    await modal.getByRole("combobox", { name: "Seleccionar factura para ajuste de cartera" }).click();
    await page.getByRole("option", { name: new RegExp(documentNumber) }).click();
    await expect(modal.getByText(`${partyName} · 1001`)).toBeVisible();
    await expect(modal.getByText(documentNumber, { exact: true }).last()).toBeVisible();
    await expect(modal.getByText("$ 3.500").first()).toBeVisible();
    await expect(modal.getByText("$ 3.000", { exact: true })).toBeVisible();
    await expect(modal.getByText(`${position.accountCode} · ${position.accountName}`, { exact: true })).toBeVisible();
    expect(positionReads).toHaveLength(1);
    expect(new URLSearchParams(positionReads[0]).get("counterpartyRoleId")).toBe(partyId);
    expect(new URLSearchParams(positionReads[0]).has("subledgerId")).toBe(false);
    expect(nativeDetailReads).toBe(0);
    await expect(modal.getByLabel("Valor del ajuste")).toBeEnabled();
    await expect(modal.getByRole("button", { name: "Close" })).toBeVisible();
    await modal.getByRole("button", { name: "Cerrar" }).click();
    await expect(modal).toHaveCount(0);
    await expect(page).toHaveURL(new RegExp(`/dashboard/${direction}$`));

    await page.getByRole("tab", { name: "Facturas" }).click();
    const invoiceSearch = page.getByPlaceholder("Número de documento o identificación", { exact: true });
    if (!await invoiceSearch.isVisible()) await page.getByText("Filtros", { exact: true }).click();
    const filteredInvoices = page.waitForResponse(response => {
      const url = new URL(response.url());
      return url.pathname.endsWith(`/${direction}`) && url.searchParams.get("search") === documentNumber;
    });
    await invoiceSearch.fill(documentNumber);
    await filteredInvoices;
    await page.getByRole("row", { name: new RegExp(documentNumber) }).click();
    expect(partyListReads).toBe(1);
    const nativeDetail = page.getByRole("dialog", { name: documentNumber });
    await expect(nativeDetail.getByText("Anticipo o saldo a favor aplicado", { exact: true })).toBeVisible();
    await expect(nativeDetail.getByText(/−.*1\.500/)).toBeVisible();
    await nativeDetail.getByRole("button", { name: "Desaplicar cruce", exact: true }).click();
    const creditUnapplication = page.getByRole("dialog", { name: "Desaplicar cruce", exact: true });
    await expect(creditUnapplication.getByText(/Restituye el saldo a favor original/)).toBeVisible();
    await expect(creditUnapplication.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).last()).toContainText("Saldo a favor original");
    expect(creditUnapplicationReads).toBe(1);
    expect(savedUnapplications).toHaveLength(0);
    await creditUnapplication.getByRole("button", { name: "Cerrar", exact: true }).click();
    await page.getByRole("dialog", { name: "¿Salir sin guardar?", exact: true }).getByRole("button", { name: "Salir sin guardar", exact: true }).click();
    await expect(creditUnapplication).toHaveCount(0);
    await page.getByRole("row", { name: new RegExp(documentNumber) }).click();
    await nativeDetail.getByRole("button", { name: "Ajuste de cartera" }).click();
    await expect(modal.getByRole("combobox", { name: "Seleccionar factura para ajuste de cartera" })).toContainText(documentNumber);
    await expect(modal.getByText(`${partyName} · 1001`)).toBeVisible();
    await expect(modal.getByText("$ 3.500").first()).toBeVisible();
    await modal.getByRole("button", { name: "Close" }).click();
    await expect(modal).toHaveCount(0);
    await page.getByRole("row", { name: new RegExp(documentNumber) }).click();
    await nativeDetail.getByRole("button", { name: "Desaplicar", exact: true }).click();
    const unapplication = page.getByRole("dialog", { name: "Desaplicar abono", exact: true });
    await expect(unapplication.getByText(/una cuenta de caja, banco o tarjeta si el dinero fue devuelto/)).toBeVisible();
    await expect(unapplication.getByLabel("Descripción", { exact: true })).toHaveValue("Desaplicar abono conservando anticipo");
    await expect(unapplication.getByText(`${position.accountCode} · ${position.accountName}`, { exact: true })).toBeVisible();
    const detailReadsBeforeSave = nativeDetailReads;
    const positionReadsBeforeSave = positionReads.length;
    expect(unapplicationReads).toBe(1);
    expect(savedUnapplications).toHaveLength(0);
    await unapplication.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(unapplication.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeDisabled();
    expect(savedUnapplications).toHaveLength(1);
    const savedUnapplication = savedUnapplications[0];
    expect(savedUnapplication?.payloadVersion).toBe(4);
    expect(savedUnapplication?.voucherTypeCode).toBe("Accounting");
    expect(savedUnapplication?.lines[0].existingPosition?.reversesTransactionId).toBe(originalTransactionId);
    expect(savedUnapplication?.lines[0][payable ? "credit" : "debit"]).toBe(500.0001);
    expect(savedUnapplication?.lines[1].accountId).toBeNull();
    expect(unapplicationReads).toBe(1);
    expect(nativeDetailReads).toBe(detailReadsBeforeSave);
    expect(positionReads).toHaveLength(positionReadsBeforeSave);
    expect(voucherSends).toBe(0);
    const bounds = await unapplication.evaluate(node => ({ width: node.clientWidth, content: node.scrollWidth, left: node.scrollLeft }));
    expect(bounds.content, "La tabla debe desplazarse dentro del editor, sin recortar el modal.").toBeLessThanOrEqual(bounds.width);
    expect(bounds.left).toBe(0);
    await unapplication.getByRole("button", { name: "Cerrar", exact: true }).click();
    await expect(unapplication).toHaveCount(0);
    await page.getByRole("row", { name: new RegExp(documentNumber) }).click();
    await nativeDetail.getByRole("button", { name: "Desaplicar", exact: true }).click();
    await expect(unapplication.getByLabel("Descripción", { exact: true })).toHaveValue("Desaplicar el importe restante");
    await expect(unapplication.getByLabel(payable ? "Crédito de la partida" : "Débito de la partida").first()).toHaveValue("250.0001");
    expect(unapplicationReads).toBe(2);
    await unapplication.getByRole("row", { name: "Partida 2", exact: true }).getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
    await page.getByRole("option", { name: "11050501 · Caja de devoluciones", exact: true }).click();
    await unapplication.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
    await expect(unapplication.getByRole("button", { name: "Guardar comprobante", exact: true })).toBeDisabled();
    expect(savedUnapplications).toHaveLength(2);
    expect(savedUnapplications[1]?.lines[1].accountId).toBe(treasuryAccountId);
    expect(savedUnapplications[1]?.lines[1][payable ? "debit" : "credit"]).toBe(250.0001);
    expect(savedUnapplications[1]?.lines[0].existingPosition?.reversesTransactionId).toBe(originalTransactionId);
    expect(treasuryAccountReads).toBe(1);
    expect(unapplicationReads).toBe(2);
    await unapplication.getByLabel("Descripción", { exact: true }).fill("Cambio sin guardar");
    await unapplication.getByRole("button", { name: "Cerrar", exact: true }).click();
    const discard = page.getByRole("dialog", { name: "¿Salir sin guardar?", exact: true });
    await expect(discard).toBeVisible();
    await discard.getByRole("button", { name: "Salir sin guardar", exact: true }).click();
    await expect(unapplication).toHaveCount(0);
    await expect(page.getByRole("region", { name: "Resumen de cartera" }).getByText(/3\.500/)).toBeVisible();
    expect(partyListReads, "Una pestaña de terceros oculta no debe consultar para reconstruir totales de facturas.").toBe(1);
    await page.getByRole("tab", { name: payable ? "Pagos" : "Recaudos", exact: true }).click();
    await expect(page.getByText(`No hay ${payable ? "pagos" : "recaudos"} para estos filtros.`, { exact: true })).toBeVisible();
    await expect(page.getByRole("region", { name: "Resumen de cartera" })).toHaveCount(0);
    expect(paymentListReads).toBe(1);
    expect(partyListReads).toBe(1);
    await page.getByRole("tab", { name: "Facturas", exact: true }).click();
    await expect(page.getByRole("row", { name: new RegExp(documentNumber) })).toBeVisible();
    invoiceFailure = true;
    await page.getByRole("button", { name: "Actualizar", exact: true }).click();
    await expect(page.getByText(payable ? "No se pudieron cargar las obligaciones." : "No se pudo cargar la cartera.", { exact: false })).toBeVisible();
    const summary = page.getByRole("region", { name: "Resumen de cartera" });
    await expect(summary.getByText("—", { exact: true })).toHaveCount(3);
    invoiceFailure = false; invoiceEmpty = true;
    // The browser may offer PWA installation while this mobile journey runs.
    // Dismiss it through its normal control before using the covered retry.
    const dismissInstall = page.getByRole("button", { name: "No mostrar de nuevo", exact: true });
    if (await dismissInstall.isVisible()) await dismissInstall.click();
    await page.getByRole("button", { name: "Reintentar", exact: true }).click();
    await expect(summary.getByText("$ 0", { exact: true })).toHaveCount(2);
    await expect(summary.getByText("0", { exact: true })).toBeVisible();
    await expect(page.getByRole("row", { name: new RegExp(documentNumber) })).toHaveCount(0);
    expect(partyListReads).toBe(1);
    expect(paymentListReads).toBe(1);
  });
}
