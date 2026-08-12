import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { buildLoginRedirect } from "./login-redirect";

describe("buildLoginRedirect", () => {
  it("returns an expired POS session to the POS", () => {
    assert.equal(buildLoginRedirect("/pos"), "/login?redirect=%2Fpos");
  });

  it("preserves a safe local query string", () => {
    assert.equal(
      buildLoginRedirect("/dashboard/orders", "?page=2"),
      "/login?redirect=%2Fdashboard%2Forders%3Fpage%3D2",
    );
  });

  it("does not accept a non-local pathname", () => {
    assert.equal(buildLoginRedirect("https://evil.test"), "/login?redirect=%2F");
  });
});
