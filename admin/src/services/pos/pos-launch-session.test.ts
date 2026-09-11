import assert from "node:assert/strict";
import test from "node:test";
import { resolvePosOrderPrintRoute } from "./pos-order-print-routing";
import {
  installedPosLaunchDestination,
  shouldFallbackToLocalPos,
  usesEnrolledPosRuntime,
  workspaceActivationMode,
} from "./pos-launch-session";
import { isCurrentEdgeUserSession } from "./pos-edge-session";
import {
  canIssuePosDocument,
  dianQuotaExhaustedMessage,
  fiscalConfigurationRequiredMessage,
  fiscalLaunchReadinessError,
} from "./pos-fiscal-guard";

test("a delayed response from an old Edge login cannot clear the new login", () => {
  assert.equal(isCurrentEdgeUserSession("session-a", "session-b"), false);
  assert.equal(isCurrentEdgeUserSession("session-b", "session-b"), true);
});

test("electronic invoices require active fiscal configuration", () => {
  assert.equal(canIssuePosDocument("SalesInvoice", false), false);
  assert.equal(canIssuePosDocument("SalesInvoice", true), true);
  assert.equal(canIssuePosDocument("SalesReceipt", false), true);
  assert.match(fiscalConfigurationRequiredMessage, /Configuración fiscal/);
});

test("offline enrollment delegates fiscal recovery and assignment to the server", () => {
  const onlineOnly = {
    isReadyForOnlineSales: true,
  };
  const missingOnlineConfiguration = {
    isReadyForOnlineSales: false,
  };

  assert.equal(fiscalLaunchReadinessError("online", onlineOnly), null);
  assert.equal(fiscalLaunchReadinessError("enroll", onlineOnly), null);
  assert.equal(
    fiscalLaunchReadinessError("enroll", missingOnlineConfiguration),
    null,
  );
});

test("online invoices still require the active server fiscal configuration", () => {
  const missingConfiguration = {
    isReadyForOnlineSales: false,
  };

  assert.equal(
    fiscalLaunchReadinessError("online", missingConfiguration),
    fiscalConfigurationRequiredMessage,
  );
});

test("an enrolled installation opens the shared Auraly login", () => {
  assert.equal(
    installedPosLaunchDestination({ status: "LoginRequired", identityReady: true }),
    "/login",
  );
  assert.equal(
    installedPosLaunchDestination({ status: "Ready", identityReady: true }),
    "/login",
  );
});

test("an unenrolled installation opens the same shared Auraly login", () => {
  assert.equal(
    installedPosLaunchDestination({ status: "EnrollmentRequired", identityReady: false }),
    "/login",
  );
  assert.equal(installedPosLaunchDestination(null), "/login");
});

test("an administrative installed login falls back locally only when cloud is unavailable", () => {
  const unavailable = Object.assign(new Error("Servicio no disponible"), { statusCode: 503 });
  const denied = Object.assign(new Error("Credenciales inválidas"), { statusCode: 401 });
  const timeout = new Error("The operation timed out");
  timeout.name = "TimeoutError";

  assert.equal(shouldFallbackToLocalPos(unavailable, true), true);
  assert.equal(shouldFallbackToLocalPos(denied, true), false);
  assert.equal(shouldFallbackToLocalPos(timeout, true), true);
  assert.equal(shouldFallbackToLocalPos(new Error("Empresa requerida"), true), false);
  assert.equal(shouldFallbackToLocalPos(denied, false), true);
});

test("enrollment is the single owner of installed runtime selection", () => {
  assert.equal(
    usesEnrolledPosRuntime({ status: "LoginRequired", identityReady: true }),
    true,
  );
  assert.equal(
    usesEnrolledPosRuntime({ status: "Ready", identityReady: true }),
    true,
  );
  assert.equal(
    usesEnrolledPosRuntime({ status: "EnrollmentRequired", identityReady: false }),
    false,
  );
});

test("online invoices report an exhausted DIAN quota without a technical error", () => {
  assert.equal(
    fiscalLaunchReadinessError("online", {
      isReadyForOnlineSales: true,
      hasDianDocumentQuota: false,
    }),
    dianQuotaExhaustedMessage,
  );
});
test("configuration never changes an enrolled installation to the online adapter", () => {
  assert.equal(
    workspaceActivationMode("edge", "business-a", "warehouse-a", "business-a", "warehouse-a"),
    "keep-edge",
  );
  assert.equal(
    workspaceActivationMode("edge", "business-a", "warehouse-a", "business-b", "warehouse-b"),
    "reenrollment-required",
  );
  assert.equal(
    workspaceActivationMode("online", "business-a", "warehouse-a", "business-b", "warehouse-b"),
    "activate-online",
  );
});
test("order printing selects the installed transport without changing issuance ownership", () => {
  assert.equal(resolvePosOrderPrintRoute("edge-session"), "installed-app");
  assert.equal(resolvePosOrderPrintRoute(null), "browser");
});
