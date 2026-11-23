import assert from "node:assert/strict";
import test from "node:test";
import type { AccountingOpeningBalance } from "../services/api/accounting";
import { emptyOpeningLine, openingCaptureMatchesServer, openingCaptureSignature, openingRecoveryKey,
  openingRecoveryScope, readOpeningCapture, validOpeningDate, type StoredOpeningCapture } from "./accounting-opening-recovery";

test("isolates opening recovery by tenant, actor, business and effective date", () => {
  const keys = [["tenant-1", "user-1", "site-1", "2026-01-01"], ["tenant-2", "user-1", "site-1", "2026-01-01"],
    ["tenant-1", "user-2", "site-1", "2026-01-01"], ["tenant-1", "user-1", "site-2", "2026-01-01"],
    ["tenant-1", "user-1", "site-1", "2026-02-01"]].map(([tenant, user, site, date]) => openingRecoveryKey(openingRecoveryScope(tenant, user, site), date));
  assert.equal(new Set(keys).size, 5);
});

test("recovers incomplete capture and rejects foreign, corrupt or oversized snapshots", () => {
  const draft: StoredOpeningCapture = { version: 1, kind: "capture", scope: "own-scope", effectiveOn: "2026-01-01",
    capture: { batchId: "own-batch", rowVersion: null, description: "", lines: [emptyOpeningLine()] } };
  assert.equal(readOpeningCapture(draft, draft.scope, draft.effectiveOn), draft);
  assert.equal(readOpeningCapture(draft, "another-user", draft.effectiveOn), null);
  assert.equal(readOpeningCapture(draft, draft.scope, "2026-02-01"), null);
  assert.equal(readOpeningCapture({ ...draft, version: 2 }, draft.scope, draft.effectiveOn), null);
  assert.equal(readOpeningCapture({ ...draft, capture: { ...draft.capture, rowVersion: undefined } }, draft.scope, draft.effectiveOn), null);
  assert.equal(readOpeningCapture({ ...draft, capture: { ...draft.capture, lines: Array.from({ length: 501 }, emptyOpeningLine) } }, draft.scope, draft.effectiveOn), null);
  assert.equal(readOpeningCapture({ ...draft, capture: { ...draft.capture, lines: [{ ...draft.capture.lines[0], partyName: {} }] } }, draft.scope, draft.effectiveOn), null);
});

test("does not silently replace a newer or different server draft", () => {
  const capture = { batchId: "batch-1", rowVersion: "version-1", description: "Apertura", lines: [emptyOpeningLine()] };
  const server = { batchId: "batch-1", rowVersion: "version-1" } as AccountingOpeningBalance;
  assert.equal(openingCaptureMatchesServer(capture, server), true);
  assert.equal(openingCaptureMatchesServer(capture, { ...server, rowVersion: "version-2" }), false);
  assert.equal(openingCaptureMatchesServer(capture, { ...server, batchId: "batch-2" }), false);
  assert.equal(openingCaptureMatchesServer(capture, null), false);
  assert.equal(openingCaptureMatchesServer({ ...capture, rowVersion: null }, null), true);
});

test("display labels do not mark the journal dirty but invoice and amount edits do", () => {
  const capture = { description: "Apertura", lines: [{ ...emptyOpeningLine(), debit: "125.5000", partyName: "Nombre" }] };
  assert.equal(openingCaptureSignature(capture), openingCaptureSignature({ ...capture, lines: [{ ...capture.lines[0], debit: "125.5", partyName: "Etiqueta actualizada" }] }));
  assert.notEqual(openingCaptureSignature(capture), openingCaptureSignature({ ...capture, lines: [{ ...capture.lines[0], debit: "126" }] }));
  assert.notEqual(openingCaptureSignature(capture), openingCaptureSignature({ ...capture, lines: [{ ...capture.lines[0], newInvoice: { documentNumber: "INI-1", issuedOn: "2025-01-01", dueOn: "2025-02-01", partySiteId: "site-1" } }] }));
});

test("recovers only valid SQL calendar dates", () => {
  assert.equal(validOpeningDate("2026-11-19"), true);
  for (const invalid of ["2026-02-30", "0000-01-01", "2026-11-19T12:00:00Z", "", undefined]) assert.equal(validOpeningDate(invalid), false);
});


test("recovers initial advance evidence and detects changes without persisting display labels", () => {
  const line={...emptyOpeningLine(),newAdvance:{reference:"ANT-1",issuedOn:"2025-12-01",partySiteId:"site-1"},credit:"1000.1254"};
  const capture={batchId:"batch-1",rowVersion:null,description:"Anticipos iniciales",lines:[line]};
  const stored:StoredOpeningCapture={version:1,kind:"capture",scope:"own",effectiveOn:"2026-01-01",capture};
  assert.equal(readOpeningCapture(stored,"own",stored.effectiveOn),stored);
  assert.equal(openingCaptureSignature(capture),openingCaptureSignature({...capture,lines:[{...line,newAdvancePartySiteName:"Sede"}]}));
  assert.notEqual(openingCaptureSignature(capture),openingCaptureSignature({...capture,lines:[{...line,newAdvance:{...line.newAdvance,reference:"ANT-2"}}]}));
  assert.equal(readOpeningCapture({...stored,capture:{...capture,lines:[{...line,newAdvance:{reference:123}}]}},"own",stored.effectiveOn),null);
});
