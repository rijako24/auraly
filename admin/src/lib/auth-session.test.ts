import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  clearPreviousWebIdentityContext,
  isActiveLocalPosSession,
  isAuthenticationRequest,
  isCurrentWebSessionVersion,
  retryAuthenticatedRequest,
  runAuthenticationSessionReplacement,
  shouldRunCloudBackgroundSynchronization,
  shouldRefreshSession,
} from "./auth-session";

describe("auth session decisions", () => {
  it("clears the previous identity context before a new login is installed", () => {
    const removed: string[] = [];
    clearPreviousWebIdentityContext({ removeItem: (key) => { removed.push(key); } });
    assert.deepEqual(removed, [
      "auth-state",
      "selected_tenant_id",
      "selected_business_id",
    ]);
  });

  it("coalesces simultaneous submits so one browser cannot revoke its own login", async () => {
    let releasesLogin!: (value: string) => void;
    const loginResult = new Promise<string>((resolve) => { releasesLogin = resolve; });
    let loginCalls = 0;
    let boundaries = 0;
    const login = () => { loginCalls += 1; return loginResult; };

    const first = runAuthenticationSessionReplacement(() => { boundaries += 1; }, login);
    const second = runAuthenticationSessionReplacement(() => { boundaries += 1; }, login);
    releasesLogin("authenticated");

    assert.equal(await first, "authenticated");
    assert.equal(await second, "authenticated");
    assert.equal(loginCalls, 1);
    assert.equal(boundaries, 2);
  });

  it("never lets a response from the replaced browser session expire the winner", () => {
    assert.equal(isCurrentWebSessionVersion("old", "new"), false);
    assert.equal(isCurrentWebSessionVersion("winner", "winner"), true);
  });

  it("fences stale requests before login can revoke the previous session", async () => {
    const events: string[] = [];
    let finishLogin!: (value: string) => void;
    const login = new Promise<string>((resolve) => { finishLogin = resolve; });

    const replacement = runAuthenticationSessionReplacement(
      () => events.push("boundary"),
      () => {
        events.push("login");
        return login;
      },
    );

    assert.deepEqual(events, ["boundary", "login"]);
    finishLogin("authenticated");
    assert.equal(await replacement, "authenticated");
    assert.deepEqual(events, ["boundary", "login", "boundary"]);
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

  it("uses the exact replacement reason only when refresh confirms another login", async () => {
    const reasons: string[] = [];
    await retryAuthenticatedRequest(
      "/api/commerce/v1/orders",
      async () => ({ status: 401 }),
      async () => "replaced",
      async (reason) => { reasons.push(reason); },
    );
    assert.deepEqual(reasons, ["replaced"]);
  });

  it("does not turn a resource 401 into a false concurrent-login closure", async () => {
    let sends = 0;
    let expirations = 0;
    const response = await retryAuthenticatedRequest(
      "/api/commerce/v1/restricted-view",
      async () => ({ status: (++sends, 401) }),
      async () => "refreshed",
      async () => { expirations += 1; },
    );
    assert.equal(response.status, 401);
    assert.equal(sends, 2);
    assert.equal(expirations, 0);
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
