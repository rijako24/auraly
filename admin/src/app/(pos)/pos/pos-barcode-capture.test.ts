import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { parsePosBarcodeCapture, submitPosCaptureOnEnter } from "./pos-barcode-capture";

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

describe("submitPosCaptureOnEnter", () => {
  it("owns the scanner Enter contract instead of relying on implicit WebView submission", () => {
    let prevented = false;
    let submitted = false;
    const handled = submitPosCaptureOnEnter({
      key: "Enter",
      preventDefault: () => { prevented = true; },
      currentTarget: { form: { requestSubmit: () => { submitted = true; } } },
    });

    assert.equal(handled, true);
    assert.equal(prevented, true);
    assert.equal(submitted, true);
  });

  it("does not consume navigation keys", () => {
    let prevented = false;
    const handled = submitPosCaptureOnEnter({
      key: "ArrowDown",
      preventDefault: () => { prevented = true; },
      currentTarget: { form: null },
    });

    assert.equal(handled, false);
    assert.equal(prevented, false);
  });
});
