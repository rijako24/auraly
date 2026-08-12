import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resolveAuthorizedSelection } from "./execution-context-selection";

describe("resolveAuthorizedSelection", () => {
  it("selects the only authorized option without asking", () => {
    assert.equal(resolveAuthorizedSelection(["tenant-1"], null), "tenant-1");
  });

  it("restores the last authorized option", () => {
    assert.equal(
      resolveAuthorizedSelection(["tenant-1", "tenant-2"], "tenant-2"),
      "tenant-2",
    );
  });

  it("restores UUID selections regardless of letter casing", () => {
    const canonical = "2368d79b-fe40-465f-b43d-e69332ee979c";
    assert.equal(
      resolveAuthorizedSelection(
        ["a0a10000-0000-0000-0000-000000000000", canonical],
        canonical.toUpperCase(),
      ),
      canonical,
    );
  });

  it("falls back safely when the previous option was revoked", () => {
    assert.equal(
      resolveAuthorizedSelection(["tenant-1"], "tenant-revoked"),
      "tenant-1",
    );
  });
});
