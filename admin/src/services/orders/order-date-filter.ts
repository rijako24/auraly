import { addCalendarDays, businessDateTimeInput, businessDayBoundary } from "../../lib/accounting-document-date";

export function localOrderDateValue(timeZone: string, now = new Date()): string {
  return businessDateTimeInput(now, timeZone).slice(0, 10);
}

export function orderDayRange(value: string, timeZone: string): {
  createdFrom: string;
  createdTo: string;
} {
  return {
    createdFrom: businessDayBoundary(value, timeZone),
    createdTo: businessDayBoundary(addCalendarDays(value, 1), timeZone),
  };
}
