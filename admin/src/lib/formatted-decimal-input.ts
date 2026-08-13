export function sanitizeDecimalInput(value: string, maximumFractionDigits = 4): string {
  const allowed = value.replace(/[^\d.]/g, "");
  if (!allowed) return "";

  const separator = allowed.indexOf(".");
  const integerSource = (separator < 0 ? allowed : allowed.slice(0, separator)).replace(/\D/g, "");
  const fractionSource = separator < 0
    ? ""
    : allowed.slice(separator + 1).replace(/\D/g, "").slice(0, maximumFractionDigits);
  const integer = (integerSource || "0").replace(/^0+(?=\d)/, "");
  return separator < 0 ? integer : `${integer}.${fractionSource}`;
}

export function formatDecimalInput(value: string, maximumFractionDigits = 4): string {
  const sanitized = sanitizeDecimalInput(value, maximumFractionDigits);
  if (!sanitized) return "";
  const [integer, fraction] = sanitized.split(".");
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, " ");
  return sanitized.includes(".") ? `${grouped}.${fraction ?? ""}` : grouped;
}

export function parseDecimalInput(value: string): number | null {
  const sanitized = sanitizeDecimalInput(value);
  if (!sanitized || sanitized === ".") return null;
  const parsed = Number(sanitized);
  return Number.isFinite(parsed) ? parsed : null;
}

export function decimalInputFromNumber(value: string | number): string {
  if (typeof value === "number") return Number.isFinite(value) ? String(value) : "";
  if (!value.trim()) return "";
  const parsed = Number(value);
  return Number.isFinite(parsed) ? String(parsed) : "";
}
