import assert from "node:assert/strict";
import test from "node:test";
import { resolvePosOrderPrintRoute } from "./pos-order-print-routing";
import {
  installedPosLaunchDestination,
  usesEnrolledPosRuntime,
} from "./pos-launch-session";
import {
  canIssuePosDocument,
  fiscalConfigurationRequiredMessage,
  fiscalLaunchReadinessError,
} from "./pos-fiscal-guard";

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
test("connected Auraly orders use the installed orders printer", () => {
  assert.equal(resolvePosOrderPrintRoute("edge-session"), "installed-pos");
  assert.equal(resolvePosOrderPrintRoute(null), "browser");
});
