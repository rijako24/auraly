import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { localOrderDateValue, orderDayRange } from "./order-date-filter";

describe("order date filter", () => {
  it("uses the same local day boundary for the initial badge and workspace", () => {
    const today = localOrderDateValue(new Date(2026, 8, 12, 23, 59, 0));
    const range = orderDayRange(today);

    assert.equal(today, "2026-09-12");
    assert.equal(
      new Date(range.createdTo).getTime() - new Date(range.createdFrom).getTime(),
      24 * 60 * 60 * 1000,
    );
  });
});
