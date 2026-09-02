import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  isActiveLocalPosSession,
  isAuthenticationRequest,
  isCurrentWebSessionVersion,
  retryAuthenticatedRequest,
  shouldRunCloudBackgroundSynchronization,
  shouldRefreshSession,
} from "./auth-session";

describe("auth session decisions", () => {
  it("never lets a response from the replaced browser session expire the winner", () => {
    assert.equal(isCurrentWebSessionVersion("old", "new"), false);
    assert.equal(isCurrentWebSessionVersion("winner", "winner"), true);
  });

  it("keeps an enrolled POS session independent from a stale web cookie", () => {
    assert.equal(isActiveLocalPosSession("/pos", "edge", "cashier"), true);
    assert.equal(isActiveLocalPosSession("/pos", "edge", null), false);
    assert.equal(isActiveLocalPosSession("/dashboard", "edge", "cashier"), false);
  });

  it("runs cloud outbox synchronization only inside the cloud workspace", () => {
    assert.equal(shouldRunCloudBackgroundSynchronization("/dashboard/orders"), true);
    assert.equal(shouldRunCloudBackgroundSynchronization("/login"), false);
    assert.equal(shouldRunCloudBackgroundSynchronization("/pos"), false);
  });

  it("refreshes protected web requests after an unauthorized response", () => {
    assert.equal(shouldRefreshSession(401, "/api/businesses"), true);
    assert.equal(shouldRefreshSession(403, "/api/businesses"), false);
  });

  it("never recursively refreshes authentication endpoints", () => {
    assert.equal(isAuthenticationRequest("/api/auth/refresh"), true);
    assert.equal(shouldRefreshSession(401, "/api/auth/login"), false);
  });

  it("renews and retries any protected request once", async () => {
    const statuses = [401, 200];
    let sends = 0;
    let refreshes = 0;
    let expirations = 0;

    const response = await retryAuthenticatedRequest(
      "/api/commerce/v1/pos/drafts/products/search",
      async () => ({ status: statuses[sends++] }),
      async () => { refreshes += 1; return "refreshed"; },
      async () => { expirations += 1; },
    );

    assert.equal(response.status, 200);
    assert.equal(sends, 2);
    assert.equal(refreshes, 1);
    assert.equal(expirations, 0);
  });

  it("expires the session when renewal fails", async () => {
    let expirations = 0;
    const response = await retryAuthenticatedRequest(
      "/api/commerce/v1/orders",
      async () => ({ status: 401 }),
      async () => "expired",
      async () => { expirations += 1; },
    );

    assert.equal(response.status, 401);
    assert.equal(expirations, 1);
  });

  it("preserves the signed-in shell when renewal is temporarily unavailable", async () => {
    let expirations = 0;
    const response = await retryAuthenticatedRequest(
      "/api/commerce/v1/parties/countries",
      async () => ({ status: 401 }),
      async () => "unavailable",
      async () => { expirations += 1; },
    );

    assert.equal(response.status, 401);
    assert.equal(expirations, 0);
  });
});
