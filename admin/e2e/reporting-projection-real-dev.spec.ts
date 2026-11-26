import { expect, test } from '@playwright/test';
import { writeFileSync } from 'node:fs';
import { realAccountingContext } from './support/real-accounting-workspace';
import type { SalesReportContext, SalesReportExplorerPage } from '../src/services/api/sales-reporting';
import type { ServerIssuedSalePage } from '../src/services/pos/pos-server-history-request';

for (const width of [1366, 1920, 390]) {
  test(`DEV proyecciones y facturas reales ${width}`, async ({ page }, info) => {
    test.skip(process.env.AURALY_E2E_TARGET !== 'dev', 'Requiere DEV autorizado.');
    test.setTimeout(240_000);
    await page.setViewportSize({ width, height: width === 390 ? 844 : width === 1920 ? 1080 : 768 });
    const ctx = await realAccountingContext(page);
    const runtime = await ctx.scopedJson<{ build: string }>('GET', '/api/health');
    expect(process.env.AURALY_E2E_EXPECTED_COMMIT, 'Indica el commit que se verifica.').toBeTruthy();
    expect(runtime.build).toContain(process.env.AURALY_E2E_EXPECTED_COMMIT!.slice(0, 8));
    const proof: unknown[] = [];
    for (const [route, title] of [
      ['sales', 'Ventas'], ['impact', 'Impacto comercial'],
      ['products', 'Productos y rentabilidad'], ['supplier-impact', 'Proveedores: compras y ventas'],
    ]) {
      const contextResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/sales-reports/context'));
      const reportResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/sales-reports/explore'));
      await page.goto(`/dashboard/reports/${route}?documentType=SalesReceipt`);
      const [contextHttp, reportHttp] = await Promise.all([contextResponse, reportResponse]);
      expect(contextHttp.ok(), await contextHttp.text()).toBe(true);
      expect(reportHttp.ok(), await reportHttp.text()).toBe(true);
      const context: SalesReportContext = await contextHttp.json();
      const report: SalesReportExplorerPage = await reportHttp.json();
      await expect(page.getByRole('heading', { name: title, exact: true })).toBeVisible();
      expect(context.pendingSourceCount).toBeGreaterThanOrEqual(0);
      expect(context.failedSourceCount).toBe(0);
      expect(report.totals.missingCostLines).toBe(0);
      expect(typeof report.totals.recognizedCost).toBe('number');
      expect(typeof report.totals.profit).toBe('number');
      expect(report.totals.profit!).toBeCloseTo(report.totals.netUntaxed - report.totals.recognizedCost!, 3);
      expect(report.items.length).toBeLessThanOrEqual(report.pageSize);
      await expect(page.getByText(/No modelad[oa]/i)).toHaveCount(0);
      expect(await page.getByRole('main').evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
      if (report.items.length) {
        const column = page.getByRole('columnheader', { name: /^Bruto sin impuestos/ });
        const sortedResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/sales-reports/explore')
          && r.request().postDataJSON()?.orderBy === 'gross-asc');
        await column.getByRole('button').click();
        const sorted = await sortedResponse;
        expect(sorted.ok(), await sorted.text()).toBe(true);
        await expect(column).toHaveAttribute('aria-sort', 'ascending');
      }
      await page.screenshot({ path: info.outputPath(`real-${route}-${width}.png`), fullPage: true });
      proof.push({ route, totalCount: report.totalCount, totals: report.totals,
        pendingSources: context.pendingSourceCount, failedSources: context.failedSourceCount });
    }
    const historyResponse = page.waitForResponse(r => new URL(r.url()).pathname.endsWith('/pos/drafts/sales/search'));
    await page.goto('/dashboard/invoices');
    const history = await historyResponse;
    expect(history.ok(), await history.text()).toBe(true);
    const invoices: ServerIssuedSalePage = await history.json();
    expect(invoices.items.length).toBeLessThanOrEqual(20);
    await expect(page.getByRole('heading', { name: 'Facturas y comprobantes', exact: true })).toBeVisible();
    await expect(page.getByText('No fue posible consultar los comprobantes de esta sede.')).toHaveCount(0);
    expect(await page.getByRole('main').evaluate(node => node.scrollWidth <= node.clientWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath(`real-invoices-${width}.png`), fullPage: true });
    writeFileSync(info.outputPath('reporting-proof.json'), JSON.stringify({ build: runtime.build, width,
      realBrowser: true, realBackend: true, reports: proof, invoices: invoices.items.length, productionWrites: 0 }, null, 2));
  });
}
