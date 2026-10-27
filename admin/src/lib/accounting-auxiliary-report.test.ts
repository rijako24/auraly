import assert from "node:assert/strict";
import { it } from "node:test";
import type { AccountMovementPage } from "@/services/api/accounting";
import { auxiliaryReportColumns, auxiliaryReportRows } from "./accounting-auxiliary-report";
import { reportCellText } from "./report-viewer";

const snapshot: AccountMovementPage = {
  page: 1, pageSize: 5000, totalCount: 1, totalPages: 1,
  accounts: [{ accountCode: "13300501", accountName: "Anticipos", openingBalance: 50,
    debit: 0, credit: 20.1234, closingBalance: 29.8766, movementCount: 1, baseAmount: 0 },
  { accountCode: "23050501", accountName: "Saldo sin movimientos", openingBalance: -40,
    debit: 0, credit: 0, closingBalance: -40, movementCount: 0, baseAmount: null }],
  items: [{ entryId: "entry", lineNumber: 1, entryNumber: "CONT-1", sourceDocumentId: "document",
    sourceDocumentType: "ManualAccountingVoucher", sourceDocumentNumber: "COM-1", reference: "FACT-2",
    occurredAt: "2026-10-20T12:00:00Z", accountCode: "13300501", accountName: "Anticipos",
    partyId: "party", partyIdentification: "001234", partyName: "Tercero de la partida", description: "Cruce del anticipo",
    costCenterId: null, costCenterCode: null, costCenterName: null, baseAmount: 0, debit: 0, credit: 20.1234, balance: 29.8766 }],
};

it("keeps the authoritative running balance, party, zero base and opening-only accounts in the full report", () => {
  const rows = auxiliaryReportRows(snapshot);
  assert.equal(rows.length, 5);
  assert.match(String(rows[0].__group), /13300501.*Saldo anterior/);
  assert.equal(rows[1].party, "Tercero de la partida");
  assert.equal(rows[1].identification, "001234");
  assert.equal(rows[1].document, "COM-1 · FACT-2");
  assert.equal(rows[1].balance, 29.8766);
  assert.equal(rows[2].balance, snapshot.accounts[0].closingBalance);
  assert.equal(rows[4].balance, -40);
  const base = auxiliaryReportColumns.find(column => column.key === "base")!;
  assert.notEqual(reportCellText(base, rows[1]), "No registrada");
  assert.equal(reportCellText(base, rows[4]), "No registrada");
});

it("refuses a partial page or a movement without its account summary instead of exporting an incomplete book", () => {
  assert.throws(() => auxiliaryReportRows({ ...snapshot, totalCount: 201, totalPages: 5 }), /incompleto/);
  assert.throws(() => auxiliaryReportRows({ ...snapshot, accounts: [] }), /saldo por cuenta/);
});
