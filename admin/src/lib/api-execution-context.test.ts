import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { shouldIncludeExecutionContext } from "./api-execution-context";

describe("shouldIncludeExecutionContext", () => {
  it("keeps identity endpoints independent from the selected tenant", () => {
    assert.equal(shouldIncludeExecutionContext("/auth/me"), false);
    assert.equal(shouldIncludeExecutionContext("auth/change-password"), false);
  });

  it("adds context to operational endpoints", () => {
    assert.equal(shouldIncludeExecutionContext("/commerce/v1/products"), true);
    assert.equal(shouldIncludeExecutionContext("/businesses"), true);
  });
});