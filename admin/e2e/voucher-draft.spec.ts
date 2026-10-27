import { expect, test } from "@playwright/test";
import { readFile } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import readExcelFile from "read-excel-file/node";
import type { SaveVoucherDraft, VoucherDraft } from "../src/services/api/accounting";

test("guardar conserva el comprobante editable, sin enviarlo, y contabilizar congela la captura", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111", businessId = "22222222-2222-2222-2222-222222222222";
  const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "@voucher", username: "accountant",
    firstName: "Prueba", lastName: "Comprobante", roles: [], permissions: ["accounting.read", "accounting.manual.create", "accounting.manual.send"] };
  const account = { accountId: "44444444-4444-4444-4444-444444444444", code: "519595", name: "Servicios", accountType: "Expense", allowsPosting: true, requiresParty: false, isActive: true };
  const parties = [
    { partyId: "55555555-5555-5555-5555-555555555555", roleId: "55555555-5555-5555-5555-555555555555", role: "Supplier", displayName: "Proveedor de energía", identification: "900111222" },
    { partyId: "66666666-6666-6666-6666-666666666666", roleId: "66666666-6666-6666-6666-666666666666", role: "Supplier", displayName: "Otro tercero", identification: "900333444" },
  ];
  let saved: VoucherDraft | undefined;
  let writes = 0, sends = 0, draftReads = 0, listReads = 0, positionReads = 0, kindReads = 0, fail = true;
  const positionQueries: string[] = [];
  const cloneIds: string[] = [];
  const accountSearches: Array<string | null> = [];
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
    else if (path.endsWith("/reference-options/accounting-subledger-kind")) {
      kindReads++;
      body = [{ id: "payable", code: "Payable", label: "Cuenta por pagar" }];
    }
    else if (path.endsWith("/reference-options/accounting-voucher-type")) body = [{ id: "accounting", code: "Accounting", label: "Comprobante contable" }];
    else if (path.endsWith("/reference-options/accounting-document-status")) body = [{ id: "created", code: "Created", label: "Creado / sin enviar" }, { id: "pending", code: "Pending", label: "Enviado / pendiente" }];
    else if (path.endsWith("/accounting/open-positions")) {
      positionReads++;
      positionQueries.push(request.url());
      body = { items: [], page: 1, pageSize: 10, totalCount: 0, totalOutstandingAmount: 0 };
    }
    else if (path.endsWith("/accounting/account-options")) {
      const search = new URL(request.url()).searchParams.get("search");
      accountSearches.push(search);
      body = { items: search === "Cuenta inexistente" ? [] : [account], page: 1, pageSize: 10,
        totalPages: search === "Cuenta inexistente" ? 0 : 1, totalCount: search === "Cuenta inexistente" ? 0 : 1 };
    }
    else if (path.endsWith("/parties/role-options")) body = { items: parties, page: 1, pageSize: 10, totalPages: 1, totalCount: 2 };
    else if (path.endsWith("/accounting/documents")) { listReads++; body = { items: saved ? [saved.row] : [], page: 1, pageSize: 25, totalCount: saved ? 1 : 0, totalPages: 1 }; }
    else if (path.endsWith("/clone") && saved) {
      const command = request.postDataJSON() as { documentId: string; occurredAt: string };
      cloneIds.push(command.documentId);
      if (saved.documentId !== command.documentId) saved = { ...saved, documentId: command.documentId, occurredAt: command.occurredAt,
        clonedFromDocumentId: saved.documentId, reference: null, sentAt: null, status: "Created", canEdit: true, canSend: true,
        lines: saved.lines.map(line => ({ ...line, value: { ...line.value, reference: null, lineId: randomUUID() } })), rowVersion: "AAAAAAAAAA8=",
        row: { ...saved.row, sourceDocumentId: command.documentId, sourceDocumentNumber: null, occurredAt: command.occurredAt,
          status: "Created", entryId: null, entryNumber: null, postedAt: null } };
      if (cloneIds.length === 1) { status = 503; body = { detail: "No se recibió la respuesta de la copia." }; }
      else body = saved;
    }
    else if (path.includes("/accounting/manual/drafts/")) {
      if (request.method() === "PUT") {
        writes++;
        const value = request.postDataJSON() as SaveVoucherDraft;
        const principal = parties.find(party => party.partyId === value.mainPartyId);
        saved = { ...value, lines: value.lines.map(value => ({ value, accountCode: value.accountId ? account.code : null,
          accountName: value.accountId ? account.name : null, partyName: parties.find(party => party.partyId === value.partyId)?.displayName ?? null,
          partyIdentification: parties.find(party => party.partyId === value.partyId)?.identification ?? null, costCenterName: null })),
          mainPartyName: principal?.displayName, mainPartyIdentification: principal?.identification,
          rowVersion: "AAAAAAAAAA" + writes + "=", currencyCode: "COP", sentAt: null, status: "Created", canEdit: true, canSend: true,
          row: { sourceDocumentId: value.documentId, sourceDocumentType: value.documentType, sourceDocumentNumber: value.reference, occurredAt: value.occurredAt,
            mainPartyId: value.mainPartyId, mainPartyName: principal?.displayName, mainPartyIdentification: principal?.identification,
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
  await expect(page).toHaveURL(/\/dashboard\/financial-traceability\?new=1/);
  await expect(page.getByRole("heading", { name: "Comprobante manual", exact: true })).toBeVisible();
  const principal = page.getByRole("combobox", { name: "Seleccionar tercero principal", exact: true });
  await principal.click();
  await page.getByRole("option", { name: /Proveedor de energía/ }).click();
  await expect(principal).toContainText("Proveedor de energía");
  await expect(page.getByRole("row", { name: "Partida 1" }).getByRole("combobox", { name: "Seleccionar tercero", exact: true })).toContainText("Proveedor de energía");
  await page.getByLabel("Descripción", { exact: true }).fill("Reclasificación de prueba");
  expect(positionReads).toBe(0);
  await expect(page.getByRole("region", { name: "Documentos pendientes del tercero" })).toHaveCount(0);
  await page.getByRole("button", { name: "Ver documentos", exact: true }).click();
  const pending = page.getByRole("region", { name: "Documentos pendientes del tercero" });
  await expect(pending.getByText("No hay documentos pendientes para este tercero y filtro.")).toBeVisible();
  expect(positionReads).toBe(1);
  expect(kindReads).toBe(0);
  await expect(pending.getByText("Filtros", { exact: true })).toBeVisible();
  await expect(pending.getByLabel("Buscar documento")).toBeHidden();
  await pending.getByText("Filtros", { exact: true }).click();
  await expect(pending.getByLabel("Buscar documento")).toBeVisible();
  await expect.poll(() => kindReads).toBe(1);
  await expect(pending.getByRole("combobox", { name: "Tipo de saldo pendiente" })).toBeVisible();
  await expect(pending.getByRole("combobox", { name: "Seleccionar cuenta contable" })).toBeVisible();
  await expect(pending.getByText("Vence desde")).toBeVisible();
  await expect(pending.getByText("Vence hasta")).toBeVisible();
  const firstPositionQuery = new URL(positionQueries[0]).searchParams;
  expect(firstPositionQuery.get("page")).toBe("1");
  expect(firstPositionQuery.get("pageSize")).toBe("10");
  await pending.getByLabel("Buscar documento").fill("FACT-123");
  await expect.poll(() => positionQueries.some(url => new URL(url).searchParams.get("search") === "FACT-123")).toBe(true);
  await page.getByRole("button", { name: "Ocultar documentos", exact: true }).click();
  await expect(pending).toHaveCount(0);
  await page.getByRole("button", { name: "Volver a trazabilidad" }).click();
  const discard = page.getByRole("dialog", { name: "¿Salir sin guardar?" });
  await expect(discard).toBeVisible();
  await discard.getByRole("button", { name: "Seguir editando" }).click();
  await expect(discard).toHaveCount(0);
  const first = page.getByRole("row", { name: "Partida 1" });
  await expect(page.getByRole("columnheader", { name: "Descripción u observación" })).toBeVisible();
  await expect(page.getByRole("columnheader", { name: "Base (opcional)", exact: true })).toBeVisible();
  await expect(first.getByLabel("Base de la partida", { exact: true })).toHaveValue("");
  await first.getByLabel("Base de la partida", { exact: true }).fill("-123.4567");
  await expect(first.getByLabel("Referencia de la partida")).toHaveCount(0);
  const accountField = first.getByRole("combobox", { name: "Seleccionar cuenta contable" });
  await accountField.focus();
  await accountField.press("5");
  const accountSearch = page.getByPlaceholder("Buscar por código o nombre…");
  await expect(accountSearch).toBeFocused();
  await expect(accountSearch).toHaveValue("5");
  await accountSearch.fill("Servicios");
  await expect(page.getByRole("option", { name: /519595.*Servicios/ })).toBeVisible();
  await accountSearch.press("ArrowDown");
  await accountSearch.press("Enter");
  await expect(accountField).toContainText("519595");
  expect(accountSearches).toContain("Servicios");
  expect(accountSearches.every(search => Boolean(search?.trim()))).toBe(true);
  await accountField.click();
  await accountSearch.fill("Cuenta inexistente");
  await expect(page.getByText("No hay resultados.", { exact: true })).toBeVisible();
  await expect(page.getByRole("option", { name: /519595.*Servicios/ })).toHaveCount(0);
  await accountSearch.press("Escape");
  await expect(accountField).toContainText("519595");
  await expect(first.getByRole("combobox", { name: "Seleccionar tercero", exact: true })).toContainText("Proveedor de energía");
  await first.getByLabel("Descripción de la partida", { exact: true }).fill("Débito");
  await first.getByLabel("Débito de la partida", { exact: true }).fill("1200");
  await expect(first.getByText("D", { exact: true })).toBeVisible();
  await first.getByRole("button", { name: "Intercambiar débito y crédito de la partida 1", exact: true }).click();
  await expect(first.getByText("C", { exact: true })).toBeVisible();
  await first.getByRole("button", { name: "Intercambiar débito y crédito de la partida 1", exact: true }).click();
  await expect(first.getByText("D", { exact: true })).toBeVisible();
  const second = page.getByRole("row", { name: "Partida 2" });
  await expect(second.getByLabel("Base de la partida", { exact: true })).toHaveValue("");
  await second.getByLabel("Base de la partida", { exact: true }).fill("0");
  await expect(second.getByLabel("Base de la partida", { exact: true })).toHaveValue("0");
  await second.getByLabel("Base de la partida", { exact: true }).fill("");
  await expect(second.getByLabel("Base de la partida", { exact: true })).toHaveValue("");
  await second.getByLabel("Base de la partida", { exact: true }).fill("0");
  await second.getByRole("combobox", { name: "Seleccionar tercero", exact: true }).click();
  await page.getByRole("option", { name: /Otro tercero/ }).click();
  await principal.click();
  await page.getByRole("option", { name: /Otro tercero/ }).click();
  await expect(first.getByRole("combobox", { name: "Seleccionar tercero", exact: true })).toContainText("Proveedor de energía");
  await expect(second.getByRole("combobox", { name: "Seleccionar tercero", exact: true })).toContainText("Otro tercero");
  await second.getByRole("combobox", { name: "Seleccionar cuenta contable" }).click();
  await page.getByRole("option", { name: /519595.*Servicios/ }).click();
  await second.getByLabel("Crédito de la partida", { exact: true }).fill("1200");
  await expect(second.getByText("C", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: "La descripción de la partida 2" })).toContainText("es obligatoria");
  expect(writes).toBe(0);
  await second.getByLabel("Descripción de la partida", { exact: true }).fill("Crédito");
  await page.getByRole("button", { name: "Guardar comprobante", exact: true }).click();
  await expect(page.getByRole("button", { name: "Contabilizar", exact: true })).toBeEnabled();
  expect(writes).toBe(1); expect(sends).toBe(0); expect(draftReads).toBe(0); expect(listReads).toBe(1);
  expect(saved?.mainPartyId).toBe(parties[1].partyId);
  expect(saved?.payloadVersion).toBe(4);
  expect(saved?.voucherTypeCode).toBe("Accounting");
  expect(saved?.lines.every(line => Boolean(line.value.lineId))).toBe(true);
  expect(new Set(saved?.lines.map(line => line.value.lineId)).size).toBe(saved?.lines.length);
  expect(saved?.lines.map(line => line.value.partyId)).toEqual(parties.map(party => party.partyId));
  expect(saved?.lines.map(line => line.value.baseAmount)).toEqual([-123.4567, 0]);
  await expect(page).toHaveURL(/document=/);
  await page.screenshot({ path: "../work/comprobante-manual.png", fullPage: true });
  await page.getByRole("button", { name: "Volver a trazabilidad" }).click();
  await expect(page).toHaveURL(/\/dashboard\/financial-traceability$/);
  await expect(page.getByRole("columnheader", { name: "Tercero principal", exact: true })).toBeVisible();
  await expect(page.getByRole("cell", { name: /Otro tercero.*900333444/ })).toBeVisible();
  await page.getByRole("button", { name: "Contabilizar", exact: true }).click();
  await expect(page.getByText("No hay un período abierto para esta fecha.")).toBeVisible();
  expect(sends).toBe(1); expect(draftReads).toBe(0);
  await page.getByRole("button", { name: "Ver documento" }).click();
  await expect(page.getByLabel("Base de la partida", { exact: true }).first()).toHaveValue("-123.4567");
  await expect(page.getByLabel("Base de la partida", { exact: true }).nth(1)).toHaveValue("0");
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
  await expect(page.getByLabel("Base de la partida", { exact: true }).first()).toBeDisabled();
  expect(sends).toBe(3); expect(writes).toBe(2); expect(listReads).toBe(1); expect(draftReads).toBe(0);
  const sourceId = saved!.documentId;
  const sourceLineIds = new Set(saved!.lines.map(line => line.value.lineId));
  await page.getByRole("button", { name: "Clonar comprobante", exact: true }).click();
  await expect(page.getByRole("alert").filter({ hasText: "No se recibió la respuesta de la copia." })).toBeVisible();
  await page.getByRole("button", { name: "Clonar comprobante", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`document=${cloneIds[0]}`));
  expect(cloneIds).toHaveLength(2);
  expect(cloneIds[0]).toBe(cloneIds[1]);
  expect(saved!.clonedFromDocumentId).toBe(sourceId);
  expect(saved!.lines.some(line => sourceLineIds.has(line.value.lineId))).toBe(false);
  await expect(page.getByText("Copia nueva.", { exact: false })).toBeVisible();
  await expect(page.getByLabel("Referencia", { exact: true })).toHaveCount(0);
  await expect(page.getByLabel("Descripción", { exact: true })).toBeEnabled();
  await expect(page.getByLabel("Descripción", { exact: true })).toHaveValue("Descripción editada");
  await expect(page.getByLabel("Base de la partida", { exact: true }).first()).toHaveValue("-123.4567");
  await expect(page.getByLabel("Base de la partida", { exact: true }).nth(1)).toHaveValue("0");
  expect(sends).toBe(3); expect(writes).toBe(2); expect(listReads).toBe(1); expect(draftReads).toBe(0);
});
