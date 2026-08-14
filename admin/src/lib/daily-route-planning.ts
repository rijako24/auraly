export type IdentifiedStop = { routeStopId: string };
export type IdentifiedVisit = { routeStopId: string };

export function isoScheduleDay(browserDay: number) {
  if (!Number.isInteger(browserDay) || browserDay < 0 || browserDay > 6)
    throw new RangeError("Browser day must be between 0 and 6.");
  return browserDay === 0 ? 7 : browserDay;
}

export function pendingRouteStops<T extends IdentifiedStop>(stops: readonly T[], visits: readonly IdentifiedVisit[]) {
  const completed = new Set(visits.map((visit) => visit.routeStopId));
  return stops.filter((stop) => !completed.has(stop.routeStopId));
}

export function firstPendingRouteStop<T extends IdentifiedStop>(stops: readonly T[], visits: readonly IdentifiedVisit[]) {
  return pendingRouteStops(stops, visits)[0] ?? null;
}
