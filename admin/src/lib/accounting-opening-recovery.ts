import type { AccountingOpeningBalance, AccountingOpeningBalanceLine, AccountingOpeningInvoice, AccountingOpeningAdvance } from "../services/api/accounting";
import { catalogDraftKey } from "./catalog-draft-store";

export type OpeningDraftLine = {
  accountId: string; partyId: string | null; costCenterId: string | null; description: string;
  debit: string; credit: string; lineId: string; existingPosition: AccountingOpeningBalanceLine["existingPosition"];
  existingPositionDocumentNumber?: string | null; newInvoice: AccountingOpeningInvoice | null;
  newInvoicePartySiteName?: string | null; newAdvance?: AccountingOpeningAdvance | null; newAdvancePartySiteName?: string | null; partyName?: string | null; partyIdentification?: string | null;
};
export type OpeningCapture = { batchId: string; rowVersion: string | null; description: string; lines: OpeningDraftLine[] };
export type StoredOpeningCapture = { version: 1; kind: "capture"; scope: string; effectiveOn: string; capture: OpeningCapture };

export const openingRecoveryScope = (tenantId: string, userId: string, businessId: string) =>
  catalogDraftKey("accounting-opening", userId, businessId, tenantId);
export const openingRecoveryKey = (scope: string, effectiveOn: string) => `${scope}:${effectiveOn}`;
export const emptyOpeningLine = (): OpeningDraftLine => ({ lineId: crypto.randomUUID(), existingPosition: null, newInvoice: null, newAdvance: null,
  accountId: "", partyId: null, costCenterId: null, description: "Saldo inicial", debit: "", credit: "" });

export function openingCapture(server: AccountingOpeningBalance | null, effectiveOn: string): OpeningCapture {
  return server ? { batchId: server.batchId, rowVersion: server.rowVersion, description: server.description,
    lines: server.lines.map(line => ({ ...line, lineId: line.lineId ?? crypto.randomUUID(),
      existingPosition: line.existingPosition ?? null, newInvoice: line.newInvoice ?? null, newAdvance: line.newAdvance ?? null,
      debit: line.debit ? String(line.debit) : "", credit: line.credit ? String(line.credit) : "" })) }
    : { batchId: crypto.randomUUID(), rowVersion: null, description: `Saldos iniciales al ${effectiveOn}`, lines: [emptyOpeningLine(), emptyOpeningLine()] };
}

// One comparison owns unsaved capture state; labels are display metadata only.
export function openingCaptureSignature(value: Pick<OpeningCapture, "description" | "lines">) {
  return JSON.stringify([value.description.trim(), value.lines.map(line => [line.accountId, line.partyId, line.costCenterId,
    line.description.trim(), Number(line.debit || 0), Number(line.credit || 0), line.existingPosition ?? null,
    line.newInvoice ? [line.newInvoice.documentNumber.trim(), line.newInvoice.issuedOn, line.newInvoice.dueOn, line.newInvoice.partySiteId] : null,
    line.newAdvance ? [line.newAdvance.reference.trim(),line.newAdvance.issuedOn,line.newAdvance.partySiteId] : null])]);
}

const record = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null;
const nullableString = (value: unknown) => value == null || typeof value === "string";
export function validOpeningDate(value: unknown): value is string {
  if (typeof value !== "string" || value < "0001-01-01" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T12:00:00Z`);
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
}
function validLine(value: unknown): value is OpeningDraftLine {
  if (!record(value) || !["accountId", "description", "debit", "credit", "lineId"].every(key => typeof value[key] === "string")) return false;
  if (!["partyId", "costCenterId", "partyName", "partyIdentification", "newInvoicePartySiteName", "newAdvancePartySiteName", "existingPositionDocumentNumber"].every(key => nullableString(value[key]))) return false;
  const position = value.existingPosition, invoice = value.newInvoice, advance = value.newAdvance;
  return (position == null || record(position) && ["Payable", "Receivable", "SupplierCredit", "CustomerCredit"].includes(String(position.subledgerKind)) && typeof position.subledgerId === "string") &&
    (invoice === null || record(invoice) && ["documentNumber", "issuedOn", "dueOn", "partySiteId"].every(key => typeof invoice[key] === "string")) &&
    (advance == null || record(advance) && ["reference", "issuedOn", "partySiteId"].every(key => typeof advance[key] === "string"));
}
export function readOpeningCapture(value: unknown, scope: string, effectiveOn: string): StoredOpeningCapture | null {
  if (!record(value) || value.version !== 1 || value.kind !== "capture" || value.scope !== scope || value.effectiveOn !== effectiveOn || !record(value.capture)) return null;
  const capture = value.capture;
  if (typeof capture.batchId !== "string" || !(capture.rowVersion === null || typeof capture.rowVersion === "string") || typeof capture.description !== "string" ||
    !Array.isArray(capture.lines) || capture.lines.length < 1 || capture.lines.length > 500 || !capture.lines.every(validLine)) return null;
  return value as StoredOpeningCapture;
}
export function openingCaptureMatchesServer(capture: OpeningCapture, server: AccountingOpeningBalance | null) {
  return capture.rowVersion === (server?.rowVersion ?? null) && (!server || capture.batchId === server.batchId);
}
