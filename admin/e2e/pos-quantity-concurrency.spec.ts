import { expect, test } from "@playwright/test";

test.use({ viewport: { width: 1366, height: 768 }, serviceWorkers: "block" });

for (const mode of ["DirectSale", "Store"]) for (const trigger of ["Enter", "Tab", "Blur"]) {
  test(`${mode}: cantidad con ${trigger} repetido espera el único guardado y conserva la versión`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const warehouseId = "33333333-3333-3333-3333-333333333333";
    const user = { userId: "44444444-4444-4444-4444-444444444444", tenantId, tenantKey: "TEST",
      username: "cajero", firstName: "Caja", lastName: "Prueba", roles: ["Cajero"], permissions: ["sales.create"] };
    const workspace = { businessId, warehouseId, businessName: "Prueba", warehouseName: "Principal",
      warehouseCode: "B01", warehouseAllowsNegativeStockSales: true, hasActiveEdgeEnrollment: false, pointOfSaleMode: mode };
    let version = 2, quantity = 1;
    const snapshot = () => ({ draftId: "quantity-draft", ...workspace, workSessionId: "session", userId: user.userId,
      status: "Active", version, charges: [], lines: [{ lineId: "line-1", productId: "product-1", productCode: "PRD-PESO",
        description: "Producto por peso", unitCode: "KG", quantity, unitPrice: 10_000, baseUnitPrice: 10_000,
        publicUnitPrice: 10_000, publicLineTotal: quantity * 10_000, net: quantity * 10_000, tax: 0, total: quantity * 10_000,
        discount: 0, taxCode: "IVA", taxRate: 0, priceSource: "Base", allowsFractionalSale: true }],
      untaxedAmount: quantity * 10_000, taxAmount: 0, payableAmount: quantity * 10_000 });
    const writes: { quantity: number; expectedVersion: number }[] = [];
    let activeReads = 0, releaseFirst: () => void = () => undefined;
    const firstResponse = new Promise<void>(resolve => { releaseFirst = resolve; });
    await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true, sameSite: "Lax" }]);
    await page.addInitScript(({ tenantId, businessId, warehouseId, user }) => {
      localStorage.setItem("selected_tenant_id", tenantId); localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
      localStorage.setItem(`auraly.pos.sales-workspace:${tenantId}:${user.userId}`, `${businessId}:${warehouseId}`);
    }, { tenantId, businessId, warehouseId, user });
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let body: unknown = [];
      if (path.endsWith("/auth/me")) body = user;
      else if (path.endsWith("/workspace/bootstrap")) body = { tenantId, tenantName: "Prueba", userId: user.userId,
        userDisplayName: "Caja Prueba", options: [workspace], canEnrollPosDevice: false, activeEnrolledDeviceCount: 0, maximumEnrolledDevices: 0 };
      else if (path.endsWith("/workspace/options")) body = [workspace];
      else if (path.endsWith("/workspace/select")) body = workspace;
      else if (path.endsWith("/work-sessions/current")) body = { workSessionId: "session" };
      else if (path.endsWith("/pos/catalog/products/search")) body = { items: [], hasMore: false, nextOffset: null };
      else if (path.endsWith("/drafts/active")) { activeReads++; body = snapshot(); }
      else if (path.endsWith("/quantity-draft/lines/line-1/quantity")) {
        const request = route.request().postDataJSON(); writes.push(request);
        if (request.expectedVersion !== version) {
          await route.fulfill({ status: 409, contentType: "application/problem+json",
            body: JSON.stringify({ title: "SalesDraftVersionConflict", status: 409, detail: `El borrador cambió. Versión actual: ${version}.` }) });
          return;
        }
        quantity = request.quantity; version++; body = snapshot();
        if (writes.length === 1) await firstResponse;
      } else if (path.includes("/orders")) {
        const items = mode === "DirectSale" && trigger === "Enter" ? [1,2,3].map(index => ({
          orderId:`order-${index}`,orderNumber:`PED-000${index}`,customerName:`Cliente ${index}`,
          customerId:`customer-${index}`,status:"Confirmed",createdAt:"2026-11-24T12:00:00Z",
          total:10000,currency:"COP",lineCount:1,canInvoice:true,canRecover:true,
        })) : [];
        body = { items, totalCount: items.length, totalPages: 1, page: 1, pageSize: 25 };
      }
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });
    try {
      await page.goto("/pos");
      const input = page.getByRole("spinbutton", { name: "Cantidad de Producto por peso", exact: true });
      await expect(input).toHaveValue("1");
      if (mode === "DirectSale" && trigger === "Enter") {
        await page.getByRole("tab", { name: /^Pedidos/ }).click();
        for (const viewport of [{ width: 1918, height: 866 }, { width: 1536, height: 693 }, { width: 1366, height: 768 }]) {
          await page.setViewportSize(viewport);
          const geometry = await page.evaluate(() => ({
            height: document.documentElement.scrollHeight, viewport: innerHeight,
            width: document.documentElement.scrollWidth, viewportWidth: innerWidth,
            headerTop: document.querySelector("main > header")!.getBoundingClientRect().top,
          }));
          expect(geometry.height, JSON.stringify({ viewport, geometry })).toBeLessThanOrEqual(geometry.viewport);
          expect(geometry.width).toBeLessThanOrEqual(geometry.viewportWidth);
          expect(geometry.headerTop).toBe(0);
        }
        await page.setViewportSize({ width: 1366, height: 768 });
        // The header can grow with display scaling or a longer operator name.
        await page.locator("main > header").evaluate(element => { element.style.minHeight = "84px"; });
        const frame = await page.locator("main").first().evaluate(element => ({ height: element.clientHeight, content: element.scrollHeight }));
        expect(frame.content).toBeLessThanOrEqual(frame.height);
        await page.locator("main > header").evaluate(element => { element.style.minHeight = ""; });
      }
      const readsBefore = activeReads;
      await input.fill("0.333");
      // Dispatch both events in one task: React state alone is not a synchronous lock.
      await input.evaluate((element, trigger) => {
        for (let repeat = 0; repeat < 2; repeat++) element.dispatchEvent(trigger === "Blur"
          ? new FocusEvent("focusout", { bubbles: true })
          : new KeyboardEvent("keydown", { key: trigger, bubbles: true, cancelable: true }));
      }, trigger);
      await expect(input).toBeDisabled({ timeout: 2_000 });
      expect(writes).toEqual([{ quantity: 0.333, expectedVersion: 2 }]);
      releaseFirst();
      await expect(input).toBeEnabled(); await expect(input).toHaveValue("0.333");
      await expect(page.getByText(/El borrador cambió/)).toHaveCount(0);
      expect(activeReads).toBe(readsBefore);
      await input.fill("0.5"); await input.press("Tab");
      await expect(input).toHaveValue("0.5"); await expect(input).toBeEnabled();
      expect(writes).toEqual([{ quantity: 0.333, expectedVersion: 2 }, { quantity: 0.5, expectedVersion: 3 }]);
      expect(activeReads).toBe(readsBefore);
      if (trigger === "Enter") {
        await page.screenshot({ path: `test-results/pos-quantity-${mode}-1366.png`, fullPage: true });
        await page.setViewportSize({ width: 390, height: 844 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        await page.screenshot({ path: `test-results/pos-quantity-${mode}-390.png`, fullPage: true });
      }
      if (mode === "DirectSale" && trigger === "Enter") {
        version++; // A genuine edit from another window must still be rejected.
        await input.fill("0.75"); await input.press("Enter");
        await expect(page.getByText(`El borrador cambió. Versión actual: ${version}.`, { exact: true })).toBeVisible();
        expect(writes).toHaveLength(3); expect(activeReads).toBe(readsBefore);
        await page.reload(); await expect(input).toHaveValue("0.5");
        await input.fill("0.75"); await input.press("Enter");
        await expect(input).toHaveValue("0.75"); await expect(input).toBeEnabled();
        expect(writes).toHaveLength(4); expect(writes[3].expectedVersion).toBe(5);
      }
    } finally { releaseFirst(); }
  });
}
