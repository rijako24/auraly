import assert from "node:assert/strict";
import { test } from "node:test";
import { formatDate, formatDateTime, parseApiDate } from "./utils";

test("calendar dates keep their day in every display zone", () => {
  for (const zone of ["America/Bogota", "Pacific/Kiritimati", "Pacific/Honolulu", "America/New_York"])
    assert.equal(formatDate("2026-11-11", zone), formatDate("2026-11-11", "UTC"));
  assert.equal(formatDate("2026-02-28", "America/Bogota"), formatDate("2026-02-28", "UTC"));
});

test("instants display the business day instead of the browser day", () => {
  const instant = "2026-11-12T01:30:00Z";
  assert.equal(formatDate(instant, "America/Bogota"), formatDate("2026-11-11", "UTC"));
  assert.equal(formatDate(instant, "Asia/Tokyo"), formatDate("2026-11-12", "UTC"));
});

test("equivalent API instants retain one value and one local representation", () => {
  const utc = "2026-03-08T07:30:00Z", offset = "2026-03-08T03:30:00-04:00";
  assert.equal(parseApiDate(utc).getTime(), parseApiDate(offset).getTime());
  assert.equal(formatDateTime(utc, "America/New_York"), formatDateTime(offset, "America/New_York"));
  assert.notEqual(formatDateTime(utc, "America/New_York"), formatDateTime(utc, "UTC"));
});

test("invalid calendar values and configured zones are rejected", () => {
  assert.throws(() => formatDate("2026-02-30", "America/Bogota"), RangeError);
  assert.throws(() => formatDate("2026-11-11T00:00:00Z", "Unknown/Zone"), RangeError);
});
