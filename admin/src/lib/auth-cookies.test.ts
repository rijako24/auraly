import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { shouldUseSecureAuthCookies } from "./auth-cookies";

describe("shouldUseSecureAuthCookies", () => {
  it("keeps secure cookies for the SaaS production host", () => {
    assert.equal(shouldUseSecureAuthCookies("production", undefined), true);
    assert.equal(shouldUseSecureAuthCookies("production", "false"), true);
  });

  it("allows loopback HTTP only inside the installed desktop application", () => {
    assert.equal(shouldUseSecureAuthCookies("production", "true"), false);
  });

  it("allows local development over HTTP", () => {
    assert.equal(shouldUseSecureAuthCookies("development", undefined), false);
  });
});
