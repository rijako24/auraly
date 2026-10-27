export function proposedAccountingDate() {
  const date = new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/Bogota", year: "numeric", month: "2-digit", day: "2-digit",
  }).format(new Date());
  return `${date}T12:00:00-05:00`;
}
