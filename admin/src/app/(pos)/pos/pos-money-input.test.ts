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

test("preserves a negative correction so it cannot become a positive payment", () => {
  assert.equal(formatMoneyDraft("-2000"), "-2.000");
  assert.equal(parseMoneyDraft("-2.000"), -2_000);
});

test("formats stored numeric values consistently", () => {
  assert.equal(formatMoneyValue(0), "0");
  assert.equal(formatMoneyValue(60_000), "60.000");
  assert.equal(formatMoneyValue(60_000.5), "60.000,50");
});


test("restaurant amounts retain four decimals without changing direct-sale defaults", () => {
  assert.equal(formatMoneyValue(7_545.6, 4), "7.545,60");
  assert.equal(formatMoneyValue(30_654.4, 4), "30.654,40");
  assert.equal(formatMoneyValue(1.2345, 4), "1,2345");
  assert.equal(formatMoneyDraft("7545,67891", 4), "7.545,6789");
  assert.equal(parseMoneyDraft(formatMoneyValue(7_545.6789, 4)), 7_545.6789);
  assert.equal(formatMoneyValue(1.2345), "1,23");
});
