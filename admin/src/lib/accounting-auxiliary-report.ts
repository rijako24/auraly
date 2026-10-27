import type { AccountMovementPage } from "@/services/api/accounting";
import { accountingDocumentTypeLabel } from "./accounting-labels";
import { parseApiDate } from "./utils";
import type { ReportColumn, ReportRow } from "./report-viewer";

const money = new Intl.NumberFormat("es-CO", {
  style: "currency", currency: "COP", minimumFractionDigits: 2, maximumFractionDigits: 4,
});
const amount: ReportColumn["format"] = value => value == null ? "" : money.format(Number(value));
const date = new Intl.DateTimeFormat("es-CO", { day: "2-digit", month: "2-digit", year: "numeric" });
const numberFormat = "#,##0.00##";

export const auxiliaryReportColumns: ReportColumn[] = [
  { key: "account", label: "Cuenta" },
  { key: "date", label: "Fecha" },
  { key: "number", label: "Número" },
  { key: "document", label: "Documento / referencia" },
  { key: "identification", label: "Identificación" },
  { key: "party", label: "Tercero" },
  { key: "description", label: "Observación" },
  { key: "center", label: "Centro de costo" },
  { key: "base", label: "Base registrada", align: "right", numberFormat, format: value => value == null ? "No registrada" : money.format(Number(value)) },
  { key: "debit", label: "Débito", align: "right", numberFormat, format: amount },
  { key: "credit", label: "Crédito", align: "right", numberFormat, format: amount },
  { key: "balance", label: "Saldo", align: "right", numberFormat, format: amount },
];

// Presentation only: all amounts, running balances and totals come from the
// same complete server snapshot. A page cannot masquerade as a full report.
export function auxiliaryReportRows(page: AccountMovementPage): ReportRow[] {
  if (page.page !== 1 || page.items.length !== page.totalCount || page.totalPages > 1)
    throw new Error("El auxiliar recibido está incompleto. Vuelve a preparar el informe antes de exportarlo.");
  const accountCodes = new Set(page.accounts.map(account => account.accountCode));
  const grouped = new Map<string, ReportRow[]>();
  for (const item of page.items) {
    if (!item.accountCode || !accountCodes.has(item.accountCode))
      throw new Error("Una partida del auxiliar no tiene su saldo por cuenta. Vuelve a preparar el informe.");
    const rows = grouped.get(item.accountCode) ?? [];
    rows.push({ id: `${item.entryId}-${item.lineNumber}`, account: item.accountCode, date: date.format(parseApiDate(item.occurredAt)), number: item.entryNumber,
      document: [item.sourceDocumentNumber ?? accountingDocumentTypeLabel(item.sourceDocumentType), item.reference].filter(Boolean).join(" · "),
      identification: item.partyIdentification ?? "", party: item.partyName ?? "Sin tercero histórico",
      description: item.description, center: item.costCenterCode ? `${item.costCenterCode} · ${item.costCenterName ?? ""}` : "Sin clasificación histórica",
      base: item.baseAmount, debit: item.debit, credit: item.credit, balance: item.balance });
    grouped.set(item.accountCode, rows);
  }
  return page.accounts.flatMap(account => [
    { id: `account-${account.accountCode}`, __group: `${account.accountCode} · ${account.accountName} · Saldo anterior: ${money.format(account.openingBalance)}` },
    ...(grouped.get(account.accountCode) ?? []),
    { id: `total-${account.accountCode}`, account: account.accountCode, description: `Total cuenta ${account.accountCode}`,
      base: account.baseAmount, debit: account.debit, credit: account.credit, balance: account.closingBalance },
  ]);
}
