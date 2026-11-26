import { expect, test } from "@playwright/test";
import type { SaveVoucherDraft } from "../src/services/api/accounting";
import type { FinancialConcept, SaveFinancialConcept } from "../src/services/api/financial-concepts";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

const conceptId = "aaaaaaaa-1111-1111-1111-111111111111";
function definition(supplier = false, advance = true): FinancialConcept {
  return { financialConceptId: conceptId, code: "CONCEPTO", name: advance ? "Anticipo elegido" : "Contrapartida elegida",
    accountId: "aaaaaaaa-2222-2222-2222-222222222222", accountCode: advance ? supplier ? "13300501" : "28050501" : supplier ? "51959501" : "42959501",
    accountName: advance ? "Anticipos" : "Cuenta general", financialFunction: advance ? supplier ? "SupplierAdvance" : "CustomerAdvance" : "General",
    sideOptionId: supplier ? "debit" : "credit", sideCode: supplier ? "Debit" : "Credit", sideLabel: supplier ? "Débito" : "Crédito",
    useForReceipts: !supplier, useForSupplierPayments: supplier, useForVouchers: true, isActive: true, sortOrder: 10, rowVersion: "AAAAAAAAAAE=" };
}

for (const supplier of [false,true]) for (const width of [1366,390]) test(`${supplier?"egreso":"recibo"}: captura directa sin selector de conceptos en ${width}`,async({page,baseURL},testInfo)=>{
  await page.setViewportSize({width,height:width===390?844:768});
  const scenario=financialVoucherScenario();scenario.draft.voucherTypeCode=supplier?"Disbursement":"CashReceipt";
  if(!supplier){
    scenario.draft.lines[0].value={...scenario.draft.lines[0].value,debit:0,credit:100,existingPosition:{subledgerKind:"Receivable",subledgerId:scenario.positionId}};
    scenario.draft.lines[1].value={...scenario.draft.lines[1].value,debit:100,credit:0};
  }
  let current=scenario.draft,conceptReads=0,writes=0;
  await installFinancialVoucherContext(page,baseURL!,scenario);
  await page.route("**/api/**",async route=>{
    const request=route.request(),path=new URL(request.url()).pathname;
    if(path.endsWith("/financial-concepts")){conceptReads++;await route.fulfill({json:{items:[],page:1,pageSize:10,totalCount:0}});return;}
    if(path.endsWith(`/accounting/manual/drafts/${scenario.documentId}`)){
      if(request.method()==="PUT"){
        writes++;const capture=request.postDataJSON() as SaveVoucherDraft;
        expect(capture.lines).toHaveLength(3);
        expect(capture.lines[0].description).toBe("Partida editada directamente");
        expect(capture.lines[0].accountId).toBe(scenario.draft.lines[0].value.accountId);
        expect(capture.lines.every(line=>!line.financialConceptId)).toBe(true);
        current={...current,...capture,lines:capture.lines.map((value,index)=>({...current.lines[index],value})),rowVersion:"AAAAAAAAAAI="};
      }
      await route.fulfill({json:current});return;
    }
    await route.fallback();
  });
  await page.goto(`/dashboard/${supplier?"financial-traceability":"cash-receipts"}?document=${scenario.documentId}`);
  await expect(page.getByRole("region",{name:"Formas de pago del comprobante",exact:true})).toBeVisible();
  await expect(page.getByRole("region",{name:"Agregar concepto financiero",exact:true})).toHaveCount(0);
  await expect(page.getByRole("combobox",{name:"Concepto financiero",exact:true})).toHaveCount(0);
  await expect(page.getByRole("button",{name:"Agregar concepto",exact:true})).toHaveCount(0);
  await page.getByRole("row",{name:"Partida 1",exact:true}).getByRole("textbox",{name:"Descripción de la partida",exact:true}).fill("Partida editada directamente");
  await page.getByRole("button",{name:"Agregar partida",exact:true}).click();
  await page.getByRole("row",{name:"Partida 3",exact:true}).getByRole("textbox",{name:"Descripción de la partida",exact:true}).fill("Partida por completar");
  await page.getByRole("button",{name:"Guardar borrador",exact:true}).click();
  await expect.poll(()=>writes).toBe(1);expect(conceptReads).toBe(0);
  if(width===390){
    const table=page.getByRole("table").filter({has:page.getByRole("columnheader",{name:"Cuenta PUC",exact:true})});
    const grid=await table.boundingBox(),footer=await page.locator("footer").last().boundingBox();
    expect(footer!.y,"Las acciones no deben cubrir las partidas en móvil").toBeGreaterThanOrEqual(grid!.y+grid!.height);
  }
  await page.screenshot({path:testInfo.outputPath("voucher-without-concept-shortcut.png"),fullPage:true});
});

test("500 partidas con conceptos extensos conservan filas uniformes y permiten consultar la última", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario(), concept = { ...definition(false, false), name: "Concepto financiero extenso ".repeat(6).slice(0, 160) };
  const cash = scenario.draft.lines[1];
  cash.value = { ...cash.value, debit: 499, credit: 0, financialGroupId: scenario.documentId, paymentEvidence: { tenderedAmount: 499 } };
  scenario.draft = { ...scenario.draft, voucherTypeCode: "CashReceipt", row: { ...scenario.draft.row, debitTotal: 499, creditTotal: 499 },
    lines: [cash, ...Array.from({ length: 499 }, (_, index) => ({
      value: { accountId: concept.accountId, partyId: scenario.draft.mainPartyId ?? null, costCenterId: null,
        description: "Contrapartida explícita", debit: 0, credit: 1,
        lineId: `aaaaaaaa-1111-4111-8111-${String(index + 1).padStart(12, "0")}`,
        financialGroupId: scenario.documentId, financialConceptId: conceptId },
      accountCode: concept.accountCode, accountName: concept.accountName, partyName: "Tercero", partyIdentification: null,
      costCenterName: null, financialConceptCode: concept.code, financialConceptName: concept.name, financialConceptSideCode: concept.sideCode,
    }))] };
  let reads = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route(`**/accounting/manual/drafts/${scenario.documentId}`, async route => {
    expect(route.request().method()).toBe("GET"); reads++; await route.fulfill({ json: scenario.draft });
  });
  await page.goto(`/dashboard/cash-receipts?document=${scenario.documentId}`);
  const first = page.getByRole("row", { name: "Partida 1", exact: true });
  const second = page.getByRole("row", { name: "Partida 2", exact: true });
  await expect(second).toBeVisible();
  expect((await second.boundingBox())!.height, "Las filas virtualizadas deben conservar su altura aunque el concepto sea largo")
    .toBe((await first.boundingBox())!.height);
  const table = page.getByRole("table").filter({ has: page.getByRole("columnheader", { name: "Cuenta PUC", exact: true }) });
  await table.evaluate(element => { element.parentElement!.scrollTop = element.parentElement!.scrollHeight; });
  const last = page.getByRole("row", { name: "Partida 500", exact: true });
  await expect(last).toBeVisible();
  await last.scrollIntoViewIfNeeded();
  await expect(last).toBeInViewport();
  await expect(last.getByTitle(`${concept.code} · ${concept.name}`, { exact: true })).toHaveCount(1);
  expect(await page.getByRole("row", { name: /^Partida \d+$/ }).count()).toBeLessThanOrEqual(24);
  expect(reads).toBe(1);
});

test("la bandeja filtra por el concepto de las partidas, incluso inactivo, mediante su identidad", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario(), concept = { ...definition(), isActive: false };
  const queries: URL[] = []; let options = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith("/financial-concepts")) {
      options++; expect(url.searchParams.get("usage")).toBe("Filter"); expect(url.searchParams.has("isActive")).toBe(false);
      await route.fulfill({ json: { items: [concept], page: 1, pageSize: 10, totalCount: 1 } }); return;
    }
    if (url.pathname.endsWith("/accounting/documents")) {
      queries.push(url); const items = url.searchParams.get("financialConceptId") === conceptId ? [{ ...scenario.draft.row, sourceDocumentNumber: "RC-CONCEPTO", voucherTypeCode: "CashReceipt" }] : [];
      await route.fulfill({ json: { items, page: 1, pageSize: 25, totalCount: items.length, totalPages: items.length } }); return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/cash-receipts");
  await expect(page.getByText("No hay documentos para estos filtros.", { exact: true })).toBeVisible(); expect(options).toBe(0);
  await page.getByRole("button",{name:"Filtros de documentos",exact:true}).click();
  expect(queries).toHaveLength(1);
  await page.getByRole("combobox", { name: "Concepto financiero", exact: true }).click();
  await page.getByRole("option", { name: /Anticipo elegido/ }).click();
  await expect(page.getByText("RC-CONCEPTO", { exact: true })).toBeVisible();
  expect(queries).toHaveLength(2); expect(queries[1].searchParams.get("financialConceptId")).toBe(conceptId);
  expect(queries[1].searchParams.get("voucherTypeCode")).toBe("CashReceipt");
  await page.getByRole("button",{name:"Filtros de documentos",exact:true}).click();
  await expect(page.getByRole("combobox",{name:"Concepto financiero",exact:true})).toHaveCount(0);
  await expect(page.getByText("RC-CONCEPTO",{exact:true})).toBeVisible();
  expect(queries).toHaveLength(2);
  await page.getByRole("button",{name:"Filtros de documentos",exact:true}).click();
  await expect(page.getByRole("combobox",{name:"Concepto financiero",exact:true})).toContainText(concept.name);
  await page.getByRole("button", { name: "Quitar selección de Concepto financiero", exact: true }).click();
  await expect(page.getByText("No hay documentos para estos filtros.", { exact: true })).toBeVisible();
  // The unchanged unfiltered page is already authoritative in this journey.
  expect(queries).toHaveLength(2); expect(options).toBe(1);
});

test("el maestro guarda la cuenta y naturaleza y reutiliza la página devuelta sin otro GET", async ({ page, baseURL }, testInfo) => {
  const scenario = financialVoucherScenario(), concept = definition();
  scenario.user.permissions.push("catalog.read","catalog.update");
  let reads = 0, writes = 0;
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    if (path.endsWith("/reference-options/accounting-balance-side")) {
      await route.fulfill({ json: [{ id: "debit", code: "Debit", label: "Débito" }, { id: "credit", code: "Credit", label: "Crédito" }] }); return;
    }
    if (path.endsWith("/accounting/account-options")) {
      await route.fulfill({ json: { items: [{ accountId: concept.accountId, code: concept.accountCode, name: concept.accountName }], page: 1, pageSize: 10, totalCount: 1, totalPages: 1 } }); return;
    }
    if (path.endsWith("/financial-concepts")) {
      reads++; await route.fulfill({ json: { items: [], page: 1, pageSize: 50, totalCount: 0 } }); return;
    }
    if (path.includes("/financial-concepts/") && request.method() === "PUT") {
      writes++; const capture = request.postDataJSON() as SaveFinancialConcept;
      expect(capture).toMatchObject({ code: "ANT", name: "Anticipo elegido", accountId: concept.accountId, sideOptionId: "credit", useForReceipts: true });
      const saved = { ...concept, ...capture, rowVersion: "AAAAAAAAAAI=" };
      await route.fulfill({ json: { concept: saved, page: { items: [saved, { ...concept, financialConceptId: "concurrent", code: "OTRO", name: "Concepto concurrente" }], page: 1, pageSize: 50, totalCount: 2 } } }); return;
    }
    await route.fallback();
  });
  await page.goto("/dashboard/accounting?section=concepts");
  await expect(page.getByRole("heading",{name:"Conceptos contables",exact:true})).toBeVisible();
  await expect(page.getByText("No hay conceptos financieros para este filtro.", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Nuevo concepto", exact: true }).click();
  await page.getByLabel("Código", { exact: true }).fill("ANT"); await page.getByLabel("Nombre", { exact: true }).fill("Anticipo elegido");
  await page.getByRole("combobox", { name: "Seleccionar cuenta contable", exact: true }).click();
  await page.getByPlaceholder("Buscar por código o nombre…", { exact: true }).fill(concept.accountCode);
  await page.getByRole("option", { name: new RegExp(concept.accountCode) }).click();
  await page.getByRole("combobox", { name: "Naturaleza", exact: true }).click(); await page.getByRole("option", { name: "Crédito", exact: true }).click();
  await page.getByRole("checkbox", { name: "Recibos de caja", exact: true }).check();
  await page.getByRole("button", { name: "Guardar concepto", exact: true }).click();
  await expect(page.getByText("Concepto concurrente", { exact: true })).toBeVisible();
  await expect(page.getByText("Página 1 · 2 conceptos", { exact: true })).toBeVisible();
  expect(reads).toBe(1); expect(writes).toBe(1);
  await page.screenshot({ path: testInfo.outputPath("financial-concept-master.png"), fullPage: true });
});


test("conceptos contables exige permiso de catálogo y no consulta una ficha bloqueada",async({page,baseURL})=>{
  const scenario=financialVoucherScenario();await installFinancialVoucherContext(page,baseURL!,scenario);
  let reads=0;await page.route("**/financial-concepts**",route=>{reads++;return route.fulfill({json:{items:[],page:1,pageSize:20,totalCount:0}});});
  await page.goto("/dashboard/accounting?section=concepts");
  await expect(page.getByRole("button",{name:"Conceptos contables",exact:true})).toBeDisabled();
  await expect(page.getByRole("alert").filter({hasText:"No tienes permiso para consultar los conceptos contables."})).toBeVisible();expect(reads).toBe(0);
});
