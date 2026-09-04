import assert from "node:assert/strict";
import test from "node:test";

import {
  calculateTenantVerificationDigit,
  supportsTenantVerificationDigit,
} from "./tenant-legal-identity";

test("calculates the same immutable verification digit for NIT and natural-person numeric IDs", () => {
  assert.equal(calculateTenantVerificationDigit("NIT", "1065658655"), 6);
  assert.equal(calculateTenantVerificationDigit("CC", "1065658655"), 6);
});

test("does not invent a verification digit for alphanumeric identification types", () => {
  assert.equal(supportsTenantVerificationDigit("PA"), false);
  assert.equal(calculateTenantVerificationDigit("PA", "AB12345"), null);
});

test("waits for a complete numeric identification before calculating", () => {
  assert.equal(calculateTenantVerificationDigit("CC", "12"), null);
});
