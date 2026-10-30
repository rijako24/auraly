import type { Page } from "@playwright/test";
import type { VoucherDraft } from "../../src/services/api/accounting";

export function financialVoucherScenario() {
  const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
  const documentId = "77777777-7777-7777-7777-777777777777", partyId = "55555555-5555-5555-5555-555555555555";
  const positionId = "99999999-9999-9999-9999-999999999999";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "@voucher", username: "accountant",
    firstName: "Prueba", lastName: "Comprobante", roles: [], permissions: ["accounting.read", "accounting.manual.create", "accounting.manual.send", "fiscal.manual-support.send", "payables.payments.create"] };
  const draft: VoucherDraft = {
    documentId, documentType: "ManualAccountingVoucher", occurredAt: "2026-10-23T12:00:00+00:00", conceptCode: "MANUAL_VOUCHER",
    description: "Abono pendiente de contabilizar", reference: "REF-V4", currencyCode: "COP", adjustment: null,
    rowVersion: "AAAAAAAAAAE=", sentAt: null, status: "Created", canEdit: true, canSend: true,
    mainPartyId: partyId, mainPartyName: "Proveedor de prueba", payloadVersion: 4, voucherTypeCode: "Disbursement",
    lines: [
      { value: { accountId: "44444444-4444-4444-4444-444444444444", partyId, costCenterId: null, description: "Abono a obligación",
        debit: 100, credit: 0, lineId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", existingPosition: { subledgerKind: "Payable", subledgerId: positionId }, reference: "FACT-1" },
        accountCode: "22050501", accountName: "Proveedor", partyName: "Proveedor de prueba", partyIdentification: null, costCenterName: null },
      { value: { accountId: "66666666-6666-6666-6666-666666666666", partyId: null, costCenterId: null, description: "Salida de caja",
        debit: 0, credit: 100, lineId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", paymentEvidence: { tenderedAmount: 100, notes: "Pago exacto" } },
        accountCode: "11050501", accountName: "Caja", partyName: null, partyIdentification: null, costCenterName: null },
    ],
    row: { sourceDocumentId: documentId, sourceDocumentType: "ManualAccountingVoucher", sourceDocumentNumber: "REF-V4", occurredAt: "2026-10-23T12:00:00+00:00",
      status: "Created", attemptCount: 0, errorCode: null, errorMessage: null, entryId: null, entryNumber: null, debitTotal: 100, creditTotal: 100,
      postedAt: null, fiscalDocumentType: null, dianNumber: null, uniqueCodeType: null, uniqueCode: null, fiscalStatus: null, hasManualDraft: true },
  };
  return { tenantId, businessId, documentId, positionId, user, draft };
}

export async function installFinancialVoucherContext(page: Page, baseURL: string, scenario: ReturnType<typeof financialVoucherScenario>) {
  const { tenantId, businessId, user } = scenario;
  await page.context().addCookies([{ name: "auth_token", value: "voucher", url: baseURL, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auraly:pwa-install-dismissed", "1");
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = [];
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa de prueba" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path.endsWith("/reference-options/accounting-manual-concept")) body = [{ id: "concept", code: "MANUAL_VOUCHER", label: "Comprobante manual" }];
    else if (path.endsWith("/reference-options/accounting-voucher-type")) body = [
      { id: "accounting", code: "Accounting", label: "Comprobante contable" },
      { id: "disbursement", code: "Disbursement", label: "Egreso" },
      { id: "support", code: "SupportDocument", label: "Documento soporte" },
      { id: "cancel-support", code: "SupportDocumentCancellation", label: "Anulación de documento soporte" },
    ];
    else if (path.endsWith("/accounting/open-positions")) body =
      { items: [], page: 1, pageSize: 10, totalCount: 0, totalOutstandingAmount: 0 };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });
}
