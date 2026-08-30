import { expect, test, type Page } from "@playwright/test";

const tenantId = "11111111-1111-1111-1111-111111111111";
const businessId = "22222222-2222-2222-2222-222222222222";
const permissions = ["dashboard.read", "fiscal.configuration.read", "fiscal.configuration.manage"];

async function authenticate(page: Page) {
  await page.context().addCookies([{ name: "auth_token", value: "e2e", url: process.env.AURALY_E2E_BASE_URL ?? "http://127.0.0.1:3000", httpOnly: true, sameSite: "Lax" }]);
  await page.addInitScript(({ tenant, business, granted }) => {
    localStorage.setItem("selected_tenant_id", tenant);
    localStorage.setItem("selected_business_id", business);
    localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user: { userId: "33333333-3333-3333-3333-333333333333", tenantId: tenant, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions: granted } }, version: 0 }));
  }, { tenant: tenantId, business: businessId, granted: permissions });
}

test("facturación electrónica usa la asignación canónica por emisor y orienta si falta el perfil tributario", async ({ page }) => {
  let requestedDeviceNumbering = false;
  await page.route("**/api/auth/me", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "AURALY", username: "e2e", email: "e2e@auraly.test", firstName: "Prueba", lastName: "E2E", avatarUrl: null, roles: ["Administrator"], permissions }) }));
  await page.route("**/api/execution-context/tenants", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([{ tenantId, name: "Auraly" }]) }));
  await page.route("**/api/execution-context/businesses", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([{ tenantId, businessId, name: "Auraly" }]) }));
  await page.route("**/api/execution-context/access", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ tenantId, businessId, roles: ["Administrator"], permissions }) }));
  await page.route("**/api/commerce/v1/fiscal/configuration/devices**", async (route) => { requestedDeviceNumbering = true; await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ businessId, availableConsecutives: 0, availableResolutions: [], onlineAssignment: null, expirationWarningDays: 3, numberingWarningRemaining: 100, devices: [] }) }); });
  await page.route("**/api/commerce/v1/fiscal/configuration/onboarding**", async (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ businessId, businessName: "Auraly", legalName: "Auraly", supplierTaxId: "", supplierCheckDigit: "", stage: "NotConfigured", softwareIdentificationCode: null, testSetId: null, hasCertificate: false, certificateThumbprintSuffix: null, certificateValidFrom: null, certificateValidTo: null, habilitationAccepted: false, habilitationAcceptedAt: null, productionActive: false, assignedRange: null, availableRanges: [], missingRequirements: ["PerfilLegal", "SoftwareId", "TestSetId", "Certificado"] }) }));

  await authenticate(page);
  await page.goto("/dashboard/settings/fiscal");
  await expect(page.getByRole("heading", { name: /Activación de facturación electrónica/ })).toBeVisible();
  await expect(page.getByText("Completa primero el perfil tributario de la empresa")).toBeVisible();
  await expect(page.getByText("Numeración por equipo enrolado")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Resoluciones por emisor" })).toBeVisible();
  expect(requestedDeviceNumbering).toBe(true);
});
