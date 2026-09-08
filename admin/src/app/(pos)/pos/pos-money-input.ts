const wholeNumber = new Intl.NumberFormat("es-CO", {
  maximumFractionDigits: 0,
  useGrouping: true,
});

export function formatMoneyValue(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return "0";
  const rounded = Math.round((value + Number.EPSILON) * 100) / 100;
  const integer = Math.trunc(rounded);
  const fraction = Math.round((rounded - integer) * 100);
  const base = wholeNumber.format(integer);
  return fraction > 0
    ? `${base},${String(fraction).padStart(2, "0")}`
    : base;
}

export function formatMoneyDraft(raw: string): string {
  const negative = raw.trimStart().startsWith("-");
  const normalized = raw
    .replace(/\s/g, "")
    .replace(/\$/g, "")
    .replace(/\./g, "")
    .replace(/[^0-9,]/g, "");
  const comma = normalized.indexOf(",");
  const integerPart = (comma >= 0 ? normalized.slice(0, comma) : normalized)
    .replace(/^0+(?=\d)/, "");
  const fractionPart = comma >= 0
    ? normalized.slice(comma + 1).replace(/,/g, "").slice(0, 2)
    : null;
  const integer = integerPart || "0";
  const grouped = wholeNumber.format(Number(integer));
  const formatted = fractionPart === null ? grouped : `${grouped},${fractionPart}`;
  return negative ? `-${formatted}` : formatted;
}

export function parseMoneyDraft(value: string): number {
  const negative = value.trimStart().startsWith("-");
  const normalized = value
    .replace(/\s/g, "")
    .replace(/\$/g, "")
    .replace(/\./g, "")
    .replace(",", ".")
    .replace(/[^0-9.]/g, "");
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? (negative ? -parsed : parsed) : 0;
}
