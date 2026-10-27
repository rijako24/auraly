import { expect, test } from "@playwright/test";
import { financialVoucherScenario, installFinancialVoucherContext } from "./support/financial-voucher";

test("reintenta el mismo comprobante sin duplicarlo cuando se pierde la primera respuesta", async ({ page, baseURL }) => {
  const scenario = financialVoucherScenario();
  const { documentId } = scenario;
  let draft = scenario.draft;
  let reads = 0;
  const attempts: Array<{ path: string; body: { rowVersion: string } }> = [];
  await installFinancialVoucherContext(page, baseURL!, scenario);
  await page.route("**/api/**", async route => {
    const request = route.request(), path = new URL(request.url()).pathname;
    const endpoint = `/accounting/manual/drafts/${documentId}`;
    if (path.endsWith(endpoint) && request.method() === "GET") {
      reads++;
      await route.fulfill({ status: 200, json: draft });
    } else if (path.endsWith(endpoint + "/send") && request.method() === "POST") {
      attempts.push({ path, body: request.postDataJSON() as { rowVersion: string } });
      draft = { ...draft, sentAt: "2026-10-23T12:01:00+00:00", status: "Pending", canEdit: false, canSend: false,
        rowVersion: "AAAAAAAAAAI=", row: { ...draft.row, status: "Pending" } };
      if (attempts.length === 1) {
        await route.fulfill({ status: 503, contentType: "application/problem+json", json: { detail: "Se perdió la respuesta del mismo comprobante." } });
      } else await route.fulfill({ status: 200, json: draft });
    } else await route.fallback();
  });
  await page.goto(`/dashboard/financial-traceability?document=${documentId}`);
  const send = page.getByRole("button", { name: "Contabilizar", exact: true });
  await send.click();
  await expect(page.getByRole("alert").filter({ hasText: "Se perdió la respuesta" })).toBeVisible();
  await expect(page.getByText("Documento enviado: contenido inmutable.")).toHaveCount(0);
  expect(attempts).toHaveLength(1);
  await send.click();
  await expect(page.getByText("Documento enviado: contenido inmutable.")).toBeVisible();
  await expect(page.getByLabel("Descripción", { exact: true })).toBeDisabled();
  expect(attempts).toHaveLength(2);
  expect(attempts[1]).toEqual(attempts[0]);
  expect(attempts[0].body).toEqual({ rowVersion: scenario.draft.rowVersion });
  expect(reads).toBe(1);
});
