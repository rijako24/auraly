import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";
import { realAccountingWorkspace, selectRealCashForm, waitForRealPosting } from "./support/real-accounting-workspace";
import { createRealProduct } from "./support/real-product";
import type { AccountingCostCenter, AccountingEntry } from "../src/services/api/accounting";
import type { SalesWorkspaceOption } from "../src/services/pos/online-pos-client";

test.use({ actionTimeout: 30_000, serviceWorkers: "block" });

test("POS real: factura con centro explícito y automático sin duplicar el asiento", async ({ page }) => {
  test.setTimeout(240_000);
  const context = await realAccountingWorkspace(page);
  const { scopedJson, businessId } = context;
  const center = await scopedJson<AccountingCostCenter>("POST", "/api/commerce/v1/accounting/cost-centers", {
    costCenterId: randomUUID(), tenantId: process.env.AURALY_E2E_TENANT_ID,
    code: `QA-CAJA-${randomUUID().slice(0, 8)}`, name: "Caja de prueba de facturación con centro seleccionado",
    parentCostCenterId: null, isDefault: false,
  });
  const productName = await createRealProduct(page, false);
  const workspace = (await scopedJson<SalesWorkspaceOption[]>("GET", "/api/commerce/v1/pos/workspace/options"))
    .find(row => row.businessId === businessId && row.pointOfSaleMode === "DirectSale");
  expect(workspace).toBeTruthy();
  const documents: Array<{ documentId: string; costCenterId: string | null; entryId: string }> = [];

  for (const explicit of [true, false]) {
    await page.setViewportSize({ width: explicit ? 390 : 1366, height: explicit ? 844 : 768 });
    await page.goto("/pos?workspace=change");
    await expect(page.getByRole("button", { name: "Continuar a ventas", exact: true })).toBeVisible();
    const business = page.getByText("Sede", { exact: true }).locator("..").getByRole("combobox");
    if (await business.count()) {
      await business.click();
      await page.getByRole("option", { name: workspace!.businessName, exact: true }).click();
    }
    const warehouse = page.getByText("Bodega", { exact: true }).locator("..").getByRole("combobox");
    if (await warehouse.count()) {
      await warehouse.click();
      await page.getByRole("option", { name: `${workspace!.warehouseCode} · ${workspace!.warehouseName}`, exact: true }).click();
    }
    const picker = page.getByRole("combobox", { name: "Centro de costo de facturación", exact: true });
    await picker.click();
    if (explicit) {
      await page.getByPlaceholder("Buscar centro por código o nombre…").fill(center.code);
      await page.getByRole("option", { name: `${center.code} · ${center.name}`, exact: true }).click();
    } else {
      await page.getByRole("option", { name: "Automático", exact: true }).click();
    }
    await page.getByRole("button", { name: /Comprobante de venta/ }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: test.info().outputPath(`setup-${explicit ? "selected-mobile" : "automatic-desktop"}.png`), fullPage: true, animations: "disabled" });
    const active = page.waitForResponse(response => response.url().includes("/pos/drafts/active") && response.ok());
    await page.getByRole("button", { name: "Continuar a ventas", exact: true }).click();
    const draft = await (await active).json() as { lines: unknown[] };
    expect(draft.lines, "La venta activa de esta cuenta QA debe estar vacía antes de probar.").toHaveLength(0);
    const scanner = page.locator("#pos-scanner");
    await expect(scanner).toBeEnabled();
    // The mobile setup is exercised above; use the full POS keyboard for issuance.
    await page.setViewportSize({ width: 1366, height: 768 });
    await page.keyboard.press("F1");
    const search = page.getByRole("dialog").filter({ hasText: "Buscar producto" });
    await search.getByRole("combobox", { name: "Buscar producto", exact: true }).fill(productName);
    const option = search.getByRole("option").first();
    await expect(option).toContainText(productName);
    const code = (await option.innerText()).split(/\s+/).find(value => value.startsWith("PRD-"));
    expect(code).toBeTruthy();
    await page.keyboard.press("Escape");
    await scanner.fill(code!);
    const capture = page.waitForResponse(response => response.request().method() === "POST" && /\/pos\/drafts\/[^/]+\/items$/.test(new URL(response.url()).pathname));
    await scanner.press("Enter");
    expect((await capture).ok()).toBe(true);
    await expect(page.getByRole("spinbutton", { name: /Cantidad de/ })).toHaveCount(1);
    await page.keyboard.press("F8");
    const payment = page.getByRole("dialog", { name: "Finalizar venta", exact: true });
    await selectRealCashForm(page, payment);
    const accepted = page.waitForResponse(response => response.request().method() === "POST" && /\/pos\/drafts\/[^/]+\/complete$/.test(new URL(response.url()).pathname));
    await payment.getByRole("button", { name: /Emitir e imprimir/ }).click();
    const response = await accepted;
    expect(response.ok(), await response.text()).toBe(true);
    const request = response.request().postDataJSON();
    expect(request.costCenterId ?? null).toBe(explicit ? center.costCenterId : null);
    const result = await response.json() as { receipt: { documentId: string } };
    const documentId = result.receipt.documentId;
    await waitForRealPosting(context, documentId);
    const entry = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${documentId}`);
    expect(entry.debitTotal).toBeGreaterThan(0);
    expect(entry.debitTotal).toBe(entry.creditTotal);
    expect(entry.lines.every(line => explicit ? line.costCenterId === center.costCenterId : !!line.costCenterId)).toBe(true);
    const replay = await scopedJson<typeof result>("POST", new URL(response.url()).pathname, request, {
      "Idempotency-Key": response.request().headers()["idempotency-key"],
    });
    expect(replay.receipt.documentId).toBe(documentId);
    const retained = await scopedJson<AccountingEntry>("GET", `/api/commerce/v1/accounting/entries/by-document/${documentId}`);
    expect(retained.entryId).toBe(entry.entryId);
    documents.push({ documentId, costCenterId: explicit ? center.costCenterId : null, entryId: entry.entryId });
    await expect(payment).toBeHidden();
  }
  await test.info().attach("cost-center-accepted-documents", { body: JSON.stringify(documents, null, 2), contentType: "application/json" });
});
