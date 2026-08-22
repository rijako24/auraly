export function sanitizeDecimalInput(value: string, maximumFractionDigits = 4, allowNegative = false): string {
  const negative = allowNegative && value.trimStart().startsWith("-");
  const allowed = value.replace(/[^\d.]/g, "");
  if (!allowed) return negative ? "-" : "";

  const separator = allowed.indexOf(".");
  const integerSource = (separator < 0 ? allowed : allowed.slice(0, separator)).replace(/\D/g, "");
  const fractionSource = separator < 0
    ? ""
    : allowed.slice(separator + 1).replace(/\D/g, "").slice(0, maximumFractionDigits);
  const integer = (integerSource || "0").replace(/^0+(?=\d)/, "");
  const result = separator < 0 ? integer : `${integer}.${fractionSource}`;
  return negative ? `-${result}` : result;
}

export function formatDecimalInput(value: string, maximumFractionDigits = 4, allowNegative = false): string {
  const sanitized = sanitizeDecimalInput(value, maximumFractionDigits, allowNegative);
  if (!sanitized) return "";
  if (sanitized === "-") return sanitized;
  const negative = sanitized.startsWith("-");
  const unsigned = negative ? sanitized.slice(1) : sanitized;
  const [integer, fraction] = unsigned.split(".");
  const grouped = integer.replace(/\B(?=(\d{3})+(?!\d))/g, " ");
  const result = unsigned.includes(".") ? `${grouped}.${fraction ?? ""}` : grouped;
  return negative ? `-${result}` : result;
}

export function parseDecimalInput(value: string, allowNegative = false): number | null {
  const sanitized = sanitizeDecimalInput(value, 4, allowNegative);
  if (!sanitized || sanitized === "." || sanitized === "-") return null;
  const parsed = Number(sanitized);
  return Number.isFinite(parsed) ? parsed : null;
}

export function decimalInputFromNumber(value: string | number): string {
  if (typeof value === "number") return Number.isFinite(value) ? String(value) : "";
  if (!value.trim()) return "";
  const parsed = Number(value);
  return Number.isFinite(parsed) ? String(parsed) : "";
}
