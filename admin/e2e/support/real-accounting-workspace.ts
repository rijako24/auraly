import { expect, test, type Page } from "@playwright/test";
import { randomUUID } from "node:crypto";
import type { CityItem, CreateThirdPartyRequest, DivisionItem, GeographyItem } from "../../src/services/api/parties";
import { proposedAccountingDate } from "../../src/lib/accounting-document-date";
import { login } from "./auth";
import type { AccountingAccount } from "../../src/services/api/accounting";

// Test synchronization for the real cloud worker, not application polling.
// A finite observation window checks the durable outcome after acceptance.
export async function waitForRealPosting(context: Awaited<ReturnType<typeof realAccountingContext>>, documentId: string) {
  await expect.poll(async () => {
    const result = await context.scopedResponse("GET", `/api/commerce/v1/accounting/postings/by-document/${documentId}`);
    // Operational acceptance precedes creation of the financial source.
    if (result.status === 404) return "AwaitingOperationalProcessing";
    expect(result.ok, JSON.stringify(result)).toBe(true);
    const posting = result.body as { status: string; errorCode?: string; errorMessage?: string };
    expect(["AccountingPendingConfiguration", "DeadLettered", "Failed"], JSON.stringify(posting)).not.toContain(posting.status);
    return posting.status;
  }, { timeout: 30_000, intervals: [500, 1_000, 2_000, 5_000], message: "El documento aceptado debe terminar contabilizado una sola vez" }).toBe("Posted");
}

export async function realAccountingContext(page: Page, businessIdOverride?: string) {
  test.skip(process.env.AURALY_E2E_REAL_LOGIN !== "1", "Requiere el entorno de pruebas autorizado.");
  const tenantId = process.env.AURALY_E2E_TENANT_ID;
  const businessId = businessIdOverride ?? process.env.AURALY_E2E_BUSINESS_ID;
  if(process.env.AURALY_E2E_TARGET === "dev") {
    expect(test.info().project.use.baseURL).toBe("https://proud-moss-0d9cf540f.7.azurestaticapps.net");
    expect(process.env.AURALY_API_URL).toBe("https://api-auraly-dev-w5usmo6w.azurewebsites.net");
    expect(tenantId).toBe("a0a10000-0000-0000-0000-000000000000");
    expect(businessId).toBe("a0a10000-0000-0000-0000-000000000001");
  } else {
    for (const address of [test.info().project.use.baseURL, process.env.AURALY_API_URL]) {
      expect(address, "Esta prueba con escrituras requiere URLs locales explícitas.").toBeTruthy();
      expect(["127.0.0.1", "localhost"]).toContain(new URL(address!).hostname);
    }
  }
  expect(tenantId, "Indica el tenant de la base aislada en AURALY_E2E_TENANT_ID.").toBeTruthy();
  expect(businessId, "Indica la sede de la base aislada en AURALY_E2E_BUSINESS_ID.").toBeTruthy();
  const headers = { "X-Tenant-Id": tenantId!, "X-Business-Id": businessId! };
  await login(page, undefined, 60_000);
  await page.addInitScript(({ tenantId, businessId }) => {
    localStorage.setItem("selected_tenant_id", tenantId!);
    localStorage.setItem("selected_business_id", businessId!);
    localStorage.setItem("auraly:pwa-install-dismissed", "1");
  }, { tenantId, businessId });
  async function scopedResponse(method: string, path: string, data?: unknown, extraHeaders: Record<string, string> = {}) {
    return page.evaluate(async ({ method, path, headers, data, extraHeaders }) => {
      const response = await fetch(path, { method, headers: { ...headers, ...extraHeaders, "Content-Type": "application/json" },
        credentials: "include", body: data === undefined ? undefined : JSON.stringify(data) });
      const text = await response.text();
      return { ok: response.ok, status: response.status, body: text ? JSON.parse(text) : null };
    }, { method, path, headers, data, extraHeaders });
  }
  async function scopedJson<T>(method: string, path: string, data?: unknown, extraHeaders: Record<string, string> = {}): Promise<T> {
    const result = await scopedResponse(method, path, data, extraHeaders);
    expect(result.ok, `${method} ${path}: ${result.status} ${JSON.stringify(result.body)}`).toBe(true);
    return result.body as T;
  }

  const businesses = await scopedJson<Array<{ businessId: string; timeZone: string }>>(
    "GET", "/api/execution-context/businesses");
  const business = businesses.find(item => item.businessId === businessId);
  expect(business?.timeZone, "El contexto autorizado debe incluir la zona del negocio de pruebas.").toBeTruthy();
  return { scopedJson, scopedResponse, businessId: businessId!, timeZone: business!.timeZone };
}

export async function realAccountingWorkspace(page: Page) {
  const context = await realAccountingContext(page);
  const { scopedJson } = context;
  await scopedJson("PUT", "/api/commerce/v1/accounting/defaults", {});
  const activation = await scopedJson<{ status: string }>("POST", "/api/commerce/v1/accounting/activate", {
    effectiveFrom: `${proposedAccountingDate(context.timeZone).slice(0, 4)}-01-01`,
    functionalCurrencyCode: "COP", openingBalanceMode: "ZeroDeclared",
  });
  expect(activation.status).toBe("Ready");
  return context;
}

export async function realAccountingAuxiliary(
  { scopedJson }: Awaited<ReturnType<typeof realAccountingWorkspace>>,
  prefix: string, accountType: string, financialFunction: string, balanceSide: "Debit" | "Credit",
  bankAccountId: string | null = null, requiresParty = false,
) {
  const path = "/api/commerce/v1/accounting/accounts";
  const parentId = randomUUID();
  const parentCode = prefix + BigInt(`0x${parentId.replaceAll("-", "").slice(0, 12)}`).toString();
  await scopedJson("POST", path, { accountId: parentId, tenantId: process.env.AURALY_E2E_TENANT_ID,
    code: parentCode, name: "Agrupación contable E2E", accountType, allowsPosting: false, requiresParty: false });
  return scopedJson<AccountingAccount>("POST", path, { accountId: randomUUID(), tenantId: process.env.AURALY_E2E_TENANT_ID,
    code: `${parentCode}01`, name: "Auxiliar contable E2E", accountType, allowsPosting: true, requiresParty,
    semantics: { parentAccountId: parentId, financialFunction, balanceSide, businessId: null, bankAccountId, paymentMethodOptionId: null } });
}

export async function realPartyRequest(
  { scopedJson, businessId }: Awaited<ReturnType<typeof realAccountingWorkspace>>,
  displayName: string,
): Promise<CreateThirdPartyRequest> {
  const api = "/api/commerce/v1/masters/geography";
  const country = (await scopedJson<GeographyItem[]>("GET", `${api}/countries`)).find(item => item.code === "CO")!;
  expect(country).toBeTruthy();
  const division = (await scopedJson<DivisionItem[]>("GET", `${api}/countries/${country.countryId}/divisions`)).find(item => item.code === "11")!;
  expect(division).toBeTruthy();
  const city = (await scopedJson<CityItem[]>("GET", `${api}/divisions/${division.administrativeDivisionId}/cities`)).find(item => item.code === "11001")!;
  expect(city).toBeTruthy();
  return {
    operationId: randomUUID(), businessId,
    party: { partyType: "Organization", identificationCountryId: country.countryId,
      identificationTypeCode: "31", identification: String(800000000 + Math.floor(Math.random() * 100000000)),
      verificationDigit: null, displayName, legalName: displayName, firstName: null, lastName: null, email: null, phone: null },
    primarySite: { code: "PRINCIPAL", name: "Sede de prueba", countryId: country.countryId,
      administrativeDivisionId: division.administrativeDivisionId, cityId: city.cityId, addressLine: "Dirección de prueba local",
      neighborhood: null, postalCode: null, email: null, phone: null, isPrimary: true,
      googleMapsUrl: null, googlePlaceId: null, latitude: null, longitude: null },
  };
}
