import { expect, test, type Locator, type Page } from "@playwright/test";
import { randomUUID } from "node:crypto";
import type { AccountingAccount, AccountingEntry, AccountingOpeningBalance, AccountingOpenPositionPage } from "../src/services/api/accounting";
import type { PayablesReportPage } from "../src/services/api/payables";
import type { ReceivablesReportPage } from "../src/services/api/receivables";
import type { PaymentMethodPage } from "../src/services/api/payment-methods";
import { addCalendarDays, proposedAccountingDate } from "../src/lib/accounting-document-date";
import { closeOpeningNotificationHint, openingPortfolioContext, type OpeningPartyFixture, type OpeningPortfolioFixture } from "./support/opening-portfolio-fixture";
import { realAccountingContext, realPartyRequest, waitForRealPosting } from "./support/real-accounting-workspace";
import { openingRecoveryKey, openingRecoveryScope, type StoredOpeningCapture } from "../src/lib/accounting-opening-recovery";
import { readLocalCatalogDraft } from "./support/local-draft-evidence";

test("empresa QA de apertura: navegador, contexto propio y configuración inicial", async ({ page }, testInfo) => {
  test.setTimeout(120_000);
  const { fixture, context } = await openingPortfolioContext(page);
  const readiness = await context.scopedJson<{ status: string; canEditOpeningBalances: boolean; blockingIssues: unknown[] }>("GET",
    `/api/commerce/v1/accounting/readiness?effectiveFrom=${proposedAccountingDate(context.timeZone).slice(0, 10)}&openingBalanceMode=ImportedAndApproved`);
  expect(readiness.canEditOpeningBalances).toBe(true);
  console.info(JSON.stringify({ openingQaPrerequisites: readiness.blockingIssues }));
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await closeOpeningNotificationHint(page);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await testInfo.attach("readiness", { body: JSON.stringify({ tenantId: fixture.TenantId, businessId: fixture.BusinessId, readiness }), contentType: "application/json" });
  await page.screenshot({ path: testInfo.outputPath("opening-qa-before-capture-mobile.png") });
});

async function chooseDate(page: Page, trigger: Locator, iso: string) {
  await trigger.click();
  const months = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
  const target = new Date(`${iso}T12:00:00Z`), targetMonth = target.getUTCFullYear() * 12 + target.getUTCMonth();
  const heading = page.getByRole("button", { name: "Mes anterior", exact: true }).locator("..").locator("p");
  for (let attempt = 0; attempt < 24; attempt++) {
    const current = /^(\p{L}+) de (\d{4})$/u.exec((await heading.innerText()).toLocaleLowerCase("es-CO"));
    expect(current).toBeTruthy();
    const month = Number(current![2]) * 12 + months.indexOf(current![1]);
    if (month === targetMonth) break;
    await page.getByRole("button", { name: month < targetMonth ? "Mes siguiente" : "Mes anterior", exact: true }).click();
  }
  const label = new Intl.DateTimeFormat("es-CO", { day: "numeric", month: "long", year: "numeric", timeZone: "UTC" }).format(target);
  await page.getByRole("button", { name: label, exact: true }).click();
}

type PreparedOpening=NonNullable<OpeningPortfolioFixture["Opening"]>;
async function activateAndVerifyOpening(page:Page,setup:Awaited<ReturnType<typeof openingPortfolioContext>>,prepared:PreparedOpening){
  const {fixture,context,save}=setup,{scopedJson}=context,api="/api/commerce/v1";
  await page.getByText("Cómo iniciar",{exact:true}).locator("..").getByRole("combobox").click();
  await page.getByRole("option",{name:"Usar saldos aprobados",exact:true}).click();
  await expect(page.getByRole("button",{name:"Activar contabilidad",exact:true})).toBeEnabled();
  const activating = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/accounting/activate"));
  await page.getByRole("button", { name: "Activar contabilidad", exact: true }).click();
  const activated = await activating; expect(activated.ok(), await activated.text()).toBe(true);
  expect(["Configuring","Ready"]).toContain((await activated.json()).status);
  await waitForRealPosting(context, prepared.batchId);
  const readiness=await scopedJson<{status:string}>("GET",`${api}/accounting/readiness?effectiveFrom=${prepared.effectiveOn}&openingBalanceMode=ImportedAndApproved`);
  expect(readiness.status).toBe("Ready");
  const entry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${prepared.batchId}`);
  expect(entry.debitTotal).toBe(2001.0004); expect(entry.creditTotal).toBe(2001.0004);
  for (const kind of ["Receivable", "Payable"] as const) {
    const positions = await scopedJson<AccountingOpenPositionPage>("GET", `${api}/accounting/open-positions?subledgerKind=${kind}&counterpartyRoleId=${prepared[kind].roleId}&page=1&pageSize=10`);
    expect(positions.items).toHaveLength(2);
    expect(positions.items.every(position => position.openingEntryId === entry.entryId)).toBe(true);
    expect(positions.items.map(position => position.documentNumber).sort()).toEqual(prepared.documents.filter(document => document.kind === kind).map(document => document.number).sort());
  }

  fixture.Opening=prepared;delete fixture.PreparedOpening;await save();
}

test("recarga inmediata real: conserva la última edición incompleta sin guardarla en la empresa",async({page},testInfo)=>{
  test.setTimeout(180_000);page.setDefaultTimeout(20_000);
  const {fixture,context,save}=await openingPortfolioContext(page);
  test.skip(!fixture.RecoveryOnly||!!fixture.Opening,"Requiere la propia empresa QA de recuperación sin apertura financiera.");
  let commands=0;page.on("request",request=>{if(new URL(request.url()).pathname.includes("/accounting/opening-balances")&&request.method()!=="GET")commands++});
  const accounts=await context.scopedJson<AccountingAccount[]>("GET","/api/commerce/v1/accounting/accounts");
  const account=accounts.find(row=>row.isActive&&row.allowsPosting&&row.semantics?.financialFunction==="Receivable")!;expect(account).toBeTruthy();
  await page.goto("/dashboard/accounting");
  const loading=page.waitForResponse(response=>response.request().method()==="GET"&&new URL(response.url()).pathname.endsWith("/opening-balances"));
  await page.getByRole("button",{name:"Saldos iniciales",exact:true}).click();
  const loaded=await loading;expect(loaded.ok()).toBe(true);expect(await loaded.json()).toBeNull();
  const effectiveOn=new URL(loaded.url()).searchParams.get("effectiveOn")!;
  const capture=page.getByRole("region",{name:"Saldos iniciales de la empresa",exact:true});
  await capture.getByRole("button",{name:"Agregar factura inicial",exact:true}).click();
  await capture.getByRole("combobox",{name:"Cuenta de apertura 1",exact:true}).click();
  await page.getByRole("option",{name:`${account.code} · ${account.name}`,exact:true}).click();
  let capturedBatch:string|undefined;
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:844});await closeOpeningNotificationHint(page);
    for(let attempt=0;attempt<3;attempt++){
      const reference=`RECARGA-${width}-${attempt}`;
      await capture.getByRole("region",{name:"Factura inicial de la partida 1",exact:true}).getByLabel("Número de factura original").fill(reference);
      await page.reload();await page.getByRole("button",{name:"Saldos iniciales",exact:true}).click();await closeOpeningNotificationHint(page);
      await expect(capture.getByRole("region",{name:"Factura inicial de la partida 1",exact:true}).getByLabel("Número de factura original")).toHaveValue(reference);
      await expect(capture.getByRole("button",{name:"Guardar borrador",exact:true})).toBeDisabled();
      const key=openingRecoveryKey(openingRecoveryScope(fixture.TenantId,fixture.UserId,fixture.BusinessId),effectiveOn);
      const local=await readLocalCatalogDraft<StoredOpeningCapture>(page,key);expect(local).toBeTruthy();
      if(capturedBatch)expect(local!.capture.batchId).toBe(capturedBatch);else capturedBatch=local!.capture.batchId;
    }
  }
  expect(commands).toBe(0);
  expect(await context.scopedJson("GET",`/api/commerce/v1/accounting/opening-balances?effectiveOn=${effectiveOn}`)).toBeNull();
  await capture.getByRole("region",{name:"Factura inicial de la partida 1",exact:true}).screenshot({path:testInfo.outputPath("immediate-reload-incomplete-mobile.png")});
  // Volume preparation is confined to this browser's existing local draft.
  // No invoice, opening or financial effect is created on the server.
  const key=openingRecoveryKey(openingRecoveryScope(fixture.TenantId,fixture.UserId,fixture.BusinessId),effectiveOn);
  const stored=(await readLocalCatalogDraft<StoredOpeningCapture>(page,key))!;
  const source=stored.capture.lines[0];
  stored.capture.lines=Array.from({length:500},(_,index)=>({...source,lineId:randomUUID(),description:`Volumen ${index+1}`,
    newInvoice:index%2===0?{...source.newInvoice!,documentNumber:`VOLUMEN-${index+1}`}:null}));
  await page.goto("/dashboard/accounting");
  await page.evaluate(async({key,value})=>{
    const database=await new Promise<IDBDatabase>((resolve,reject)=>{const request=indexedDB.open("auraly-catalog-work");request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error)});
    try{await new Promise<void>((resolve,reject)=>{const transaction=database.transaction("catalog-drafts","readwrite");transaction.objectStore("catalog-drafts").put({key,value,updatedAt:new Date().toISOString()});transaction.oncomplete=()=>resolve();transaction.onabort=()=>reject(transaction.error);transaction.onerror=()=>reject(transaction.error)})}finally{database.close()}
  },{key,value:stored});
  for(const width of [1440,390]){
    await page.setViewportSize({width,height:844});
    await page.reload();await page.getByRole("button",{name:"Saldos iniciales",exact:true}).click();await closeOpeningNotificationHint(page);
    await expect(capture.getByText("500 partidas · Desplázate para revisar el documento completo.",{exact:true})).toBeVisible();
    const grid=capture.getByRole("region",{name:"Partidas de saldos iniciales",exact:true});
    const first=grid.getByLabel("Descripción de apertura 1",{exact:true});await first.focus();
    await grid.evaluate(element=>{element.scrollTop=element.scrollHeight;});
    const last=grid.getByLabel("Descripción de apertura 500",{exact:true});await expect(last).toHaveValue(width===1440?"Volumen 500":"Revisado 1440");
    await expect(first).toBeFocused();
    await last.fill(`Revisado ${width}`);
    await expect.poll(async()=>(await readLocalCatalogDraft<StoredOpeningCapture>(page,key))?.capture.lines[499].description).toBe(`Revisado ${width}`);
    expect(await grid.locator('tr[aria-label^="Partida de apertura "]').count()).toBeLessThan(30);
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
    await grid.screenshot({path:testInfo.outputPath(`opening-500-continuous-${width}.png`)});
    await expect(capture.getByRole("button",{name:"Agregar línea",exact:true})).toBeDisabled();
    await expect(capture.getByRole("button",{name:"Guardar borrador",exact:true})).toBeDisabled();
  }
  expect(commands).toBe(0);
  expect((await readLocalCatalogDraft<StoredOpeningCapture>(page,key))?.capture.batchId).toBe(capturedBatch);
  expect(await context.scopedJson("GET",`/api/commerce/v1/accounting/opening-balances?effectiveOn=${effectiveOn}`)).toBeNull();
  fixture.RecoveryOnlyPassedAtUtc=new Date().toISOString();await save();
});

test("apertura inicial real: captura facturas de cliente y proveedor desde escritorio y móvil", async ({ page }, testInfo) => {
  test.setTimeout(300_000);
  page.setDefaultTimeout(20_000);
  const setup=await openingPortfolioContext(page),{fixture,context,save}=setup;
  test.skip(!!fixture.Opening||!!fixture.PreparedOpening, "Conservar la captura guardada; continuar su activación o ejecutar pagos/reportes.");
  const { scopedJson } = context, api = "/api/commerce/v1";
  const accounts = await scopedJson<AccountingAccount[]>("GET", `${api}/accounting/accounts`);
  const accountFor = (kind: string) => accounts.find(account => account.isActive && account.allowsPosting && account.semantics?.financialFunction === kind)!;
  const receivable = accountFor("Receivable"), payable = accountFor("Payable");
  expect(receivable).toBeTruthy(); expect(payable).toBeTruthy();
  const forms = await scopedJson<PaymentMethodPage>("GET", `${api}/payment-methods?page=1&pageSize=50`);
  const cashForms = forms.items.filter(form => form.isActive && form.methodCode === "Cash" && form.accountId);
  expect(cashForms).toHaveLength(1);
  const cash = accounts.find(account => account.accountId === cashForms[0].accountId)!;
  const parties: Record<string, OpeningPartyFixture> = {}, prefix = randomUUID().slice(0, 8);
  for (const kind of ["Receivable", "Payable"] as const) {
    const supplier = kind === "Payable", name = `${fixture.Label} ${supplier ? "Proveedor" : "Cliente"} ${prefix}`;
    const party = await scopedJson<{ partyId: string; customerId?: string; supplierId?: string }>("POST", `${api}/${supplier ? "suppliers" : "customers"}`, {
      ...await realPartyRequest(context, name), ...(supplier ? { purchaseEvidencePolicy: "InternalReceiptVoucher", defaultPaymentDueDays: 30 } : {}),
    });
    parties[kind] = { partyId: party.partyId, roleId: (supplier ? party.supplierId : party.customerId)!, name, accountId: supplier ? payable.accountId : receivable.accountId };
  }
  const effectiveOn = proposedAccountingDate(context.timeZone).slice(0, 10), issuedOn = addCalendarDays(effectiveOn, -60);
  const documents = (["Receivable", "Payable"] as const).flatMap(kind => [0, 1].map(index => ({ kind,
    number: `INI-${kind === "Receivable" ? "CXC" : "CXP"}-${prefix}-${index + 1}`, amount: index === 0 ? 1000.1254 : 1000.875,
    issuedOn, dueOn: addCalendarDays(effectiveOn, index === 0 ? -20 : 15) })));
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  const capture = page.getByRole("region", { name: "Saldos iniciales de la empresa", exact: true });
  await chooseDate(page, capture.getByText("Fecha efectiva", { exact: true }).locator("..").getByRole("button"), effectiveOn);
  // A failed browser attempt may have left only a draft in this isolated QA
  // company. Reuse its aggregate and keep the two initial rows before capture.
  const rows = capture.getByRole("row", { name: /^Partida de apertura / });
  await expect(rows.first()).toBeVisible();
  for (let count = await rows.count(); count > 2; count--)
    await capture.getByRole("button", { name: "Eliminar línea", exact: true }).last().click();
  for (const [index, document] of documents.entries()) {
    if (index >= 2) await capture.getByRole("button", { name: "Agregar factura inicial", exact: true }).click();
    const row = capture.getByRole("row", { name: `Partida de apertura ${index + 1}`, exact: true });
    const account = document.kind === "Receivable" ? receivable : payable, party = parties[document.kind];
    await row.getByRole("combobox", { name: `Cuenta de apertura ${index + 1}`, exact: true }).click();
    await page.getByRole("option", { name: `${account.code} · ${account.name}`, exact: true }).click();
    await row.getByRole("combobox", { name: `Tercero de apertura ${index + 1}`, exact: true }).click();
    await page.getByPlaceholder("Buscar por nombre o identificación…").fill(party.name);
    await page.getByRole("option").filter({ hasText: party.name }).click();
    const invoice = capture.getByRole("region", { name: `Factura inicial de la partida ${index + 1}`, exact: true });
    await invoice.getByLabel("Número de factura original").fill(document.number);
    await invoice.getByRole("combobox", { name: `Sede de la factura inicial ${index + 1}`, exact: true }).click();
    await page.getByRole("option").filter({ hasText: "Sede de prueba" }).click();
    await chooseDate(page, invoice.getByLabel("Fecha de emisión original", { exact: true }), document.issuedOn);
    await chooseDate(page, invoice.getByLabel("Fecha de vencimiento", { exact: true }), document.dueOn);
    await invoice.getByRole("textbox", { name: `Saldo pendiente de la factura inicial ${index + 1}`, exact: true }).fill(String(document.amount).replace(".", ","));
  }
  await expect(capture.getByText("· Cuadrado", { exact: true })).toBeVisible();
  const actorId = await page.evaluate(() => JSON.parse(localStorage.getItem("auth-state")!).state.user.userId as string);
  const recoveryKey = openingRecoveryKey(openingRecoveryScope(fixture.TenantId, actorId, fixture.BusinessId), effectiveOn);
  await expect.poll(async () => (await readLocalCatalogDraft<StoredOpeningCapture>(page, recoveryKey))?.capture.lines.map(line => line.newInvoice?.documentNumber)).toEqual(documents.map(document => document.number));
  const localCapture = (await readLocalCatalogDraft<StoredOpeningCapture>(page, recoveryKey))!;
  await page.reload();
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  for (const [index, document] of documents.entries())
    await expect(capture.getByRole("region", { name: `Factura inicial de la partida ${index + 1}`, exact: true }).getByLabel("Número de factura original")).toHaveValue(document.number);
  await expect(page.getByText("Recuperamos la captura de este equipo.", { exact: false })).toBeVisible();
  const saving = page.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname.endsWith("/opening-balances"));
  await capture.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  const savedResponse = await saving; expect(savedResponse.ok(), await savedResponse.text()).toBe(true);
  const saved = await savedResponse.json() as AccountingOpeningBalance;
  expect(saved.batchId).toBe(localCapture.capture.batchId);
  expect(saved.lines).toHaveLength(4);
  expect(saved.lines.map(line => line.newInvoice?.documentNumber)).toEqual(documents.map(document => document.number));
  expect(saved.debitTotal).toBe(2001.0004); expect(saved.creditTotal).toBe(2001.0004);
  await expect.poll(() => readLocalCatalogDraft(page, recoveryKey)).toBeNull();
  fixture.PreparedOpening={batchId:saved.batchId,effectiveOn,cashAccountId:cash.accountId,cashFormName:cashForms[0].name,
    Receivable:parties.Receivable,Payable:parties.Payable,documents};
  await save();
  await page.reload();
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  for (const [index, document] of documents.entries())
    await expect(capture.getByRole("region", { name: `Factura inicial de la partida ${index + 1}`, exact: true }).getByLabel("Número de factura original")).toHaveValue(document.number);
  await capture.screenshot({ path: testInfo.outputPath("initial-opening-desktop.png") });
  await page.setViewportSize({ width: 390, height: 844 });
  await closeOpeningNotificationHint(page);
  expect(await capture.evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
  await capture.getByRole("region", { name: "Factura inicial de la partida 1", exact: true }).screenshot({ path: testInfo.outputPath("initial-invoice-mobile.png") });
  await expect(capture.getByRole("textbox", { name: "Débito de apertura 1", exact: true })).toHaveValue(/1[\s\u00a0]000\.1254/);
  const approving=page.waitForResponse(response=>response.request().method()==="POST"&&response.url().endsWith(`/opening-balances/${saved.batchId}/approve`));
  await capture.getByRole("button", { name: "Aprobar saldos", exact: true }).click();
  const approved=await approving;expect(approved.ok(),await approved.text()).toBe(true);
  expect((await approved.json()).status).toBe("Approved");
  await activateAndVerifyOpening(page,setup,fixture.PreparedOpening!);
});

test("apertura aprobada real: continúa la activación sin reemplazar las facturas capturadas",async({page},testInfo)=>{
  test.setTimeout(180_000);page.setDefaultTimeout(20_000);
  const setup=await openingPortfolioContext(page),prepared=setup.fixture.PreparedOpening;
  test.skip(!prepared||!!setup.fixture.Opening,"Solo continúa una captura real de QA que quedó aprobada.");
  const stored=await setup.context.scopedJson<AccountingOpeningBalance>("GET",`/api/commerce/v1/accounting/opening-balances?effectiveOn=${prepared!.effectiveOn}`);
  expect(stored.status).toBe("Approved");expect(stored.batchId).toBe(prepared!.batchId);
  expect(stored.lines.map(line=>line.newInvoice?.documentNumber)).toEqual(prepared!.documents.map(document=>document.number));
  await page.goto("/dashboard/accounting");
  await page.getByRole("button",{name:"Saldos iniciales",exact:true}).click();
  const capture=page.getByRole("region",{name:"Saldos iniciales de la empresa",exact:true});
  await chooseDate(page,capture.getByText("Fecha efectiva",{exact:true}).locator("..").getByRole("button"),prepared!.effectiveOn);
  await page.setViewportSize({width:390,height:844});await closeOpeningNotificationHint(page);
  await expect(capture.getByRole("button",{name:"Aprobar saldos",exact:true})).toBeDisabled();
  await capture.screenshot({path:testInfo.outputPath("approved-opening-resumed-mobile.png")});
  await activateAndVerifyOpening(page,setup,prepared!);
});

test("apertura contabilizada real: consulta la fecha oficial desde otro navegador en móvil",async({page},testInfo)=>{
  test.setTimeout(120_000);page.setDefaultTimeout(20_000);
  const {fixture,context}=await openingPortfolioContext(page);expect(fixture.Opening).toBeTruthy();
  const opening=fixture.Opening!;
  const stored=await context.scopedJson<AccountingOpeningBalance>("GET",`/api/commerce/v1/accounting/opening-balances?effectiveOn=${opening.effectiveOn}`);
  expect(stored.status).toBe("Posted");expect(stored.batchId).toBe(opening.batchId);
  await page.goto("/dashboard/accounting");await page.getByRole("button",{name:"Saldos iniciales",exact:true}).click();
  await page.setViewportSize({width:390,height:844});await closeOpeningNotificationHint(page);
  const capture=page.getByRole("region",{name:"Saldos iniciales de la empresa",exact:true});
  const date=capture.getByText("Fecha efectiva",{exact:true}).locator("..").getByRole("button");
  const expected=new Intl.DateTimeFormat("es-CO",{day:"numeric",month:"long",year:"numeric",timeZone:"UTC"}).format(new Date(`${opening.effectiveOn}T12:00:00Z`));
  await expect(date).toHaveText(expected);await expect(date).toBeDisabled();
  for(const [index,document] of opening.documents.entries()){
    const number=capture.getByRole("region",{name:`Factura inicial de la partida ${index+1}`,exact:true}).getByLabel("Número de factura original");
    await expect(number).toHaveValue(document.number);await expect(number).toBeDisabled();
  }
  await expect(capture.getByRole("button",{name:"Guardar borrador",exact:true})).toBeDisabled();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
  await capture.getByRole("region",{name:"Factura inicial de la partida 1",exact:true}).screenshot({path:testInfo.outputPath("posted-opening-another-browser-mobile.png")});
});

test("cartera inicial real: muestra abonos, saldos y edades del cliente y proveedor en móvil", async ({ page }, testInfo) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(20_000);
  const { fixture, context } = await openingPortfolioContext(page);
  expect(fixture.Opening).toBeTruthy(); expect(fixture.Payments?.Receivable).toBeTruthy(); expect(fixture.Payments?.Payable).toBeTruthy();
  await page.setViewportSize({ width: 390, height: 844 });
  for (const kind of ["Receivable", "Payable"] as const) {
    const supplier = kind === "Payable", party = fixture.Opening![kind];
    const report = await context.scopedJson<{ items: Array<{ documentNumber: string; issuedAt: string; dueDate: string; originalAmount: number; outstandingAmount: number; paidAmount: number; ageDays?: number; daysPastDue?: number; notDueAmount: number; overdue1To30Amount: number }> }>("GET",
      `/api/commerce/v1/${supplier ? "payables" : "receivables"}/report?consolidated=false&templateVersion=4&dateRangeVersion=2&cutoff=${fixture.Opening!.effectiveOn}&${supplier ? "supplierId" : "customerId"}=${party.roleId}&page=1&pageSize=20`);
    expect(report.items).toHaveLength(2);
    expect(report.items.map(item => item.documentNumber).sort()).toEqual(fixture.Opening!.documents.filter(item => item.kind === kind).map(item => item.number).sort());
    expect(report.items.reduce((total, item) => total + item.paidAmount, 0)).toBe(200);
    expect(Number(report.items.reduce((total, item) => total + item.outstandingAmount, 0).toFixed(4))).toBe(1801.0004);
    for (const invoice of fixture.Opening!.documents.filter(item => item.kind === kind)) {
      const item = report.items.find(row => row.documentNumber === invoice.number)!;
      expect(item.issuedAt.slice(0, 10)).toBe(invoice.issuedOn);
      expect(item.dueDate.slice(0, 10)).toBe(invoice.dueOn);
      if (!supplier) expect(item.ageDays).toBe(60);
      expect(item.originalAmount).toBe(invoice.amount);
      expect(item.outstandingAmount).toBe(Number((item.originalAmount - item.paidAmount).toFixed(4)));
      if (invoice.dueOn < fixture.Opening!.effectiveOn) {
        if (!supplier) expect(item.daysPastDue).toBe(20);
        expect(item.overdue1To30Amount).toBe(item.outstandingAmount);
      } else expect(item.notDueAmount).toBe(item.outstandingAmount);
    }
    await page.goto(`/dashboard/${supplier ? "payables" : "receivables"}`);
    await page.getByRole("main").getByRole("button", { name: "Reportes", exact: true }).click();
    const viewer = page.getByRole("dialog").last();
    await viewer.getByRole("button", { name: /Detallado con saldo/ }).click();
    const selector = viewer.getByRole("combobox").first();
    await selector.click();
    await page.getByPlaceholder("Buscar por nombre o identificación…").fill(party.name);
    await page.getByRole("option").filter({ hasText: party.name }).click();
    for (const invoice of fixture.Opening!.documents.filter(item => item.kind === kind)) {
      const row = viewer.getByRole("row").filter({ hasText: invoice.number });
      const overdue = invoice.dueOn < fixture.Opening!.effectiveOn;
      const band = row.getByText(overdue ? "1–30 días" : "Sin vencer", { exact: true });
      await expect(band).toBeVisible();
      await expect(band).toHaveCSS("background-color", overdue ? "rgb(255, 241, 194)" : "rgb(221, 244, 232)");
    }
    await expect(viewer.getByText(/875,1254/).first()).toBeVisible();
    await expect(viewer.getByText(/925,875/).first()).toBeVisible();
    await closeOpeningNotificationHint(page);
    await viewer.getByText(fixture.Opening!.documents.find(item => item.kind === kind)!.number, { exact: true }).scrollIntoViewIfNeeded();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await viewer.screenshot({ path: testInfo.outputPath(`initial-${kind}-report-mobile.png`) });
    await viewer.getByRole("button", { name: "Cerrar", exact: true }).click();
  }
});

for(const area of ["receivables","payables"])test(`cartera existente DEV: reporte general ${area} en móvil`, async ({ page }, testInfo) => {
  test.setTimeout(180_000);
  page.setDefaultTimeout(20_000);
  test.skip(process.env.AURALY_E2E_TARGET !== "dev" || !!process.env.AURALY_E2E_DEV_OPENING_FIXTURE,
    "Requiere el contexto AURALY DEV original; esta prueba es de solo lectura.");
  const context = await realAccountingContext(page);
  const cutoff = proposedAccountingDate(context.timeZone).slice(0, 10);
  {
    const report = await context.scopedJson<ReceivablesReportPage|PayablesReportPage>("GET",
      `/api/commerce/v1/${area}/report?consolidated=true&templateVersion=4&dateRangeVersion=2&cutoff=${cutoff}&page=1&pageSize=20`);
    if("currencyTotals" in report)expect(report.currencyTotals.every(total=>Number.isFinite(total.outstandingAmount))).toBe(true);
    else expect(Number.isFinite(report.totalOutstanding)).toBe(true);
    await page.goto(`/dashboard/${area}`);
    const loading = page.waitForResponse(response => new URL(response.url()).pathname.endsWith(`/${area}/report`));
    await page.getByRole("main").getByRole("button", { name: "Reportes", exact: true }).click();
    const response = await loading;
    expect(response.ok(), await response.text()).toBe(true);
    const viewer = page.getByRole("dialog").last();
    await page.setViewportSize({ width: 390, height: 844 });
    await closeOpeningNotificationHint(page);
    const consolidated=page.waitForResponse(response=>new URL(response.url()).pathname.endsWith(`/${area}/report`));
    await viewer.getByRole("button",{name:"Consolidado general",exact:true}).click();
    const consolidatedResponse=await consolidated;expect(consolidatedResponse.ok(),await consolidatedResponse.text()).toBe(true);
    await expect.poll(()=>viewer.getByRole("row").count()).toBeGreaterThan(1);
    await viewer.getByRole("row").nth(1).scrollIntoViewIfNeeded();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await viewer.screenshot({ path: testInfo.outputPath(`existing-${area}-report-mobile.png`) });
    await viewer.getByRole("button", { name: "Cerrar", exact: true }).click();
  }
});
