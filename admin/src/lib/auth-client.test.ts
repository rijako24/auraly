import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { resolveAuthenticationClientId } from "./auth-client";

describe("resolveAuthenticationClientId", () => {
  it("keeps the durable browser identifier across tabs and requests", () => {
    const existing = "0198f79a-0395-7f92-9577-cf7fcba60563";
    assert.equal(
      resolveAuthenticationClientId(existing, () => assert.fail("must not generate")),
      existing,
    );
  });

  it("replaces a missing or malformed identifier with a UUID", () => {
    const generated = "6198f79a-0395-4f92-9577-cf7fcba60563";
    assert.equal(resolveAuthenticationClientId(undefined, () => generated), generated);
    assert.equal(resolveAuthenticationClientId("not-a-guid", () => generated), generated);
  });
});
