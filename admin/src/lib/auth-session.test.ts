import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  isAuthenticationRequest,
  isInstalledApplicationDisplay,
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
});
