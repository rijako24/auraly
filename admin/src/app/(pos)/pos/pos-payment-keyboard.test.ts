import assert from "node:assert/strict";
import test from "node:test";

import {
  documentTypeForShortcut,
  isChangeDocumentShortcut,
  nextPaymentAmountIndex,
} from "./pos-payment-keyboard";

test("up and down move only between received-value rows", () => {
  assert.equal(nextPaymentAmountIndex(1, 3, "ArrowUp"), 0);
  assert.equal(nextPaymentAmountIndex(1, 3, "ArrowDown"), 2);
  assert.equal(nextPaymentAmountIndex(0, 3, "ArrowUp"), 0);
  assert.equal(nextPaymentAmountIndex(2, 3, "ArrowDown"), 2);
  assert.equal(nextPaymentAmountIndex(1, 3, "Enter"), null);
});

test("F6 opens document selection unless the document is locked", () => {
  assert.equal(isChangeDocumentShortcut("F6", false), true);
  assert.equal(isChangeDocumentShortcut("F6", true), false);
});

test("document shortcuts map F1 to invoice and F2 to receipt", () => {
  assert.equal(documentTypeForShortcut("F1"), "SalesInvoice");
  assert.equal(documentTypeForShortcut("F2"), "SalesReceipt");
  assert.equal(documentTypeForShortcut("F3"), null);
});
