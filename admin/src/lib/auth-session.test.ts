import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  classifySessionRuntime,
  isAuthenticationRequest,
  isInstalledApplicationDisplay,
  retryAuthenticatedRequest,
  shouldExpireSession,
  shouldRefreshSession,
} from "./auth-session";

describe("auth session decisions", () => {
  it("refreshes protected web requests after an unauthorized response", () => {
    assert.equal(shouldRefreshSession(401, "/api/businesses"), true);
    assert.equal(shouldRefreshSession(403, "/api/businesses"), false);
  });

  it("never recursively refreshes authentication endpoints", () => {
    assert.equal(isAuthenticationRequest("/api/auth/refresh"), true);
    assert.equal(shouldRefreshSession(401, "/api/auth/login"), false);
  });

  it("recognizes installed and desktop application displays", () => {
    assert.equal(isInstalledApplicationDisplay(true, false), true);
    assert.equal(isInstalledApplicationDisplay(false, true), true);
    assert.equal(isInstalledApplicationDisplay(false, false), false);
  });

  it("expires an invalid session in installed web apps but preserves the desktop runtime", () => {
    assert.equal(classifySessionRuntime(false, false, false), "web");
    assert.equal(classifySessionRuntime(false, true, false), "installed-web");
    assert.equal(classifySessionRuntime(false, false, true), "installed-web");
    assert.equal(classifySessionRuntime(true, true, true), "desktop");
    assert.equal(shouldExpireSession("web"), true);
    assert.equal(shouldExpireSession("installed-web"), true);
    assert.equal(shouldExpireSession("desktop"), false);
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
