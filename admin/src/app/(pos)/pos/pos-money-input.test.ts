import assert from "node:assert/strict";
import test from "node:test";

import {
  formatMoneyDraft,
  formatMoneyValue,
  parseMoneyDraft,
} from "./pos-money-input";

test("groups Colombian pesos while the cashier types", () => {
  assert.equal(formatMoneyDraft("6"), "6");
  assert.equal(formatMoneyDraft("60000"), "60.000");
  assert.equal(formatMoneyDraft("60.0000"), "600.000");
});

test("preserves an optional Colombian decimal draft", () => {
  assert.equal(formatMoneyDraft("60000,"), "60.000,");
  assert.equal(formatMoneyDraft("60000,5"), "60.000,5");
  assert.equal(formatMoneyDraft("60000,567"), "60.000,56");
});

test("parses the formatted amount used by settlement calculations", () => {
  assert.equal(parseMoneyDraft("60.000"), 60_000);
  assert.equal(parseMoneyDraft("$ 60.000,50"), 60_000.5);
});

test("formats stored numeric values consistently", () => {
  assert.equal(formatMoneyValue(0), "0");
  assert.equal(formatMoneyValue(60_000), "60.000");
  assert.equal(formatMoneyValue(60_000.5), "60.000,50");
});
