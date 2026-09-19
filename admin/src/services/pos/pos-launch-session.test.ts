import assert from "node:assert/strict";
import test from "node:test";
import { resolvePosOrderPrintRoute } from "./pos-order-print-routing";
import {
  enrolledWorkspaceOption,
  installedPosLaunchDestination,
  resolvePosExecutionMode,
  shouldAutoActivateRememberedWorkspace,
  workspaceActivationMode,
} from "./pos-launch-session";
import { isCurrentEdgeUserSession } from "./pos-edge-session";
import {
  canIssuePosDocument,
  dianQuotaExhaustedMessage,
  fiscalConfigurationRequiredMessage,
  fiscalLaunchReadinessError,
  posDocumentReadinessError,
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

test("entering an electronic invoice reports the missing local resolution", () => {
  assert.equal(
    posDocumentReadinessError("SalesInvoice", false, true),
    fiscalConfigurationRequiredMessage,
  );
  assert.equal(posDocumentReadinessError("SalesReceipt", false, true), null);
});

test("an unenrolled installation resumes its remembered online workspace", () => {
  assert.equal(shouldAutoActivateRememberedWorkspace(null), false);
  assert.equal(shouldAutoActivateRememberedWorkspace("business-a:warehouse-a"), true);
});

test("one resolver selects web for browser or unenrolled installations", () => {
  assert.equal(resolvePosExecutionMode(false, null), "online");
  assert.equal(resolvePosExecutionMode(true, {
    status: "EnrollmentRequired",
    identityReady: false,
  }), "online");
});

test("the same resolver selects SQLite for every enrolled preparation state", () => {
  for (const status of [
    "IdentitySynchronizing",
    "Synchronizing",
    "LoginRequired",
    "Ready",
  ])
    assert.equal(resolvePosExecutionMode(
      true,
      { status, identityReady: status === "Ready" },
    ), "edge");
});

test("an unavailable installed service does not guess a data owner", () => {
  assert.equal(resolvePosExecutionMode(true, null), null);
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
test("enrolled configuration is projected from Edge without a cloud bootstrap", () => {
  assert.deepEqual(
    enrolledWorkspaceOption({
      businessId: "business-a",
      businessName: "Auraly",
      warehouseId: "warehouse-a",
      warehouseName: "Bodega principal",
      warehouseAllowsNegativeStockSales: true,
      fiscalReady: true,
      fiscalWarnings: ["warning"],
      dianQuotaAvailable: null,
    }),
    {
      businessId: "business-a",
      businessName: "Auraly",
      warehouseId: "warehouse-a",
      warehouseCode: "",
      warehouseName: "Bodega principal",
      warehouseAllowsNegativeStockSales: true,
      hasActiveEdgeEnrollment: true,
      fiscalReadyForOnlineSales: true,
      fiscalReadyForEnrollment: true,
      hasDianDocumentQuota: true,
      fiscalWarningMessages: ["warning"],
    },
  );
});
test("order printing selects the installed transport without changing issuance ownership", () => {
  assert.equal(resolvePosOrderPrintRoute("edge-session"), "installed-app");
  assert.equal(resolvePosOrderPrintRoute(null), "browser");
});
