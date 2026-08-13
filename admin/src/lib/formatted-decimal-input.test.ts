import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { formatDecimalInput, parseDecimalInput, sanitizeDecimalInput } from "./formatted-decimal-input";

describe("formatted decimal input", () => {
  it("accepts digits and one decimal point and formats thousands", () => {
    assert.equal(sanitizeDecimalInput("12500.50"), "12500.50");
    assert.equal(formatDecimalInput("12500.50"), "12 500.50");
    assert.equal(parseDecimalInput("12 500.50"), 12500.5);
  });

  it("discards letters and commas", () => {
    assert.equal(sanitizeDecimalInput("$ 12a,500x.75 COP"), "12500.75");
    assert.equal(formatDecimalInput("$ 12a,500x.75 COP"), "12 500.75");
  });

  it("keeps only one decimal point and limits the fraction", () => {
    assert.equal(sanitizeDecimalInput("12.34.56789"), "12.3456");
  });
});
