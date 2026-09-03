import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  posOperationalContextKey,
  posWorkspaceOptionsCacheKey,
  posWorkspaceStorageKey,
} from "./pos-operational-context";

describe("POS operational context", () => {
  it("isolates workspace state by tenant and user", () => {
    assert.notEqual(
      posWorkspaceStorageKey("tenant-a", "user-1"),
      posWorkspaceStorageKey("tenant-b", "user-1"),
    );
    assert.notEqual(
      posWorkspaceOptionsCacheKey("tenant-a", "user-1"),
      posWorkspaceOptionsCacheKey("tenant-a", "user-2"),
    );
  });

  it("does not create an unscoped POS key", () => {
    assert.equal(posOperationalContextKey(null, "user-1"), null);
    assert.equal(posWorkspaceStorageKey("tenant-a", null), null);
    assert.equal(posWorkspaceOptionsCacheKey(null, null), null);
  });
});
