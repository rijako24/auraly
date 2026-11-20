import { readFile, writeFile } from "node:fs/promises";
import { expect, test, type Page } from "@playwright/test";
import { realAccountingContext, type DevOpeningQaScope } from "./real-accounting-workspace";

export type OpeningPartyFixture = { partyId: string; roleId: string; name: string; accountId: string };
export type OpeningPortfolioFixture = {
  Label: string; TenantId: string; BusinessId: string; TenantKey: string; Username: string; UserId: string;
  ApiUrl: string; UiUrl: string; TimeZone: string;
  Opening?: { batchId: string; effectiveOn: string; cashAccountId: string; cashFormName: string;
    Receivable: OpeningPartyFixture; Payable: OpeningPartyFixture;
    documents: Array<{ kind: "Receivable" | "Payable"; number: string; amount: number; dueOn: string; issuedOn: string }> };
  Payments?: Record<string, { documentId: string; outstanding: number[] }>;
};

export async function readOpeningFixture() {
  const path = process.env.AURALY_E2E_DEV_OPENING_FIXTURE;
  test.skip(!path || process.env.AURALY_E2E_REAL_LOGIN !== "1", "Requiere la empresa QA de apertura preparada explícitamente en DEV.");
  const fixture = JSON.parse(await readFile(path!, "utf8")) as OpeningPortfolioFixture;
  expect(fixture.Label).toMatch(/^QA Apertura /);
  expect(fixture.TenantKey).toMatch(/^@qa-apertura-/);
  expect(fixture.ApiUrl).toBe("https://api-auraly-dev-w5usmo6w.azurewebsites.net");
  expect(fixture.UiUrl).toBe("https://proud-moss-0d9cf540f.7.azurestaticapps.net");
  return { fixture, path: path! };
}

export function openingQaScope(fixture: OpeningPortfolioFixture): DevOpeningQaScope {
  return { purpose: "OpeningBalanceQa", tenantId: fixture.TenantId, businessId: fixture.BusinessId, tenantKey: fixture.TenantKey };
}

export async function openingPortfolioContext(page: Page) {
  const { fixture, path } = await readOpeningFixture();
  // Exercise the real UI with notifications explicitly denied in the browser;
  // do not register a push subscription for this temporary QA company.
  const browser = await page.context().newCDPSession(page);
  await browser.send("Browser.setPermission", { permission: { name: "notifications" }, setting: "denied", origin: fixture.UiUrl });
  await browser.detach();
  const context = await realAccountingContext(page, fixture.BusinessId, undefined, openingQaScope(fixture));
  const save = () => writeFile(path, JSON.stringify(fixture, null, 2), "utf8");
  return { fixture, path, context, save };
}
