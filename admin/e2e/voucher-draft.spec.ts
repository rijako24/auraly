import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import readExcelFile from "read-excel-file/node";
import type { SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";

test("guardar conserva el comprobante editable, sin enviarlo, y contabilizar congela la captura", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "@voucher", username: "accountant",
    firstName: "Prueba", lastName: "Comprobante", roles: [], permissions: ["accounting.read", "accounting.manual.create", "accounting.manual.send"] };
  const account = { accountId: "44444444-4444-4444-4444-444444444444", code: "519595", name: "Servicios", accountType: "Expense", allowsPosting: true, requiresParty: false, isActive: true };
  let saved: VoucherDraft | undefined;
  let writes = 0, sends = 0, draftReads = 0, listReads = 0, fail = true;
  await page.context().addCookies([{ name: "auth_token", value: "voucher", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ user, businessId }) => {
    localStorage.setItem("selected_tenant_id", user.tenantId); localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auraly:pwa-install-dismissed", "1");
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
  }, { user, businessId });
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    let body: unknown = [], status = 200;
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa de prueba" }];
    else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede" }];
    else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, permissions: user.permissions, roles: [] };
    else if (path.endsWith("/reference-options/accounting-manual-concept")) body = [{ id: "concept", code: "MANUAL_VOUCHER", label: "Comprobante manual" }];
    else if (path.endsWith("/reference-options/accounting-document-status")) body = [{ id: "created", code: "Created", label: "Creado / sin enviar" }, { id: "pending", code: "Pending", label: "Enviado / pendiente" }];
    else if (path.endsWith("/accounting/account-options")) body = { items: [account], page: 1, pageSize: 10, totalPages: 1, totalCount: 1 };
    else if (path.endsWith("/accounting/documents")) { listReads++; body = { items: saved ? [saved.row] : [], page: 1, pageSize: 25, totalCount: saved ? 1 : 0, totalPages: 1 }; }
    else if (path.includes("/accounting/manual/drafts/")) {
      if (request.method() === "PUT") {
        writes++;
        const value = request.postDataJSON() as SaveVoucherDraft;
        saved = { ...value, lines: value.lines.map(value => ({ value, accountCode: value.accountId ? account.code : null,
          accountName: value.accountId ? account.name : null, partyName: null, partyIdentification: null, costCenterName: null })),
          rowVersion: "AAAAAAAAAA" + writes + "=", currencyCode: "COP", sentAt: null, status: "Created", canEdit: true, canSend: true,
          row: { sourceDocumentId: value.documentId, sourceDocumentType: value.documentType, sourceDocumentNumber: value.reference, occurredAt: value.occurredAt,
            status: "Created", attemptCount: 0, errorCode: null, errorMessage: null, entryId: null, entryNumber: null,
            debitTotal: 1200, creditTotal: 1200, postedAt: null, fiscalDocumentType: null, dianNumber: null, uniqueCodeType: null, uniqueCode: null, fiscalStatus: null, hasManualDraft: true } };
      } else if (path.endsWith("/send")) {
        sends++;
        if (fail) { status = 409; body = { detail: "No hay un período abierto para esta fecha." }; }
        else if (saved) saved = { ...saved, status: "Pending", sentAt: new Date().toISOString(), canEdit: false, canSend: false, row: { ...saved.row, status: "Pending" } };
      } else draftReads++;
      if (status === 200) body = saved;
    }
    await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
  });
  await page.goto("/dashboard/financial-traceability");
  await expect(page.getByRole("button", { name: "Generar reporte" })).toHaveCount(0);
  await page.getByRole("button", { name: "Nuevo comprobante" }).click();
  await page.getByLabel("Descripción", { exact: true }).fill("Reclasificación de prueba");
  await page.getByRole("button", { name: "Volver a trazabilidad" }).click();
  const discard = page.getByRole("dialog", { name: "¿Salir sin guardar?" });
  await expect(discard).toBeVisible();
  await discard.getByRole("button", { name: "Seguir editando" }).click();
  await expect(discard).toHaveCount(0);
  const first = page.getByRole("row", { name: "Partida 1" });
  await expect(page.getByRole("columnheader", { name: "Descripción u observación" })).toBeVisible();
  await expect(first.getByLabel("Referencia de la partida")).toHaveCount(0);
  await first.getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
  await page.getByPlaceholder("Buscar por código o nombre…").fill("Servicios");
  await page.getByRole("option", { name: /519595.*Servicios/ }).click();
  await first.getByLabel("Descripción de la partida", { exact: true }).fill("Débito");
  await first.getByLabel("Débito de la partida").fill("1200");
  await expect(first.getByText("D", { exact: true })).toBeVisible();
  const second = page.getByRole("row", { name: "Partida 2" });
  await second.getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
  await page.getByRole("option", { name: /519595.*Servicios/ }).click();
  await second.getByLabel("Crédito de la partida").fill("1200");
  await expect(second.getByText("C", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: "La descripción de la partida 2" })).toContainText("es obligatoria");
  expect(writes).toBe(0);
  await second.getByLabel("Descripción de la partida", { exact: true }).fill("Crédito");
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeEnabled();
  expect(writes).toBe(1); expect(sends).toBe(0); expect(draftReads).toBe(0); expect(listReads).toBe(1);
  await expect(page).toHaveURL(/document=/);
  await page.screenshot({ path: "../work/comprobante-manual.png", fullPage: true });
  await page.getByRole("button", { name: "Volver a trazabilidad" }).click();
  await expect(page).toHaveURL(/\/dashboard\/financial-traceability$/);
  await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
  await expect(page.getByText("No hay un período abierto para esta fecha.")).toBeVisible();
  expect(sends).toBe(1); expect(draftReads).toBe(0);
  await page.getByRole("button", { name: "Ver documento" }).click();
  await page.getByRole("button", { name: "Imprimir / exportar", exact: true }).click();
  await expect(page.getByText(/NO CONTABILIZADO/).first()).toBeVisible();
  const pdfDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "Descargar PDF", exact: true }).click();
  const pdf = await pdfDownload;
  expect((await readFile((await pdf.path())!)).subarray(0, 5).toString()).toBe("%PDF-");
  const excelDownload = page.waitForEvent("download");
  await page.getByRole("button", { name: "Exportar Excel", exact: true }).click();
  const excel = await excelDownload;
  const [{ data: sheet }] = await readExcelFile((await excel.path())!);
  expect(sheet[2][0]).toContain("NO CONTABILIZADO");
  expect(sheet[4][8]).toBe(1200);
  await page.getByRole("button", { name: "Cerrar", exact: true }).click();
  await page.getByLabel("Descripción", { exact: true }).fill("Descripción editada");
  await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeDisabled();
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: "período abierto" })).toBeVisible();
  await expect(page.getByLabel("Descripción", { exact: true })).toHaveValue("Descripción editada");
  fail = false;
  await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
  await expect(page.getByText("Documento enviado: contenido inmutable.")).toBeVisible();
  await expect(page.getByLabel("Descripción", { exact: true })).toBeDisabled();
  expect(sends).toBe(3); expect(writes).toBe(2); expect(listReads).toBe(1); expect(draftReads).toBe(0);
});
