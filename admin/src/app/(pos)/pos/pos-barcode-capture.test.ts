import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { parsePosBarcodeCapture } from "./pos-barcode-capture";

describe("parsePosBarcodeCapture", () => {
  it("preserves the current single-code capture", () => {
    assert.deepEqual(parsePosBarcodeCapture(" 30973482 "), { valid: true, code: "30973482", quantity: 1 });
  });
  it("reads an explicit quantity without changing the product code", () => {
    assert.deepEqual(parsePosBarcodeCapture(" 2,5 * 7701234567890 "), { valid: true, code: "7701234567890", quantity: 2.5 });
  });
  it("rejects malformed prefixes instead of searching them as barcodes", () => {
    assert.equal(parsePosBarcodeCapture("3**770123").valid, false);
  });
});
