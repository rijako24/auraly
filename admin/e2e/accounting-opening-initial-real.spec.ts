import { expect, test, type Locator, type Page } from "@playwright/test";
import { randomUUID } from "node:crypto";
import type { AccountingAccount, AccountingEntry, AccountingOpeningBalance, AccountingOpenPositionPage } from "../src/services/api/accounting";
import type { PaymentMethodPage } from "../src/services/api/payment-methods";
import { addCalendarDays, proposedAccountingDate } from "../src/lib/accounting-document-date";
import { openingPortfolioContext, type OpeningPartyFixture } from "./support/opening-portfolio-fixture";
import { realPartyRequest, waitForRealPosting } from "./support/real-accounting-workspace";

test("empresa QA de apertura: navegador, contexto propio y configuración inicial", async ({ page }, testInfo) => {
  test.setTimeout(120_000);
  const { fixture, context } = await openingPortfolioContext(page);
  const readiness = await context.scopedJson<{ status: string; canEditOpeningBalances: boolean; blockingIssues: unknown[] }>("GET",
    `/api/commerce/v1/accounting/readiness?effectiveFrom=${proposedAccountingDate(context.timeZone).slice(0, 10)}&openingBalanceMode=ImportedAndApproved`);
  expect(readiness.canEditOpeningBalances).toBe(true);
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  await page.setViewportSize({ width: 390, height: 844 });
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

test("apertura inicial real: captura facturas de cliente y proveedor desde escritorio y móvil", async ({ page }, testInfo) => {
  test.setTimeout(300_000);
  const { fixture, context, save } = await openingPortfolioContext(page);
  test.skip(!!fixture.Opening, "La apertura de esta empresa QA ya fue creada; conservar su evidencia y ejecutar pagos/reportes.");
  const { scopedJson } = context, api = "/api/commerce/v1";
  const accounts = await scopedJson<AccountingAccount[]>("GET", `${api}/accounting/accounts`);
  const accountFor = (kind: string) => accounts.find(account => account.isActive && account.allowsPosting && account.semantics?.financialFunction === kind)!;
  const receivable = accountFor("Receivable"), payable = accountFor("Payable");
  expect(receivable).toBeTruthy(); expect(payable).toBeTruthy();
  const forms = await scopedJson<PaymentMethodPage>("GET", `${api}/payment-methods?page=1&pageSize=50`);
  const cashForms = forms.items.filter(form => form.isActive && form.methodCode === "Cash" && form.accountId);
  expect(cashForms).toHaveLength(1);
  const cash = accounts.find(account => account.accountId === cashForms[0].accountId)!;
  const parties: Record<string, OpeningPartyFixture> = {};
  for (const kind of ["Receivable", "Payable"] as const) {
    const supplier = kind === "Payable", name = `${fixture.Label} ${supplier ? "Proveedor" : "Cliente"}`;
    const party = await scopedJson<{ partyId: string; customerId?: string; supplierId?: string }>("POST", `${api}/${supplier ? "suppliers" : "customers"}`, {
      ...await realPartyRequest(context, name), ...(supplier ? { purchaseEvidencePolicy: "InternalReceiptVoucher", defaultPaymentDueDays: 30 } : {}),
    });
    parties[kind] = { partyId: party.partyId, roleId: (supplier ? party.supplierId : party.customerId)!, name, accountId: supplier ? payable.accountId : receivable.accountId };
  }
  const effectiveOn = proposedAccountingDate(context.timeZone).slice(0, 10), issuedOn = addCalendarDays(effectiveOn, -60), prefix = randomUUID().slice(0, 8);
  const documents = (["Receivable", "Payable"] as const).flatMap(kind => [0, 1].map(index => ({ kind,
    number: `INI-${kind === "Receivable" ? "CXC" : "CXP"}-${prefix}-${index + 1}`, amount: index === 0 ? 1000.1254 : 1000.875,
    issuedOn, dueOn: addCalendarDays(effectiveOn, index === 0 ? -20 : 15) })));
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  const capture = page.getByRole("region", { name: "Saldos iniciales de la empresa", exact: true });
  await capture.locator('input[type="date"]').fill(effectiveOn);
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
  const saving = page.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname.endsWith("/opening-balances"));
  await capture.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  const savedResponse = await saving; expect(savedResponse.ok(), await savedResponse.text()).toBe(true);
  const saved = await savedResponse.json() as AccountingOpeningBalance;
  expect(saved.lines).toHaveLength(4);
  expect(saved.lines.map(line => line.newInvoice?.documentNumber)).toEqual(documents.map(document => document.number));
  expect(saved.debitTotal).toBe(2001.0004); expect(saved.creditTotal).toBe(2001.0004);
  await capture.screenshot({ path: testInfo.outputPath("initial-opening-desktop.png") });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await capture.evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
  await capture.getByRole("region", { name: "Factura inicial de la partida 1", exact: true }).screenshot({ path: testInfo.outputPath("initial-invoice-mobile.png") });
  await expect(capture.getByRole("textbox", { name: "Débito de apertura 1", exact: true })).toHaveValue(/1[\s\u00a0]000\.1254/);
  await capture.getByRole("button", { name: "Aprobar saldos", exact: true }).click();
  await page.getByText("Cómo iniciar", { exact: true }).locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: "Usar saldos aprobados", exact: true }).click();
  const activating = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/accounting/activate"));
  await page.getByRole("button", { name: "Activar contabilidad", exact: true }).click();
  const activated = await activating; expect(activated.ok(), await activated.text()).toBe(true);
  expect((await activated.json()).status).toBe("Ready");
  await waitForRealPosting(context, saved.batchId);
  const entry = await scopedJson<AccountingEntry>("GET", `${api}/accounting/entries/by-document/${saved.batchId}`);
  expect(entry.debitTotal).toBe(2001.0004); expect(entry.creditTotal).toBe(2001.0004);
  for (const kind of ["Receivable", "Payable"] as const) {
    const positions = await scopedJson<AccountingOpenPositionPage>("GET", `${api}/accounting/open-positions?subledgerKind=${kind}&counterpartyRoleId=${parties[kind].roleId}&page=1&pageSize=10`);
    expect(positions.items).toHaveLength(2);
    expect(positions.items.every(position => position.openingEntryId === entry.entryId)).toBe(true);
    expect(positions.items.map(position => position.documentNumber).sort()).toEqual(documents.filter(document => document.kind === kind).map(document => document.number).sort());
  }
  fixture.Opening = { batchId: saved.batchId, effectiveOn, cashAccountId: cash.accountId, cashFormName: cashForms[0].name,
    Receivable: parties.Receivable, Payable: parties.Payable, documents };
  await save();
});

test("cartera inicial real: muestra abonos, saldos y edades del cliente y proveedor en móvil", async ({ page }, testInfo) => {
  test.setTimeout(180_000);
  const { fixture, context } = await openingPortfolioContext(page);
  expect(fixture.Opening).toBeTruthy(); expect(fixture.Payments?.Receivable).toBeTruthy(); expect(fixture.Payments?.Payable).toBeTruthy();
  await page.setViewportSize({ width: 390, height: 844 });
  for (const kind of ["Receivable", "Payable"] as const) {
    const supplier = kind === "Payable", party = fixture.Opening![kind];
    const report = await context.scopedJson<{ items: Array<{ documentNumber: string; originalAmount: number; outstandingAmount: number; paidAmount: number; ageDays: number; daysPastDue: number; notDueAmount: number; overdue1To30Amount: number }> }>("GET",
      `/api/commerce/v1/${supplier ? "payables" : "receivables"}/report?consolidated=false&templateVersion=4&dateRangeVersion=2&cutoff=${fixture.Opening!.effectiveOn}&${supplier ? "supplierId" : "customerId"}=${party.roleId}&page=1&pageSize=20`);
    expect(report.items).toHaveLength(2);
    expect(report.items.map(item => item.documentNumber).sort()).toEqual(fixture.Opening!.documents.filter(item => item.kind === kind).map(item => item.number).sort());
    expect(report.items.reduce((total, item) => total + item.paidAmount, 0)).toBe(200);
    expect(Number(report.items.reduce((total, item) => total + item.outstandingAmount, 0).toFixed(4))).toBe(1801.0004);
    for (const invoice of fixture.Opening!.documents.filter(item => item.kind === kind)) {
      const item = report.items.find(row => row.documentNumber === invoice.number)!;
      expect(item.ageDays).toBe(60);
      expect(item.originalAmount).toBe(invoice.amount);
      expect(item.outstandingAmount).toBe(Number((item.originalAmount - item.paidAmount).toFixed(4)));
      if (invoice.dueOn < fixture.Opening!.effectiveOn) {
        expect(item.daysPastDue).toBe(20);
        expect(item.overdue1To30Amount).toBe(item.outstandingAmount);
      } else expect(item.notDueAmount).toBe(item.outstandingAmount);
    }
    await page.goto(`/dashboard/${supplier ? "payables" : "receivables"}`);
    await page.getByRole("button", { name: "Reportes", exact: true }).click();
    const viewer = page.getByRole("dialog").last();
    await viewer.getByRole("button", { name: /Detallado con saldo/ }).click();
    const selector = viewer.getByRole("combobox").first();
    await selector.click();
    await page.getByPlaceholder("Buscar por nombre o identificación…").fill(party.name);
    await page.getByRole("option").filter({ hasText: party.name }).click();
    for (const invoice of fixture.Opening!.documents.filter(item => item.kind === kind)) await expect(viewer.getByText(invoice.number, { exact: true }).first()).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await viewer.screenshot({ path: testInfo.outputPath(`initial-${kind}-report-mobile.png`) });
    await viewer.getByRole("button", { name: "Cerrar", exact: true }).click();
  }
});
