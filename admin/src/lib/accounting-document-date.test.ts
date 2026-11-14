import assert from "node:assert/strict";
import { test } from "node:test";
import { accountingDateAtNoon, proposedAccountingDate, businessDateTimeAt, businessDateTimeInput, businessDateInput, addCalendarDays, businessDayBoundary } from "./accounting-document-date";

test("the proposed accounting date follows the business calendar across UTC midnight", () => {
  const now = new Date("2026-11-09T15:00:00Z");
  assert.equal(proposedAccountingDate("Pacific/Kiritimati", now), "2026-11-10T12:00:00+14:00");
  assert.equal(proposedAccountingDate("America/Bogota", now), "2026-11-09T12:00:00-05:00");
  assert.equal(proposedAccountingDate("Pacific/Honolulu", new Date("2026-11-10T01:00:00Z")), "2026-11-09T12:00:00-10:00");
});

test("payment and due dates use the selected date's offset, including daylight saving and fractional hours", () => {
  assert.equal(accountingDateAtNoon("2026-01-15", "America/New_York"), "2026-01-15T12:00:00-05:00");
  assert.equal(accountingDateAtNoon("2026-07-15", "America/New_York"), "2026-07-15T12:00:00-04:00");
  assert.equal(accountingDateAtNoon("2026-03-08", "America/New_York"), "2026-03-08T12:00:00-04:00");
  assert.equal(accountingDateAtNoon("2026-11-01", "America/New_York"), "2026-11-01T12:00:00-05:00");
  assert.equal(accountingDateAtNoon("2026-11-09", "Asia/Kathmandu"), "2026-11-09T12:00:00+05:45");
  assert.equal(accountingDateAtNoon("2026-11-09", "UTC"), "2026-11-09T12:00:00+00:00");
});

test("missing zones and invalid calendar dates never silently use the browser zone or another date", () => {
  assert.throws(() => proposedAccountingDate(undefined), /zona horaria/);
  assert.throws(() => accountingDateAtNoon("2026-11-09", ""), /zona horaria/);
  assert.throws(() => accountingDateAtNoon("2026-11-09", "Unknown/Zone"), RangeError);
  assert.throws(() => accountingDateAtNoon("2026-02-30", "America/Bogota"), /fecha válida/);
  assert.throws(() => accountingDateAtNoon("", "America/Bogota"), /fecha válida/);
  // Samoa skipped this calendar day when it crossed the date line.
  assert.throws(() => accountingDateAtNoon("2011-12-30", "Pacific/Apia"), /no existe/);
});


test("business datetime capture and reopening are independent of browser timezone", () => {
  assert.equal(businessDateTimeInput("", "America/Bogota"), "");
  assert.equal(businessDateTimeAt("2026-11-12T09:15", "Pacific/Kiritimati"), "2026-11-12T09:15:00+14:00");
  assert.equal(businessDateTimeAt("2026-11-11T20:30", "America/Bogota"), "2026-11-11T20:30:00-05:00");
  assert.equal(businessDateTimeInput("2026-11-12T01:30:00Z", "America/Bogota"), "2026-11-11T20:30");
  assert.equal(businessDateTimeInput("2026-11-12T01:30:00Z", "Asia/Tokyo"), "2026-11-12T10:30");
  assert.equal(businessDateInput("2026-11-12T01:30:00Z", "America/Bogota"), "2026-11-11");
  assert.equal(businessDateInput("2026-11-12", "America/Bogota"), "2026-11-12");
});

test("local time resolves its selected offset and rejects absent or ambiguous hours", () => {
  assert.equal(businessDateTimeAt("2026-03-08T03:30", "America/New_York"), "2026-03-08T03:30:00-04:00");
  assert.throws(() => businessDateTimeAt("2026-03-08T02:30", "America/New_York"), /no existe/);
  assert.throws(() => businessDateTimeAt("2026-11-01T01:30", "America/New_York"), /se repite/);
  // An ambiguous captured value remains editable; committing requires resolution.
  assert.equal(businessDateTimeInput("2026-11-01T01:30", "America/New_York"), "2026-11-01T01:30");
  const original = "2026-11-01T01:30:19.1234567-05:00";
  assert.equal(businessDateTimeAt(original, "America/New_York"), original);
  assert.equal(businessDateTimeInput(original, "America/New_York"), "2026-11-01T01:30");
});

test("calendar terms preserve their days across leap years and skipped browser dates", () => {
  assert.equal(addCalendarDays("2024-02-28", 1), "2024-02-29");
  assert.equal(addCalendarDays("2026-02-28", 1), "2026-03-01");
  assert.equal(addCalendarDays("2011-12-29", 1), "2011-12-30");
  assert.equal(addCalendarDays("2026-12-30", 7), "2027-01-06");
  assert.throws(() => addCalendarDays("2026-02-30", 1), /fecha válida/);
  assert.throws(() => businessDateTimeAt("2026-11-12T24:00", "America/Bogota"), /válidas/);
});


test("business day bounds include DST midnight gaps and skipped dates", () => {
  assert.equal(businessDayBoundary("2026-11-12", "America/Bogota"), "2026-11-12T05:00:00.000Z");
  assert.equal(businessDayBoundary("2026-03-08", "America/New_York"), "2026-03-08T05:00:00.000Z");
  assert.equal(businessDayBoundary("2026-03-09", "America/New_York"), "2026-03-09T04:00:00.000Z");
  assert.equal(businessDayBoundary("2026-11-01", "America/New_York"), "2026-11-01T04:00:00.000Z");
  assert.equal(businessDayBoundary("2026-11-02", "America/New_York"), "2026-11-02T05:00:00.000Z");
  assert.equal(businessDayBoundary("2018-11-04", "America/Sao_Paulo"), "2018-11-04T03:00:00.000Z");
  assert.equal(businessDayBoundary("2011-12-30", "Pacific/Apia"), businessDayBoundary("2011-12-31", "Pacific/Apia"));
});
