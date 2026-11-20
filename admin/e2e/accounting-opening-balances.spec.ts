import { expect, test, type Page, type Route } from "@playwright/test";
import { openingRecoveryKey, openingRecoveryScope, type StoredOpeningCapture } from "../src/lib/accounting-opening-recovery";
import { closeOpeningNotificationHint } from "./support/opening-portfolio-fixture";
import type { AccountingOpeningBalance } from "../src/services/api/accounting";
import { readLocalCatalogDraft } from "./support/local-draft-evidence";
import { randomUUID } from "node:crypto";
import { writeFile } from "node:fs/promises";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const permissions = ["accounting.read", "accounting.configure", "accounting.activate", "parties.read", "catalog.update", "commerce.taxation.withholdings.view", "commerce.taxation.withholdings.manage"];

async function authenticate(page: Page, granted = permissions) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auraly:pwa-install-dismissed", "1");
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: "33333333-3333-3333-3333-333333333333", tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, granted });
}

async function json(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
}

const localOpening = (page: Page, key: string) => readLocalCatalogDraft<StoredOpeningCapture>(page, key);

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`captura la factura inicial y reutiliza las etiquetas sin consultas por fila ${viewport.width}`, async ({ page }, testInfo) => {
    const granted = viewport.width === 390 ? [...permissions, "pos.approvals.read", "pos.approvals.authorize"] : permissions;
    await page.setViewportSize(viewport);
    await page.clock.setFixedTime(new Date("2026-11-19T12:00:00-05:00"));
    const accountId = "44444444-4444-4444-4444-444444444444";
    const equityId = "55555555-5555-5555-5555-555555555555";
    const partyId = "66666666-6666-6666-6666-666666666666";
    const siteId = "77777777-7777-7777-7777-777777777777";
    let partyReads = 0, siteReads = 0, openingReads = 0, saves = 0;
    const centerReads:string[][]=[];
    let pauseReadiness = false, releaseReadiness: (() => void) | undefined;
    let activated=false, approvalRace=false, importedReads=0, releaseBeforeApproval: (()=>void)|undefined, releaseApproval: (()=>void)|undefined;
    let opening: AccountingOpeningBalance = {
      batchId: "88888888-8888-8888-8888-888888888888", businessId, effectiveOn: "2026-01-01", currencyCode: "COP",
      description: "Apertura de cartera", status: "Draft", rowVersion: "AQID", debitTotal: 1500, creditTotal: 1500,
      updatedAt: "2026-11-19T17:00:00-05:00", approvedAt: null, postedAt: null,
      lines: [
        { lineId: "99999999-9999-9999-9999-999999999999", lineNumber: 1, accountId, partyId, costCenterId: null,
          description: "Factura inicial", debit: 0, credit: 1500, existingPosition: null,
          newInvoice: { documentNumber: "PROV-320", issuedOn: "2025-09-10", dueOn: "2025-10-10", partySiteId: siteId },
          newInvoicePartySiteName: "Sede principal", partyName: "Proveedor histórico", partyIdentification: "900100200" },
        { lineId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", lineNumber: 2, accountId: equityId, partyId: null, costCenterId: null,
          description: "Contrapartida", debit: 1500, credit: 0, existingPosition: null, newInvoice: null,
          newInvoicePartySiteName: null, partyName: null, partyIdentification: null },
      ],
    };
    await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, username: "e2e", roles: ["Administrator"], permissions: granted }));
    await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
    await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly", timeZone: "America/Bogota" }]));
    await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions: granted }));
    await page.route("**/api/commerce/v1/reference-options**", route => json(route, new URL(route.request().url()).pathname.endsWith("/accounting-opening-balance-mode")?[
      {id:"zero",code:"ZeroDeclared",label:"Iniciar desde cero"},{id:"imported",code:"ImportedAndApproved",label:"Usar saldos aprobados"}]:[]));
    await page.route("**/api/commerce/v1/pos/approvals/**", route => route.request().url().endsWith("/pending")
      ? json(route, [])
      : route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ message: "Sin transporte push en esta prueba visual." }) }));
    await page.route("**/api/commerce/v1/portfolio/parties/site-options**", route => {
      siteReads++;
      const params = new URL(route.request().url()).searchParams;
      expect(params.get("partyId")).toBe(partyId);
      expect(params.get("role")).toBe("Supplier");
      expect(Number(params.get("pageSize"))).toBeLessThanOrEqual(100);
      return json(route, { items: [{ partySiteId: siteId, partyId, siteName: "Sede principal", displayName: "Proveedor histórico" }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    });
    await page.route("**/api/commerce/v1/parties**", route => {
      partyReads++;
      return json(route, { items: [{ partyId, displayName: "Proveedor histórico", identification: "900100200", roles: ["Supplier"], isActive: true }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1 });
    });
    await page.route("**/api/commerce/v1/accounting/**", async route => {
      const path = new URL(route.request().url()).pathname;
      if(path.endsWith("/cost-centers")){
        const ids=new URL(route.request().url()).searchParams.getAll("ids");if(ids.length)centerReads.push(ids);
        return json(route,{items:ids.map(id=>({costCenterId:id,code:`CC-${id.slice(0,6)}`,name:"Centro QA",isActive:true})),page:1,pageSize:100,totalCount:ids.length,totalPages:1});
      }
      if (path.endsWith("/accounts")) return json(route, [
        { accountId, code: "22050501", name: "Proveedores", accountType: "Liability", allowsPosting: true, requiresParty: true, isActive: true, semantics: { financialFunction: "Payable" } },
        { accountId: equityId, code: "310505", name: "Capital", accountType: "Equity", allowsPosting: true, requiresParty: false, isActive: true },
      ]);
      if (path.endsWith("/readiness")) {
        if(activated)return json(route,{status:"Ready",effectiveFrom:opening.effectiveOn,openingBalanceMode:"ImportedAndApproved",canEditOpeningBalances:false,blockingIssues:[]});
        if(approvalRace&&new URL(route.request().url()).searchParams.get("openingBalanceMode")==="ImportedAndApproved"){
          importedReads++;
          const issues=opening.status==="Draft"?["Hay un negocio activo sin saldos iniciales aprobados para la fecha efectiva."]:[];
          if(importedReads===1)await new Promise<void>(resolve=>{releaseBeforeApproval=resolve});
          return json(route,{status:"Configuring",canEditOpeningBalances:true,blockingIssues:issues});
        }
        if (pauseReadiness) await new Promise<void>(resolve => { releaseReadiness = resolve; });
        return json(route, { status: "Disabled", effectiveFrom: new URL(route.request().url()).searchParams.get("effectiveFrom"), blockingIssues: [], canEditOpeningBalances: true });
      }
      if(path.endsWith("/approve")){
        if(approvalRace)await new Promise<void>(resolve=>{releaseApproval=resolve});
        opening={...opening,status:"Approved",rowVersion:"QVBQUk9WRUQ="};
        return json(route,opening);
      }
      if (path.endsWith("/opening-balances")) {
        if (route.request().method() === "GET") { openingReads++; return json(route, new URL(route.request().url()).searchParams.get("effectiveOn") === opening.effectiveOn ? opening : null); }
        saves++;
        const request = route.request().postDataJSON();
        expect(request.rowVersion).toBe(opening.rowVersion);
        expect(request.lines[0]).not.toHaveProperty("partyName");
        opening = { ...opening, ...request, rowVersion: "BAUG",
          debitTotal: request.lines.reduce((total: number, line: { debit: number }) => total + line.debit, 0),
          creditTotal: request.lines.reduce((total: number, line: { credit: number }) => total + line.credit, 0),
          lines: request.lines.map((line: Record<string, unknown>, index: number) => ({ ...line, lineNumber: index + 1,
          newInvoicePartySiteName: index === 0 ? "Sede principal" : null, partyName: index === 0 ? "Proveedor histórico" : null, partyIdentification: index === 0 ? "900100200" : null })) };
        return json(route, opening);
      }
      return json(route, []);
    });
    await authenticate(page, granted);
    await page.goto("/dashboard/accounting");
    if (viewport.width === 390) {
      await page.getByRole("button", { name: "Cerrar aviso de notificaciones", exact: true }).click();
      await expect(page.getByText("Recibe aprobaciones con Auraly cerrada", { exact: true })).not.toBeVisible();
    }
    await page.getByRole("button", { name: "Saldos iniciales" }).click();
    const invoice = page.getByRole("region", { name: "Factura inicial de la partida 1" });
    await expect(invoice).toBeVisible();
    await expect(invoice.getByLabel("Número de factura original")).toHaveValue("PROV-320");
    await expect(invoice.getByText("10 de septiembre de 2025", { exact: true })).toBeVisible();
    await expect(invoice.getByText("10 de octubre de 2025", { exact: true })).toBeVisible();
    expect(partyReads).toBe(0); expect(siteReads).toBe(0);
    const save = page.getByRole("button", { name: "Guardar borrador" });
    const localKey = openingRecoveryKey(openingRecoveryScope(tenantId, "33333333-3333-3333-3333-333333333333", businessId), opening.effectiveOn);
    await invoice.getByLabel("Número de factura original").fill("");
    await expect(save).toBeDisabled();
    // Reload while the latest edit is still inside the coalescing window.
    pauseReadiness = true;
    try {
      await page.reload();
      await page.getByRole("button", { name: "Saldos iniciales" }).click();
      await expect.poll(() => !!releaseReadiness).toBe(true);
      expect(openingReads).toBe(1);
    } finally { pauseReadiness = false; releaseReadiness?.(); }
    await closeOpeningNotificationHint(page);
    await expect(invoice.getByLabel("Número de factura original")).toHaveValue("");
    expect(openingReads).toBe(2);
    await expect(save).toBeDisabled();
    expect(saves).toBe(0);
    await invoice.getByLabel("Número de factura original").fill("PROV-321");
    await invoice.getByRole("textbox", { name: "Saldo pendiente de la factura inicial 1" }).fill("1750,1254");
    await page.getByRole("textbox", { name: "Débito de apertura 2" }).fill("1750,1254");
    await invoice.getByLabel("Fecha de vencimiento", { exact: true }).click();
    await page.getByRole("button", { name: "15 de octubre de 2025", exact: true }).click();
    await invoice.getByRole("combobox", { name: "Sede de la factura inicial 1" }).click();
    await expect.poll(() => siteReads).toBe(1);
    await page.getByRole("option", { name: /Sede principal/ }).click();
    expect(siteReads).toBe(1); expect(partyReads).toBe(0);
    await expect.poll(async () => (await localOpening(page, localKey))?.capture.lines[0].credit).toBe("1750.1254");
    await page.reload();
    await page.getByRole("button", { name: "Saldos iniciales" }).click();
    await closeOpeningNotificationHint(page);
    await expect(invoice.getByLabel("Número de factura original")).toHaveValue("PROV-321");
    if (viewport.width === 1440) {
      await page.evaluate(() => {
        const put = IDBObjectStore.prototype.put;
        IDBObjectStore.prototype.put = function(...args) {
          const request = put.apply(this, args);
          IDBObjectStore.prototype.put = put;
          request.addEventListener("success", () => this.transaction.abort(), { once: true });
          return request;
        };
      });
      await invoice.getByLabel("Número de factura original").fill("NO-COMMIT");
      await expect(page.getByRole("alert").filter({ hasText: "No fue posible recuperar o guardar" })).toBeVisible();
      expect((await localOpening(page, localKey))?.capture.lines[0].newInvoice?.documentNumber).toBe("PROV-321");
      await invoice.getByLabel("Número de factura original").fill("PROV-321");
    }
    await invoice.screenshot({ path: testInfo.outputPath(`opening-invoice-fields-${viewport.width}.png`) });
    await save.click();
    await expect(page.getByRole("button", { name: "Aprobar saldos" })).toBeEnabled();
    expect(saves).toBe(1); expect(openingReads).toBe(3);
    await expect.poll(() => localOpening(page, localKey)).toBeNull();
    expect(opening.lines[0].newInvoice).toEqual({ documentNumber: "PROV-321", issuedOn: "2025-09-10", dueOn: "2025-10-15", partySiteId: siteId });
    await expect(page.getByRole("textbox", { name: "Crédito de apertura 1" })).toHaveValue(/1[\s\u00a0]750\.1254/);
    expect(opening.lines[0].credit).toBe(1750.1254);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`opening-invoice-${viewport.width}.png`), fullPage: true });
    await page.reload();
    await page.getByRole("button", { name: "Saldos iniciales" }).click();
    await expect(page.getByLabel("Número de factura original")).toHaveValue("PROV-321");
    expect(partyReads).toBe(0); expect(siteReads).toBe(1);
    await closeOpeningNotificationHint(page);
    await invoice.getByLabel("Número de factura original").fill("MI-CAPTURA");
    await expect.poll(async () => (await localOpening(page, localKey))?.capture.lines[0].newInvoice?.documentNumber).toBe("MI-CAPTURA");
    opening = { ...opening, rowVersion: "UkVNT1RF", lines: opening.lines.map((line, index) => index === 0
      ? { ...line, newInvoice: { ...line.newInvoice!, documentNumber: "OTRO-BORRADOR" } } : line) };
    await page.reload();
    await page.getByRole("button", { name: "Saldos iniciales" }).click();
    await closeOpeningNotificationHint(page);
    await expect(page.getByRole("button", { name: "Recuperar mi captura", exact: true })).toBeVisible();
    await expect(save).toBeDisabled();
    expect(saves).toBe(1);
    if (viewport.width === 1440) {
      await page.getByRole("button", { name: "Recuperar mi captura", exact: true }).click();
      await expect(invoice.getByLabel("Número de factura original")).toHaveValue("MI-CAPTURA");
      await save.click();
      await expect.poll(() => saves).toBe(2);
    } else {
      await page.getByRole("button", { name: "Usar borrador guardado", exact: true }).click();
      await expect(invoice.getByLabel("Número de factura original")).toHaveValue("OTRO-BORRADOR");
    }
    await expect.poll(() => localOpening(page, localKey)).toBeNull();
    await invoice.getByLabel("Número de factura original").fill("NO-RESTAURAR-APROBADO");
    await expect.poll(async () => (await localOpening(page, localKey))?.capture.lines[0].newInvoice?.documentNumber).toBe("NO-RESTAURAR-APROBADO");
    opening = { ...opening, status: "Approved" };
    await page.reload();
    await page.getByRole("button", { name: "Saldos iniciales" }).click();
    await expect(invoice.getByLabel("Número de factura original")).toHaveValue(viewport.width === 1440 ? "MI-CAPTURA" : "OTRO-BORRADOR");
    await expect(save).toBeDisabled();
    {
      test.setTimeout(120_000);
      const savesBeforeVolume=saves;
      const invoiceLine = opening.lines[0], balance = Number((499 * 1.1254).toFixed(4));
      opening = { ...opening, batchId: randomUUID(), effectiveOn: "2026-02-01", status: "Draft", rowVersion: "TEFSR0U=", debitTotal: balance, creditTotal: balance,
        lines: [...Array.from({ length: 499 }, (_, index) => ({ ...invoiceLine, lineId: randomUUID(), lineNumber: index + 1,
          credit: 1.1254, costCenterId:randomUUID(), newInvoice: { ...invoiceLine.newInvoice!, documentNumber: `VOLUMEN-${index + 1}` } })),
          { ...opening.lines[1], lineId: randomUUID(), lineNumber: 500, debit: balance }] };
      const card = page.getByRole("region", { name: "Saldos iniciales de la empresa", exact: true });
      await card.getByText("Fecha efectiva", { exact: true }).locator("..").getByRole("button").click();
      await page.getByRole("button", { name: "Mes siguiente", exact: true }).click();
      await page.getByRole("button", { name: "1 de febrero de 2026", exact: true }).click();
      const grid=page.getByRole("region",{name:"Partidas de saldos iniciales",exact:true});
      await expect.poll(()=>page.locator('tr[aria-label^="Partida de apertura "]').count()).toBeLessThan(25);
      await expect(page.getByText("500 partidas · Desplázate para revisar el documento completo.",{exact:true})).toBeVisible();
      await expect(page.getByRole("button",{name:"Última página",exact:true})).toHaveCount(0);
      await expect(page.getByLabel("Descripción de apertura 1", { exact: true })).toBeEnabled();
      await expect(page.getByRole("combobox",{name:"Centro de costo de apertura 1",exact:true})).toContainText(`CC-${opening.lines[0].costCenterId!.slice(0,6)}`);
      expect(centerReads).toHaveLength(1);expect(centerReads[0]).toHaveLength(20);
      await page.evaluate(() => {
        const metrics: Array<{ storageMs: number; captureMs: number }> = [];
        (window as unknown as { openingMetrics: typeof metrics }).openingMetrics = metrics;
        let editedAt = performance.now();
        document.addEventListener("input", event => { if ((event.target as HTMLElement).getAttribute("aria-label") === "Descripción de apertura 1") editedAt = performance.now(); }, true);
        const put = IDBObjectStore.prototype.put;
        IDBObjectStore.prototype.put = function(...args) {
          const startedAt = performance.now(), request = put.apply(this, args);
          const value = args[0] as { value?: StoredOpeningCapture };
          if (value.value?.kind === "capture" && value.value.capture.lines.length === 500)
            this.transaction.addEventListener("complete", () => metrics.push({ storageMs: performance.now() - startedAt, captureMs: performance.now() - editedAt }), { once: true });
          return request;
        };
      });
      for (let sample = 0; sample < 20; sample++) {
        await page.getByLabel("Descripción de apertura 1", { exact: true }).fill(`Captura de volumen ${sample}`);
        await expect.poll(() => page.evaluate(() => (window as unknown as { openingMetrics: unknown[] }).openingMetrics.length)).toBe(sample + 1);
      }
      const metrics = await page.evaluate(() => (window as unknown as { openingMetrics: Array<{ storageMs: number; captureMs: number }> }).openingMetrics);
      const p95 = (values: number[]) => values.toSorted((a, b) => a - b)[18];
      const budget = { samples: metrics, storageP95Ms: p95(metrics.map(item => item.storageMs)), captureP95Ms: p95(metrics.map(item => item.captureMs)), networkSaves: saves-savesBeforeVolume, recoveryMs: 0 };
      const budgetPath = testInfo.outputPath("opening-local-500-budget.json");
      await writeFile(budgetPath, JSON.stringify(budget, null, 2));
      expect(budget.networkSaves).toBe(0);
      expect(budget.storageP95Ms).toBeLessThan(100);
      expect(budget.captureP95Ms).toBeLessThan(1500);
      const started = Date.now();
      await page.reload();
      await page.getByRole("button", { name: "Saldos iniciales" }).click();
      await expect(page.getByLabel("Descripción de apertura 1", { exact: true })).toHaveValue("Captura de volumen 19");
      await closeOpeningNotificationHint(page);
      budget.recoveryMs = Date.now() - started;
      await writeFile(budgetPath, JSON.stringify(budget, null, 2));
      expect(budget.recoveryMs).toBeLessThan(5000);
      expect(partyReads).toBe(0); expect(siteReads).toBe(1);
      // Keep a focused input mounted even when scrolling to a distant invoice.
      await page.getByLabel("Descripción de apertura 1",{exact:true}).focus();
      await grid.evaluate(element=>{element.scrollTop=element.scrollHeight;});
      await expect(page.getByLabel("Descripción de apertura 500",{exact:true})).toHaveValue("Contrapartida");
      await expect(page.getByLabel("Descripción de apertura 1",{exact:true})).toBeFocused();
      if(viewport.width===1440){
        const bounds=await grid.boundingBox(),header=await grid.locator("thead").boundingBox();
        expect(header!.y).toBeGreaterThanOrEqual(bounds!.y);expect(header!.y).toBeLessThan(bounds!.y+3);
      }
      await expect(page.getByRole("combobox",{name:"Centro de costo de apertura 499",exact:true})).toContainText(`CC-${opening.lines[498].costCenterId!.slice(0,6)}`);
      expect(centerReads.map(ids=>ids.length)).toEqual([20,20,19]);
      await page.getByLabel("Descripción de apertura 500",{exact:true}).fill("Contrapartida revisada");
      const volumeKey=openingRecoveryKey(openingRecoveryScope(tenantId,"33333333-3333-3333-3333-333333333333",businessId),opening.effectiveOn);
      await expect.poll(async()=>(await localOpening(page,volumeKey))?.capture.lines[499].description).toBe("Contrapartida revisada");
      await grid.evaluate(element=>{element.scrollTop=0;});
      await expect(page.getByLabel("Descripción de apertura 1",{exact:true})).toHaveValue("Captura de volumen 19");
      await save.click();
      await expect(page.getByRole("button",{name:"Aprobar saldos",exact:true})).toBeEnabled();
      approvalRace=true;
      // Approval and a mode change overlap; validation still observes Draft.
      await page.getByRole("button",{name:"Aprobar saldos",exact:true}).click();
      await expect.poll(()=>!!releaseApproval).toBe(true);
      await page.getByText("Cómo iniciar",{exact:true}).locator("..").getByRole("combobox").click();
      await page.getByRole("option",{name:"Usar saldos aprobados",exact:true}).click();
      await expect.poll(()=>!!releaseBeforeApproval).toBe(true);
      try {
        releaseApproval?.();
        await expect.poll(()=>importedReads).toBe(2);
        await expect(page.getByRole("button",{name:"Activar contabilidad",exact:true})).toBeEnabled();
      }finally{releaseApproval?.();releaseBeforeApproval?.();}
      await expect(page.getByRole("button",{name:"Activar contabilidad",exact:true})).toBeEnabled();
      expect(importedReads).toBe(2);
      activated=true;opening={...opening,status:"Posted"};
      const preferenceKey=`${openingRecoveryScope(tenantId,"33333333-3333-3333-3333-333333333333",businessId)}:selected-date`;
      // A different device has no selected-date preference; the locked opening
      // must use the authoritative activation date, without querying January.
      await page.evaluate(async key=>{
        const database=await new Promise<IDBDatabase>((resolve,reject)=>{const request=indexedDB.open("auraly-catalog-work");request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error)});
        try{await new Promise<void>((resolve,reject)=>{const transaction=database.transaction("catalog-drafts","readwrite");transaction.objectStore("catalog-drafts").delete(key);transaction.oncomplete=()=>resolve();transaction.onerror=()=>reject(transaction.error)});}finally{database.close()}
      },preferenceKey);
      const readsBeforeLockedView=openingReads;
      await page.reload();await page.getByRole("button",{name:"Saldos iniciales",exact:true}).click();
      await expect(card.getByText("Fecha efectiva",{exact:true}).locator("..").getByRole("button")).toHaveText(/1 de febrero de 2026/);
      await expect(page.getByLabel("Descripción de apertura 1",{exact:true})).toHaveValue("Captura de volumen 19");
      await expect(page.getByLabel("Descripción de apertura 1",{exact:true})).toBeDisabled();
      expect(openingReads-readsBeforeLockedView).toBe(1);
    }
  });
}

test("concilia una obligación anterior, conserva su identidad y exige guardar antes de aprobar", async ({ page }) => {
  const accountId = "44444444-4444-4444-4444-444444444444";
  const equityId = "55555555-5555-5555-5555-555555555555";
  const partyId = "66666666-6666-6666-6666-666666666666";
  const positionId = "77777777-7777-7777-7777-777777777777";
  let saved: Record<string, unknown> | null = null;
  let openingReads = 0;
  let positionRequest = "";
  await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, username: "e2e", roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/reference-options**", route => json(route, []));
  await page.route("**/api/commerce/v1/parties**", route => json(route, { items: [
    { partyId, displayName: "Proveedor histórico", identification: "900100200", roles: ["Supplier"], isActive: true },
  ], page: 1, pageSize: 30, totalCount: 1, totalPages: 1 }));
  await page.route("**/api/commerce/v1/accounting/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/accounts")) return json(route, [
      { accountId, code: "22050501", name: "Proveedores", accountType: "Liability", allowsPosting: true, requiresParty: true, isActive: true, semantics: { financialFunction: "Payable" } },
      { accountId: equityId, code: "310505", name: "Capital", accountType: "Equity", allowsPosting: true, requiresParty: false, isActive: true },
    ]);
    if (path.endsWith("/readiness")) return json(route, { status: "Disabled", blockingIssues: [], canEditOpeningBalances: true });
    if (path.endsWith("/opening-balances")) {
      if (route.request().method() === "GET") { openingReads++; return json(route, saved); }
      const request = route.request().postDataJSON();
      saved = { ...request, status: "Draft", rowVersion: "AQID", debitTotal: 1500, creditTotal: 1500,
        lines: request.lines.map((line: Record<string, unknown>, index: number) => ({ ...line, lineNumber: index + 1,
          existingPositionDocumentNumber: line.existingPosition ? "HIST-1500" : null })) };
      return json(route, saved);
    }
    if (path.endsWith("/open-positions")) {
      positionRequest = route.request().url();
      return json(route, { items: [{ subledgerKind: "Payable", subledgerId: positionId, partyId,
        documentNumber: "HIST-1500", partyName: "Proveedor histórico", outstandingAmount: 1500,
        availableAmount: 1500, openingEntryId: null }], page: 1, pageSize: 10, totalCount: 1, totalOutstandingAmount: 1500 });
    }
    return json(route, []);
  });
  await authenticate(page);
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales" }).click();
  const rows = page.getByRole("row", { name: /^Partida de apertura / });
  await rows.nth(0).getByRole("combobox").first().click();
  await page.getByRole("option", { name: /22050501.*Proveedores/ }).click();
  await rows.nth(0).getByRole("button", { name: "Vincular factura existente" }).click();
  const positionPicker = page.getByRole("combobox", { name: "Vincular obligación anterior en partida 1" });
  await expect(positionPicker).toBeDisabled();
  expect(positionRequest).toBe("");
  await rows.nth(0).getByRole("combobox", { name: "Tercero de apertura 1" }).click();
  await page.getByRole("option", { name: /Proveedor histórico/ }).click();
  await positionPicker.click();
  await page.getByRole("option", { name: /HIST-1500/ }).click();
  await expect(rows.nth(0).getByRole("textbox", { name: "Crédito de apertura 1" })).toHaveValue(/1[\s\u00a0]500/);
  const query = new URL(positionRequest).searchParams;
  expect(query.get("partyId")).toBe(partyId);
  expect(query.get("subledgerKind")).toBe("Payable");
  expect(Number(query.get("pageSize"))).toBeLessThanOrEqual(100);
  await rows.nth(1).getByRole("combobox").first().click();
  await page.getByRole("option", { name: /310505.*Capital/ }).click();
  await rows.nth(1).getByRole("textbox", { name: "Débito de apertura 2" }).fill("1500");
  const approve = page.getByRole("button", { name: "Aprobar saldos" });
  await expect(approve).toBeDisabled();
  await page.getByRole("button", { name: "Guardar borrador" }).click();
  await expect(approve).toBeEnabled();
  expect(openingReads).toBe(1);
  const savedLines = saved!.lines as Array<Record<string, unknown>>;
  expect(savedLines.every(line=>line.costCenterId===null)).toBe(true);
  expect(savedLines[0].existingPosition).toEqual({ subledgerKind: "Payable", subledgerId: positionId });
  const stableId = savedLines[0].lineId;
  expect(stableId).toEqual(expect.any(String));
  await rows.nth(0).getByRole("textbox", { name: "Descripción de apertura 1" }).fill("Conciliación revisada");
  await expect(approve).toBeDisabled();
  await page.getByRole("button", { name: "Guardar borrador" }).click();
  await expect(approve).toBeEnabled();
  expect((saved!.lines as Array<Record<string, unknown>>)[0].lineId).toBe(stableId);
  await page.reload();
  await page.getByRole("button", { name: "Saldos iniciales" }).click();
  await expect(page.getByRole("combobox", { name: "Vincular obligación anterior en partida 1" })).toContainText("HIST-1500");
  expect(openingReads).toBe(2);
});

test("saldos iniciales busca cualquier tercero activo sin filtrar por rol", async ({ page }) => {
  let partyRequest = "";
  await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/reference-options**", route => json(route, []));
  await page.route("**/api/commerce/v1/accounting/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/accounts")) return json(route, [
      { accountId: "44444444-4444-4444-4444-444444444444", code: "130505", name: "Cuentas por cobrar", accountType: "Asset", allowsPosting: true, requiresParty: true, isActive: true },
      { accountId: "55555555-5555-5555-5555-555555555555", code: "310505", name: "Capital", accountType: "Equity", allowsPosting: true, requiresParty: false, isActive: true },
    ]);
    if (path.endsWith("/cost-centers")) return json(route, {items:[],page:1,pageSize:1,totalCount:0,totalPages:0});
    if (path.endsWith("/periods")) return json(route, []);
    if (path.endsWith("/mappings")) return json(route, []);
    if (path.endsWith("/category-definitions")) return json(route, []);
    if (path.endsWith("/readiness")) return json(route, { status: "Disabled", blockingIssues: ["Configura los saldos iniciales."], effectiveFrom: null, openingBalanceMode: null, canEditOpeningBalances: true });
    if (path.endsWith("/opening-balances")) return json(route, null);
    return json(route, []);
  });
  await page.route("**/api/commerce/v1/parties**", async route => {
    partyRequest = route.request().url();
    return json(route, { items: [
      { partyId: "66666666-6666-6666-6666-666666666666", displayName: "Proveedor Andino", identification: "900100200", email: null, roles: ["Supplier"], isActive: true },
      { partyId: "77777777-7777-7777-7777-777777777777", displayName: "Acreedor Logístico", identification: "901300400", email: null, roles: ["Carrier"], isActive: true },
      { partyId: "88888888-8888-8888-8888-888888888888", displayName: "Cliente Contado", identification: "10203040", email: null, roles: ["Customer"], isActive: true },
    ], page: 1, pageSize: 30, totalCount: 3, totalPages: 1 });
  });

  await authenticate(page);
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales" }).click();
  await page.getByRole("combobox", { name: "Tercero de apertura 1" }).click();

  await expect(page.getByText("Proveedor Andino")).toBeVisible();
  await expect(page.getByText("Acreedor Logístico")).toBeVisible();
  await expect(page.getByText("Cliente Contado")).toBeVisible();
  const url = new URL(partyRequest);
  expect(url.searchParams.get("role")).toBe("Any");
  expect(Number(url.searchParams.get("pageSize"))).toBeLessThanOrEqual(100);
});

test("limpia el tipo de cuenta bancaria y expone la creación de reglas de retención", async ({ page }) => {
  const bankTypeId = "99999999-9999-9999-9999-999999999999";
  const bankAccountId = "44444444-4444-4444-4444-444444444444";
  let savedBankAccount: Record<string, unknown> | null = null;

  await page.route("**/api/auth/me", route => json(route, { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }));
  await page.route("**/api/execution-context/tenants", route => json(route, [{ tenantId, name: "Auraly" }]));
  await page.route("**/api/execution-context/businesses", route => json(route, [{ tenantId, businessId, name: "Auraly" }]));
  await page.route("**/api/execution-context/access", route => json(route, { tenantId, businessId, roles: ["Administrator"], permissions }));
  await page.route("**/api/commerce/v1/reference-options/**", route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/bank-account-type")) return json(route, [{ id: bankTypeId, code: "Savings", label: "Ahorros", description: null, sortOrder: 1 }]);
    if (path.endsWith("/accounting-withholding-kind")) return json(route, [{ id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", code: "IncomeTax", label: "ReteFuente", description: null, sortOrder: 1 }]);
    return json(route, []);
  });
  await page.route("**/api/commerce/v1/accounting/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/accounts")) return json(route, [{ accountId: bankAccountId, code: "111005", name: "Bancos", accountType: "Asset", allowsPosting: true, requiresParty: false, isActive: true }]);
    if (path.endsWith("/bank-accounts") && route.request().method() === "GET") return json(route, savedBankAccount ? [savedBankAccount] : []);
    if (path.includes("/bank-accounts/") && route.request().method() === "PUT") {
      expect(route.request().postDataJSON()).not.toHaveProperty("accountingAccountId");
      savedBankAccount = { ...route.request().postDataJSON(), accountingAccountId:null, accountingAccountCode:null, accountingAccountName:null, auxiliaryCount:0, accountTypeCode: "Savings", accountTypeName: "Ahorros", currencyCode: "COP", rowVersion: "AQID" };
      return json(route, savedBankAccount);
    }
    if (path.endsWith("/readiness")) return json(route, { status: "Ready", functionalCurrencyCode: "COP", effectiveFrom: "2026-01-01", openingBalanceMode: "ZeroDeclared", activatedAt: "2026-01-01T00:00:00Z", blockingIssues: [], canEditOpeningBalances: false });
    if (path.endsWith("/opening-balances")) return json(route, null);
    return json(route, []);
  });
  let ruleRequests = 0;
  await page.route("**/api/commerce/v1/taxation/withholding-rules**", route => {
    ruleRequests++;
    return json(route, [{ ruleId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", businessId, version: 1, code: "RF-COMPRA", name: "Retefuente compras", kind: "IncomeTax", direction: "Purchase", moment: "Accrual", baseKind: "TaxExclusiveAmount", conceptCode: null, jurisdictionCode: null, rate: 2.5, minimumBase: 100000, requiredResponsibilities: [], effectiveFrom: "2026-01-01", effectiveTo: null, isActive: true }]);
  });
  let appliedRequest = "";
  let appliedRequests = 0;
  await page.route("**/api/commerce/v1/taxation/withholdings/applied?**", route => {
    appliedRequests++;
    appliedRequest = route.request().url();
    return json(route, { from: "2026-10-01", to: "2026-10-31", page: 1, pageSize: 25,
      totalCount: 2, incomeTaxTotal: 500, vatTotal: 0, industryCommerceTotal: 0,
      items: [{ documentId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", documentType: "GoodsReceiptCostDocument",
        lineNumber: 1, recognizedAt: "2026-10-03T12:00:00-05:00", documentNumber: "FLETE-42",
        supplierName: "Transportes Andinos", supplierIdentification: "900100200", kind: "IncomeTax",
        name: "Retefuente flete", ruleCode: "RF-FLETE", jurisdictionCode: null,
        taxableBase: 10000, rate: 2.5, amount: 250, isManual: false,
        documentTypeLabel: "Factura adicional configurada", kindLabel: "Retención configurada" },
        { documentId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab", documentType: "ManualAccountingVoucher",
        lineNumber: 1, recognizedAt: "2026-10-03T12:00:00-05:00", documentNumber: "DS-43",
        supplierName: "Servicios del tercero", supplierIdentification: "900100201", kind: "IncomeTax",
        name: "Retención del soporte", ruleCode: "RF-SOPORTE", jurisdictionCode: null,
        taxableBase: 10000, rate: 2.5, amount: 250, isManual: false,
        documentTypeLabel: "Soporte manual configurado", kindLabel: "Retención configurada" }] });
  });

  await authenticate(page);
  await page.goto("/dashboard/accounting");
  await expect(page.getByRole("link", { name: "Reglas de retención" })).toHaveCount(0);
  await page.getByRole("button", { name: "Cuentas bancarias" }).click();
  const bankForm = page.getByRole("heading", { name: "Nueva cuenta bancaria" }).locator("../..");
  await bankForm.getByText("Banco", { exact: true }).locator("..").locator("input").fill("Banco E2E");
  await bankForm.getByText("Nombre para mostrar", { exact: true }).locator("..").locator("input").fill("Cuenta principal E2E");
  await bankForm.getByText("Tipo de cuenta", { exact: true }).locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: "Ahorros" }).click();
  await bankForm.getByText("Número de cuenta", { exact: true }).locator("..").locator("input").fill("123456789");
  await expect(bankForm.getByText("Cuenta auxiliar del PUC", { exact: true })).toHaveCount(0);
  await bankForm.getByRole("button", { name: "Guardar cuenta bancaria" }).click();

  await expect.poll(() => savedBankAccount?.accountTypeOptionId).toBe(bankTypeId);
  await expect(bankForm.getByText("Tipo de cuenta", { exact: true }).locator("..").getByRole("combobox")).toContainText("Seleccionar tipo");
  expect(savedBankAccount).not.toHaveProperty("accountingAccountId",expect.any(String));
  await expect(page.getByRole("row").filter({hasText:"Cuenta principal E2E"})).toContainText("0 auxiliares vinculados");

  await page.getByRole("button", { name: "Retenciones" }).click();
  await expect(page.getByRole("heading", { name: "Reglas de retención" })).toBeVisible();
  await expect(page.getByText("RF-COMPRA · Retefuente compras")).toBeVisible();
  expect(ruleRequests).toBe(1);
  expect(appliedRequest).toBe("");
  await page.getByRole("tab", { name: "Retenciones aplicadas" }).click();
  await expect(page.getByRole("heading", { name: "Retenciones practicadas" })).toBeVisible();
  await expect(page.getByText("FLETE-42")).toBeVisible();
  await expect(page.getByText("Transportes Andinos")).toBeVisible();
  await expect(page.getByText("DS-43")).toBeVisible();
  await expect(page.getByText("Soporte manual configurado")).toBeVisible();
  await expect(page.getByText("Factura adicional configurada")).toBeVisible();
  expect(appliedRequests).toBe(1);
  expect(new URL(appliedRequest).searchParams.get("pageSize")).toBe("25");
  await expect(page.getByRole("heading", { name: "Reglas de retención" })).toHaveCount(0);
  expect(ruleRequests).toBe(1);
  await page.getByRole("tab", { name: "Configuración" }).click();
  await expect(page.getByRole("heading", { name: "Reglas de retención" })).toBeVisible();
  await expect(page.getByText("FLETE-42")).toHaveCount(0);
  expect(appliedRequests).toBe(1);
  expect(ruleRequests).toBe(1);
  await page.getByRole("button", { name: "Nueva regla" }).click();
  await expect(page.getByRole("heading", { name: "Nueva regla de retención" })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("heading", { name: "Responsabilidades tributarias" })).toBeVisible();
  await page.getByRole("button", { name: "Nueva responsabilidad" }).click();
  await expect(page.getByRole("heading", { name: "Nueva responsabilidad tributaria" })).toBeVisible();
});
