import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function formatCurrency(amount: number, currency = "COP", maximumFractionDigits?: number): string {
  return new Intl.NumberFormat("es-CO", {
    style: "currency",
    currency,
    minimumFractionDigits: 0,
    maximumFractionDigits,
  }).format(amount);
}

export function formatCurrencyFromCents(amountInCents: number, currency = "COP"): string {
  return formatCurrency(amountInCents / 100, currency);
}

/** Parses API instants; callers choose the display zone separately. */
export function parseApiDate(date: string | Date): Date {
  if (date instanceof Date) return date;

  const value = date.trim();
  const hasTimeZone = /(?:z|[+-]\d{2}:?\d{2})$/i.test(value);
  return new Date(hasTimeZone ? value : value + "Z");
}

export function formatDate(date: string | Date, timeZone?: string): string {
  // DateOnly is a calendar value, not UTC midnight to move into another day.
  const calendarDate = typeof date === "string" && /^\d{4}-\d{2}-\d{2}$/.test(date.trim());
  const instant = parseApiDate(date);
  if (calendarDate && instant.toISOString().slice(0, 10) !== date.trim())
    throw new RangeError("La fecha de calendario no es válida.");
  return new Intl.DateTimeFormat("es-CO", {
    timeZone: calendarDate ? "UTC" : timeZone,
    year: "numeric",
    month: "short",
    day: "numeric",
  }).format(instant);
}

export function formatDateTime(date: string | Date, timeZone?: string): string {
  return new Intl.DateTimeFormat("es-CO", {
    timeZone,
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(parseApiDate(date));
}

export function formatRelativeTime(date: string | Date | null | undefined): string {
  if (!date) return "—";
  const now = new Date();
  const d = parseApiDate(date);
  const diff = now.getTime() - d.getTime();
  const minutes = Math.floor(diff / 60000);
  const hours = Math.floor(diff / 3600000);
  const days = Math.floor(diff / 86400000);

  if (minutes < 1) return "Ahora";
  if (minutes < 60) return `Hace ${minutes}m`;
  if (hours < 24) return `Hace ${hours}h`;
  if (days < 7) return `Hace ${days}d`;
  return formatDate(date);
}

export function getInitials(name: string): string {
  return name
    .split(" ")
    .map((n) => n[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);
}

export function truncate(str: string, length: number): string {
  if (str.length <= length) return str;
  return str.slice(0, length) + "...";
}
