import { expect, test, type WebSocketRoute } from "@playwright/test";

for (const firstAttemptFails of [false, true]) {
  test(`autorizaciones: una lectura inicial y actualización por evento y reconexión, fallo inicial=${firstAttemptFails}`, async ({ page, baseURL }) => {
    const tenantId = "11111111-1111-1111-1111-111111111111";
    const businessId = "22222222-2222-2222-2222-222222222222";
    const user = { userId: "33333333-3333-3333-3333-333333333333", tenantId, tenantKey: "@test", username: "supervisor",
      firstName: "Supervisor", lastName: "Prueba", roles: [], permissions: ["sales.reprint", "pos.approvals.read", "pos.approvals.authorize"] };
    await page.context().addCookies([{ name: "auth_token", value: "e2e", url: baseURL!, httpOnly: true }]);
    await page.addInitScript(({ user, businessId }) => {
      localStorage.setItem("selected_tenant_id", user.tenantId);
      localStorage.setItem("selected_business_id", businessId);
      localStorage.setItem("auraly:pwa-install-dismissed", "1");
      localStorage.setItem("auth-state", JSON.stringify({ state: { isAuthenticated: true, user }, version: 0 }));
    }, { user, businessId });
    const sockets: WebSocketRoute[] = [];
    await page.routeWebSocket("wss://qa.invalid/approvals", socket => { sockets.push(socket); });
    let pendingReads = 0, negotiations = 0;
    let releaseRecovery!: () => void;
    const recoveryGate = new Promise<void>(resolve => { releaseRecovery = resolve; });
    await page.route("**/api/**", async route => {
      const path = new URL(route.request().url()).pathname;
      let body: unknown = [];
      if (path.endsWith("/auth/me")) body = user;
      else if (path.endsWith("/execution-context/tenants")) body = [{ tenantId, name: "Empresa" }];
      else if (path.endsWith("/execution-context/businesses")) body = [{ tenantId, businessId, name: "Sede", timeZone: "America/Bogota" }];
      else if (path.endsWith("/execution-context/access")) body = { tenantId, businessId, roles: [], permissions: user.permissions };
      else if (path.endsWith("/pos/approvals/pending")) pendingReads++;
      else if (path.endsWith("/pos/approvals/synchronization/negotiate")) {
        negotiations++;
        if (firstAttemptFails && negotiations === 1) {
          await route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ title: "Canal no disponible" }) });
          return;
        }
        if (firstAttemptFails && negotiations === 2) await recoveryGate;
        body = { clientAccessUri: "wss://qa.invalid/approvals", expiresAt: new Date(Date.now() + 60_000).toISOString() };
      } else if (path.endsWith("/pos/drafts/sales/search")) body = { items: [], hasMore: false, nextOffset: null };
      else if (path.includes("/tenants/branding")) body = { tenantId, displayName: "Empresa", legalName: null, logoUrl: null };
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    });
    try {
      await page.goto("/dashboard/invoices");
      await expect.poll(() => pendingReads).toBe(1);
      if (firstAttemptFails) {
        releaseRecovery();
        await expect.poll(() => pendingReads).toBe(2);
      }
      await page.waitForLoadState("networkidle");
      const initialReads = firstAttemptFails ? 2 : 1;
      expect(pendingReads).toBe(initialReads);
      sockets.at(-1)!.send(JSON.stringify({ type: "message", data: { Stream: "Approvals", NotificationId: "change-1" } }));
      await expect.poll(() => pendingReads).toBe(initialReads + 1);
      await sockets.at(-1)!.close({ code: 1012, reason: "Ensayo de reconexión" });
      await expect.poll(() => pendingReads).toBe(initialReads + 2);
      await page.waitForLoadState("networkidle");
      expect(pendingReads).toBe(initialReads + 2);
    } finally { releaseRecovery(); }
  });
}
