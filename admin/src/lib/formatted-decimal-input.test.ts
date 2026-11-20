import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { formatDecimalInput, parseDecimalInput, sanitizeDecimalInput } from "./formatted-decimal-input";

describe("formatted decimal input", () => {
  it("preserves a leading minus while a negative percentage is being typed", () => {
    assert.equal(sanitizeDecimalInput("-", 4, true), "-");
    assert.equal(formatDecimalInput("-", 4, true), "-");
    assert.equal(parseDecimalInput("-", true), null);
    assert.equal(parseDecimalInput("-12.5", true), -12.5);
  });
  it("accepts digits and one decimal point and formats thousands", () => {
    assert.equal(sanitizeDecimalInput("12500.50"), "12500.50");
    assert.equal(formatDecimalInput("12500.50"), "12 500.50");
    assert.equal(parseDecimalInput("12 500.50"), 12500.5);
  });

  it("discards letters and grouping commas before a decimal point", () => {
    assert.equal(sanitizeDecimalInput("$ 12a,500x.75 COP"), "12500.75");
    assert.equal(formatDecimalInput("$ 12a,500x.75 COP"), "12 500.75");
  });

  it("accepts the mobile decimal comma without multiplying the amount", () => {
    assert.equal(parseDecimalInput("1750,1254"), 1750.1254);
    assert.equal(formatDecimalInput("1750,1254"), "1 750.1254");
    assert.equal(parseDecimalInput("0,25"), 0.25);
    assert.equal(parseDecimalInput("-12,5", true), -12.5);
  });

  it("accepts pasted currency grouping in either supported notation", () => {
    assert.equal(parseDecimalInput("$ 1.750,1254 COP"), 1750.1254);
    assert.equal(parseDecimalInput("$ 1,750.1254 COP"), 1750.1254);
    assert.equal(parseDecimalInput("1 750.1254"), 1750.1254);
    assert.equal(parseDecimalInput("1750.1254"), 1750.1254);
  });

  it("keeps only one decimal point and limits the fraction", () => {
    assert.equal(sanitizeDecimalInput("12.34.56789"), "12.3456");
  });
});
