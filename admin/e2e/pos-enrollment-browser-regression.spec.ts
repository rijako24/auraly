import { expect, test } from "@playwright/test";

test("enrolamiento se recupera en la misma pantalla sin filtrar la URL técnica", async ({ page, baseURL }) => {
  const tenantId = "11111111-1111-1111-1111-111111111111";
  const businessId = "22222222-2222-2222-2222-222222222222";
  const warehouseId = "33333333-3333-3333-3333-333333333333";
  const userId = "44444444-4444-4444-4444-444444444444";
  const workSessionId = "55555555-5555-5555-5555-555555555555";
  const user = {
    userId,
    tenantId,
    tenantKey: "TEST",
    username: "admin",
    firstName: "Admin",
    lastName: "Prueba",
    roles: ["ADMINISTRATOR"],
    permissions: ["sales.create", "pos.devices.enroll"],
  };
  const workspace = {
    businessId,
    warehouseId,
    businessName: "Sede prueba",
    warehouseName: "Principal",
    warehouseCode: "VEN",
    warehouseAllowsNegativeStockSales: false,
    hasActiveEdgeEnrollment: false,
    fiscalReadyForOnlineSales: false,
    hasDianDocumentQuota: false,
  };
  const draft = {
    draftId: "66666666-6666-6666-6666-666666666666",
    businessId,
    warehouseId,
    userId,
    workSessionId,
    status: "Active",
    version: 1,
    lines: [],
    untaxedAmount: 0,
    taxAmount: 0,
    payableAmount: 0,
  };
  let redeemed = false;
  let completed = false;
  let catalogPrepared = false;
  let workSessionOpened = false;
  let redeemAttempts = 0;
  let completeCalls = 0;
  let workSessionCalls = 0;

  await page.context().addCookies([{
    name: "auth_token",
    value: "e2e",
    url: baseURL!,
    httpOnly: true,
    sameSite: "Lax",
  }]);
  await page.addInitScript(({ tenantId, businessId, user }) => {
    localStorage.setItem("selected_tenant_id", tenantId);
    localStorage.setItem("selected_business_id", businessId);
    localStorage.setItem("auth-state", JSON.stringify({
      state: { isAuthenticated: true, user },
      version: 0,
    }));
  }, { tenantId, businessId, user });

  await page.route("http://127.0.0.1:47831/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/edge/v1/enrollment/redeem") {
      redeemAttempts += 1;
      if (redeemAttempts === 1) {
        await route.fulfill({
          status: 502,
          contentType: "application/problem+json",
          body: JSON.stringify({
            detail: "Host desconocido. (api-auraly-dev-w5usmo6w.azurewebsites.net:443)",
          }),
        });
        return;
      }
      redeemed = true;
      await route.fulfill({ status: 200, contentType: "application/json", body: "{}" });
      return;
    }
    if (path === "/edge/v1/auth/complete-enrollment") {
      completed = true;
      completeCalls += 1;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(localSession(false)),
      });
      return;
    }
    if (path === "/edge/v1/work-sessions/current") {
      workSessionOpened = true;
      workSessionCalls += 1;
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(localSession(true)),
      });
      return;
    }
    if (path === "/edge/v1/synchronization/refresh") {
      catalogPrepared = true;
      await route.fulfill({ status: 202, contentType: "application/json", body: "{}" });
      return;
    }
    if (path === "/edge/v1/health") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          status: !redeemed ? "EnrollmentRequired" : !completed ? "IdentitySynchronizing" : !catalogPrepared ? "Synchronizing" : "Ready",
          initialEnrollmentSessionAvailable: redeemed && !completed,
          serverConnected: redeemed,
          pushConnected: redeemed,
          deviceSeriesCode: redeemed ? "07" : "",
          businessId: redeemed ? businessId : "",
          warehouseId: redeemed ? warehouseId : "",
          businessName: redeemed ? "Sede prueba" : "",
          warehouseName: redeemed ? "Principal" : "",
          warehouseAllowsNegativeStockSales: false,
          userDisplayName: completed ? "Admin Prueba" : "",
          userId: completed ? userId : null,
          workSessionId: workSessionOpened ? workSessionId : null,
          deviceId: redeemed ? "77777777-7777-7777-7777-777777777777" : null,
          permissions: completed ? ["sales.create", "pos.synchronization.events.read"] : [],
          fiscalReady: false,
          fiscalWarnings: [],
          dianQuotaAvailable: null,
          identityReady: redeemed,
          catalogStatus: redeemed ? catalogPrepared ? "Ready" : "Bootstrapping" : "Empty",
          synchronizationInProgress: redeemed && !completed,
          automaticRetryScheduled: false,
          automaticRetryAttempt: 0,
          lastSynchronizationAt: completed ? new Date().toISOString() : null,
          lastSynchronizationFailed: completed && !catalogPrepared,
          pendingSynchronizationCount: 0,
          oldestPendingSynchronizationAt: null,
          lastSynchronizationError: completed && !catalogPrepared
            ? "No fue posible validar la página del catálogo."
            : null,
          catalogUpdatedAt: completed ? new Date().toISOString() : null,
          catalogProcessedProducts: catalogPrepared ? 1 : 0,
          catalogTotalProducts: redeemed ? 1 : 0,
          catalogProgressPercent: catalogPrepared ? 100 : redeemed ? 0 : null,
          preparationStage: catalogPrepared ? "Finalizing" : redeemed ? "Catalog" : "Identity",
          preparationCompletedSteps: catalogPrepared ? 2 : redeemed ? 1 : 0,
          preparationTotalSteps: 2,
          preparationCanResume: redeemed && !catalogPrepared,
          synchronizationStages: completed ? [] : ["usuarios y permisos"],
          failedSynchronizationStage: completed && !catalogPrepared ? "catálogo" : null,
        }),
      });
      return;
    }
    if (path === "/edge/v1/events") {
      await route.fulfill({
        status: 200,
        contentType: "text/event-stream",
        body: "event: state\ndata: ready\n\n",
      });
      return;
    }
    if (path === "/edge/v1/drafts/active") {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(draft) });
      return;
    }
    if (path === "/edge/v1/temporaries") {
      await route.fulfill({ status: 200, contentType: "application/json", body: "[]" });
      return;
    }
    if (path === "/edge/v1/sales/next-number") {
      await route.fulfill({ status: 200, contentType: "application/json", body: "null" });
      return;
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: "{}" });
  });

  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    let body: unknown = {};
    if (path === "/api/auth/me") body = user;
    else if (path.endsWith("/workspace/bootstrap")) {
      body = {
        tenantId,
        tenantName: "Tenant prueba",
        userId,
        userDisplayName: "Admin Prueba",
        options: [workspace],
        canEnrollPosDevice: true,
        activeEnrolledDeviceCount: 0,
        maximumEnrolledDevices: 5,
      };
    } else if (path === "/api/commerce/v1/pos/enrollments") {
      body = {
        enrollmentSessionId: "88888888-8888-8888-8888-888888888888",
        redemptionCode: "123456",
        expiresAt: new Date(Date.now() + 60_000).toISOString(),
        workspace,
      };
    }
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
  });

  await page.goto("/pos#edgeToken=test-session-token-with-at-least-32-bytes");
  await expect(page.getByRole("heading", { name: "Prepara facturación" })).toBeVisible();
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Continuar a ventas" }).click();

  await expect(page.getByRole("heading", { name: "No se pudo completar la preparación" })).toBeVisible();
  await expect(page.getByText(/azurewebsites|:443/i)).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Reintentar preparación" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Volver" })).toHaveCount(1);
  await expect(page.getByRole("button", { name: "Salir de Auraly" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Repetir enrolamiento" })).toHaveCount(0);

  await page.getByRole("button", { name: "Reintentar preparación" }).click();
  await expect.poll(() => redeemAttempts).toBe(2);
  await expect.poll(() => completeCalls).toBe(1);
  await expect(page.getByRole("heading", { name: "No se pudo completar la preparación" })).toBeVisible();
  await expect(page.getByText("Productos y precios")).toBeVisible();
  await expect(page.getByRole("button", { name: "Volver" })).toHaveCount(1);
  await expect(page.getByRole("button", { name: "Salir de Auraly" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Repetir enrolamiento" })).toHaveCount(0);

  await page.getByRole("button", { name: "Reintentar preparación" }).click();
  await expect(page.locator("#pos-scanner")).toBeEnabled({ timeout: 15_000 });
  expect(redeemAttempts).toBe(2);
  expect(completeCalls).toBe(1);
  expect(workSessionCalls).toBe(1);

  await page.reload();
  await expect(page.locator("#pos-scanner")).toBeEnabled({ timeout: 15_000 });
  expect(completeCalls).toBe(1);
  expect(workSessionCalls).toBe(1);

  function localSession(opened: boolean) {
    return {
      sessionId: "99999999-9999-9999-9999-999999999999",
      workSessionId: opened ? workSessionId : "00000000-0000-0000-0000-000000000000",
      userId,
      username: "admin",
      displayName: "Admin Prueba",
      permissions: ["sales.create", "pos.synchronization.events.read"],
      expiresAt: new Date(Date.now() + 86_400_000).toISOString(),
      token: "local-user-session",
    };
  }
});
