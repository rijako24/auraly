export function requireBusinessTimeZone(timeZone: string | undefined): string {
  if (!timeZone) throw new RangeError("No se cargó la zona horaria del negocio. Vuelve a cargar el contexto de trabajo.");
  return timeZone;
}

function businessDateFormatter(timeZone: string | undefined) {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: requireBusinessTimeZone(timeZone), year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23",
  });
}

function localDateTime(formatter: Intl.DateTimeFormat, instant: Date) {
  const parts = Object.fromEntries(formatter.formatToParts(instant).map(part => [part.type, part.value]));
  return `${parts.year}-${parts.month}-${parts.day}T${parts.hour}:${parts.minute}:${parts.second}`;
}

function calendarValue(value: string) {
  const instant = Date.parse(`${value}T00:00:00Z`);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || !Number.isFinite(instant) ||
      new Date(instant).toISOString().slice(0, 10) !== value)
    throw new RangeError("Selecciona una fecha válida.");
  return instant;
}

export function addCalendarDays(date: string, days: number) {
  if (!Number.isInteger(days)) throw new RangeError("El plazo debe ser un número entero de días.");
  const result = new Date(calendarValue(date));
  result.setUTCDate(result.getUTCDate() + days);
  return result.toISOString().slice(0, 10);
}

const explicitOffset = /(?:z|[+-]\d{2}:?\d{2})$/i;

function capturedDateTime(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2})?$/.test(value))
    throw new RangeError("Selecciona una fecha y hora válidas.");
  calendarValue(value.slice(0, 10));
  const target = value.length === 16 ? `${value}:00` : value;
  const wallTime = Date.parse(`${target}Z`);
  if (!Number.isFinite(wallTime) || new Date(wallTime).toISOString().slice(0, 19) !== target)
    throw new RangeError("Selecciona una fecha y hora válidas.");
  return { target, wallTime };
}

// A captured local value is resolved once, at its selected date. Existing
// explicit instants retain their offset and precision when reopened/saved.
export function businessDateTimeAt(value: string, timeZone: string | undefined) {
  const formatter = businessDateFormatter(timeZone);
  if (explicitOffset.test(value)) {
    if (!Number.isFinite(Date.parse(value))) throw new RangeError("Selecciona una fecha válida.");
    return value;
  }
  const { target, wallTime } = capturedDateTime(value);
  // Probe both sides of a transition; a fixed, small number of in-memory
  // conversions also detects an ambiguous hour instead of silently choosing it.
  const candidates = new Set<number>();
  for (const delta of [-172_800_000, 0, 172_800_000]) {
    const probe = wallTime + delta;
    const offset = Date.parse(`${localDateTime(formatter, new Date(probe))}Z`) - probe;
    const candidate = wallTime - offset;
    if (localDateTime(formatter, new Date(candidate)) === target) candidates.add(candidate);
  }
  if (candidates.size === 0)
    throw new RangeError("La fecha seleccionada no existe en la zona horaria del negocio.");
  if (candidates.size !== 1)
    throw new RangeError("La hora seleccionada se repite por un cambio de horario. Elige otra hora o conserva el instante original.");
  const instant = candidates.values().next().value!;
  const offsetMinutes = (wallTime - instant) / 60_000;
  if (!Number.isInteger(offsetMinutes))
    throw new RangeError("La zona horaria no admite esa fecha con precisión de minutos.");
  const absolute = Math.abs(offsetMinutes);
  const hours = String(Math.floor(absolute / 60)).padStart(2, "0");
  const minutes = String(absolute % 60).padStart(2, "0");
  return `${target}${offsetMinutes < 0 ? "-" : "+"}${hours}:${minutes}`;
}

export function businessDateTimeInput(value: string | Date, timeZone: string | undefined) {
  if (value === "") return "";
  const formatter = businessDateFormatter(timeZone);
  if (typeof value === "string" && !explicitOffset.test(value)) {
    return capturedDateTime(value).target.slice(0, 16);
  }
  return localDateTime(formatter, value instanceof Date ? value : new Date(value)).slice(0, 16);
}

export function businessDateInput(value: string, timeZone: string | undefined) {
  if (!value) return "";
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) { calendarValue(value); return value; }
  return businessDateTimeInput(value, timeZone).slice(0, 10);
}

// First instant whose business calendar day is at least the requested day.
// This includes days with a missing midnight and empty days at date-line jumps.
export function businessDayBoundary(date: string, timeZone: string | undefined) {
  const wallTime = calendarValue(date);
  const formatter = businessDateFormatter(timeZone);
  let before = wallTime - 172_800_000, after = wallTime + 172_800_000;
  while (after - before > 1) {
    const middle = before + Math.floor((after - before) / 2);
    if (localDateTime(formatter, new Date(middle)).slice(0, 10) < date) before = middle;
    else after = middle;
  }
  return new Date(after).toISOString();
}

// Date-only financial capture retains the existing local-noon convention.
export function accountingDateAtNoon(date: string, timeZone: string | undefined) {
  calendarValue(date);
  return businessDateTimeAt(`${date}T12:00:00`, timeZone);
}

export function proposedAccountingDate(timeZone: string | undefined, now = new Date()) {
  const date = localDateTime(businessDateFormatter(timeZone), now).slice(0, 10);
  return accountingDateAtNoon(date, timeZone);
}
