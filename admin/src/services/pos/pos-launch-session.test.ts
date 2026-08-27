import assert from "node:assert/strict";
import test from "node:test";
import { resolvePosOrderPrintRoute } from "./pos-order-print-routing";
import {
  clearInstalledPosUserSession,
  installedPosLaunchDestination,
} from "./pos-launch-session";
import {
  canIssuePosDocument,
  fiscalConfigurationRequiredMessage,
} from "./pos-fiscal-guard";

test("electronic invoices require active fiscal configuration", () => {
  assert.equal(canIssuePosDocument("SalesInvoice", false), false);
  assert.equal(canIssuePosDocument("SalesInvoice", true), true);
  assert.equal(canIssuePosDocument("SalesReceipt", false), true);
  assert.match(fiscalConfigurationRequiredMessage, /Configuración fiscal/);
});

test("an enrolled installed POS always returns to the cashier login surface", () => {
  assert.equal(
    installedPosLaunchDestination({ status: "LoginRequired", identityReady: true }),
    "/pos",
  );
  assert.equal(
    installedPosLaunchDestination({ status: "Ready", identityReady: true }),
    "/pos",
  );
});

test("an unenrolled installed POS uses the online login", () => {
  assert.equal(
    installedPosLaunchDestination({ status: "EnrollmentRequired", identityReady: false }),
    "/login?redirect=%2Fpos",
  );
  assert.equal(installedPosLaunchDestination(null), "/login?redirect=%2Fpos");
});

test("a desktop launch removes a prior cashier lease", () => {
  const removed: string[] = [];
  clearInstalledPosUserSession({ removeItem: (key) => removed.push(key) });
  assert.deepEqual(removed, ["auraly.pos.user-session"]);
});
test("connected Auraly orders use the installed orders printer", () => {
  assert.equal(resolvePosOrderPrintRoute("edge-session"), "installed-pos");
  assert.equal(resolvePosOrderPrintRoute(null), "browser");
});
