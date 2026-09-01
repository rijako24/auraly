import assert from "node:assert/strict";
import test from "node:test";
import { canOpenFiscalResolutionChange } from "./fiscal-device-resolution-state";

test("an assigned active device can open resolution change before choosing a replacement", () => {
  assert.equal(canOpenFiscalResolutionChange(true, true, true, false), true);
});

test("resolution change remains protected by management, habilitation and active device state", () => {
  assert.equal(canOpenFiscalResolutionChange(false, true, true, false), false);
  assert.equal(canOpenFiscalResolutionChange(true, false, true, false), false);
  assert.equal(canOpenFiscalResolutionChange(true, true, false, false), false);
  assert.equal(canOpenFiscalResolutionChange(true, true, true, true), false);
});
