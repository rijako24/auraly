import { readFile, writeFile } from "node:fs/promises";
import { expect, test, type Page } from "@playwright/test";
import { realAccountingContext, type DevOpeningQaScope } from "./real-accounting-workspace";

export type OpeningPartyFixture = { partyId: string; roleId: string; name: string; accountId: string };
export type OpeningPortfolioFixture = {
  Label: string; TenantId: string; BusinessId: string; TenantKey: string; Username: string; UserId: string;
  ApiUrl: string; UiUrl: string; TimeZone: string; Database?: string;
  Opening?: { batchId: string; effectiveOn: string; cashAccountId: string; cashFormName: string;
    Receivable: OpeningPartyFixture; Payable: OpeningPartyFixture;
    documents: Array<{ kind: "Receivable" | "Payable"; number: string; amount: number; dueOn: string; issuedOn: string }> };
  PreparedOpening?: NonNullable<OpeningPortfolioFixture["Opening"]>;
  Payments?: Record<string, { documentId: string; outstanding: number[] }>;
};

export async function readOpeningFixture() {
  const path = process.env.AURALY_E2E_DEV_OPENING_FIXTURE;
  test.skip(!path || process.env.AURALY_E2E_REAL_LOGIN !== "1", "Requiere la empresa QA de apertura preparada explícitamente en DEV.");
  const fixture = JSON.parse(await readFile(path!, "utf8")) as OpeningPortfolioFixture;
  expect(fixture.Label).toMatch(/^QA Apertura /);
  if (process.env.AURALY_E2E_TARGET === "dev") {
    expect(fixture.TenantKey).toMatch(/^@qa-apertura-/);
    expect(fixture.ApiUrl).toBe("https://api-auraly-dev-w5usmo6w.azurewebsites.net");
    expect(fixture.UiUrl).toBe("https://proud-moss-0d9cf540f.7.azurestaticapps.net");
  } else {
    expect(fixture.ApiUrl).toBe("http://127.0.0.1:5097");
    expect(fixture.UiUrl).toBe("http://127.0.0.1:3000");
    expect(fixture.Database).toBe("AuralyOpeningQa20261119");
  }
  expect(process.env.AURALY_API_URL).toBe(fixture.ApiUrl);
  expect(test.info().project.use.baseURL).toBe(fixture.UiUrl);
  return { fixture, path: path! };
}

export function openingQaScope(fixture: OpeningPortfolioFixture): DevOpeningQaScope {
  return { purpose: "OpeningBalanceQa", tenantId: fixture.TenantId, businessId: fixture.BusinessId, tenantKey: fixture.TenantKey };
}

export async function closeOpeningNotificationHint(page: Page) {
  const close = page.getByRole("button", { name: "Cerrar aviso de notificaciones", exact: true });
  if (await close.isVisible()) await close.click();
}

export async function openingPortfolioContext(page: Page) {
  const { fixture, path } = await readOpeningFixture();
  const context = await realAccountingContext(page, fixture.BusinessId, undefined, openingQaScope(fixture));
  const save = () => writeFile(path, JSON.stringify(fixture, null, 2), "utf8");
  return { fixture, path, context, save };
}
