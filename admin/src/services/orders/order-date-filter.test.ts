import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { localOrderDateValue, orderDayRange } from "./order-date-filter";

describe("order date filter", () => {
  it("uses the site day for both badge and workspace across a UTC date boundary", () => {
    const today = localOrderDateValue("America/Bogota", new Date("2026-09-13T03:59:00Z"));
    assert.equal(today, "2026-09-12");
    assert.deepEqual(orderDayRange(today, "America/Bogota"), {
      createdFrom: "2026-09-12T05:00:00.000Z",
      createdTo: "2026-09-13T05:00:00.000Z",
    });
  });

  it("uses calendar boundaries for a 23-hour and a 25-hour site day", () => {
    for (const [day, hours] of [["2026-03-08", 23], ["2026-11-01", 25]] as const) {
      const range = orderDayRange(day, "America/New_York");
      assert.equal(Date.parse(range.createdTo) - Date.parse(range.createdFrom), hours * 60 * 60 * 1000);
    }
  });

  it("rejects an invalid site or date instead of choosing the computer zone", () => {
    assert.throws(() => orderDayRange("2026-02-30", "America/Bogota"), RangeError);
    assert.throws(() => localOrderDateValue(""), RangeError);
  });
});
