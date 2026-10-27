import { randomInt, randomUUID } from "node:crypto";
import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import type { AccountingAccount, AccountingEntry, AccountingOpeningBalance, AccountingOpenPositionPage } from "../src/services/api/accounting";
import { realAccountingContext } from "./support/real-accounting-workspace";

test("apertura real vincula la deuda histórica desde la vista sin volver a crearla", async ({ page }) => {
  test.setTimeout(120_000);
  test.skip(!process.env.AURALY_E2E_OPENING_FIXTURE, "Requiere la preparación explícita de una obligación histórica en la base local aislada.");
  const fixture = JSON.parse(await readFile(process.env.AURALY_E2E_OPENING_FIXTURE!, "utf8")) as {
    businessId: string; partyId: string; partyName: string; payableId: string; documentNumber: string;
    effectiveOn: string; outstandingAmount: number;
  };
  const { scopedJson, businessId } = await realAccountingContext(page);
  expect(fixture.businessId).toBe(businessId);
  const base = "/api/commerce/v1/accounting";
  const accountId = randomUUID(), parentId = randomUUID(), code = `29${randomInt(1000, 10000)}`;
  await scopedJson("POST", `${base}/accounts`, { accountId: parentId, tenantId: process.env.AURALY_E2E_TENANT_ID,
    code, name: "Cartera histórica E2E", accountType: "Liability", allowsPosting: false, requiresParty: false });
  await scopedJson("POST", `${base}/accounts`, { accountId, tenantId: process.env.AURALY_E2E_TENANT_ID,
    code: `${code}01`, name: "Obligación conciliada E2E", accountType: "Liability", allowsPosting: true, requiresParty: true,
    semantics: { parentAccountId: parentId, financialFunction: "Payable", balanceSide: "Credit", businessId, bankAccountId: null, paymentMethodOptionId: null } });
  const accounts = await scopedJson<AccountingAccount[]>("GET", `${base}/accounts`);
  const equity = accounts.find(account => account.accountType === "Equity" && account.allowsPosting && account.isActive)!;
  expect(equity).toBeTruthy();
  await page.goto("/dashboard/accounting");
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  const rows = page.locator("tbody tr");
  await rows.nth(0).getByRole("combobox").first().click();
  await page.getByRole("option", { name: `${code}01 · Obligación conciliada E2E`, exact: true }).click();
  await rows.nth(0).getByRole("combobox", { name: "Seleccionar tercero", exact: true }).click();
  await page.getByPlaceholder("Buscar por nombre o identificación…").fill(fixture.partyName);
  await page.getByRole("option").filter({ hasText: fixture.partyName }).click();
  await page.getByRole("combobox", { name: "Vincular obligación anterior en partida 1", exact: true }).click();
  await page.getByPlaceholder("Buscar factura u obligación…").fill(fixture.documentNumber);
  await page.getByRole("option").filter({ hasText: fixture.documentNumber }).click();
  await expect(rows.nth(0).getByRole("spinbutton").nth(1)).toHaveValue(String(fixture.outstandingAmount));
  await rows.nth(1).getByRole("combobox").first().click();
  await page.getByRole("option", { name: `${equity.code} · ${equity.name}`, exact: true }).click();
  await rows.nth(1).getByRole("spinbutton").first().fill(String(fixture.outstandingAmount));
  const saving = page.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname.endsWith("/opening-balances"));
  await page.getByRole("button", { name: "Guardar borrador", exact: true }).click();
  const savedResponse = await saving;
  expect(savedResponse.ok(), await savedResponse.text()).toBe(true);
  const saved = await savedResponse.json() as AccountingOpeningBalance;
  expect(saved.effectiveOn).toBe(fixture.effectiveOn);
  expect(saved.lines[0].existingPosition).toEqual({ subledgerKind: "Payable", subledgerId: fixture.payableId });
  expect(saved.lines[0].credit).toBe(fixture.outstandingAmount);
  await page.reload();
  await page.getByRole("button", { name: "Saldos iniciales", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Vincular obligación anterior en partida 1", exact: true })).toContainText(fixture.documentNumber);
  await page.getByRole("button", { name: "Aprobar saldos", exact: true }).click();
  await expect(page.getByText("Aprobado", { exact: true })).toBeVisible();
  await page.getByText("Cómo iniciar", { exact: true }).locator("..").getByRole("combobox").click();
  await page.getByRole("option", { name: "Usar saldos aprobados", exact: true }).click();
  const activating = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/accounting/activate"));
  await page.getByRole("button", { name: "Activar contabilidad", exact: true }).click();
  const activated = await activating;
  expect(activated.ok(), await activated.text()).toBe(true);
  expect((await activated.json()).status).toBe("Ready");
  await expect(page.getByText("Contabilizado", { exact: true })).toBeVisible();
  const entry = await scopedJson<AccountingEntry>("GET", `${base}/entries/by-document/${saved.batchId}`);
  expect(entry.creditTotal).toBe(fixture.outstandingAmount);
  expect(entry.lines.find(line => line.accountId === accountId)).toMatchObject({ partyId: fixture.partyId, debit: 0, credit: fixture.outstandingAmount });
  const positions = await scopedJson<AccountingOpenPositionPage>("GET", `${base}/open-positions?subledgerKind=Payable&subledgerId=${fixture.payableId}&page=1&pageSize=1`);
  expect(positions.items).toHaveLength(1);
  expect(positions.items[0]).toMatchObject({ originalAmount: fixture.outstandingAmount + 500, outstandingAmount: fixture.outstandingAmount,
    openingEntryId: entry.entryId, accountId });
  await page.screenshot({ path: "../work/apertura-conciliada-real.png", fullPage: true });
});
