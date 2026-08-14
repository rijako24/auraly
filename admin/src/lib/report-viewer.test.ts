import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { filterReportRows, safeReportFileName, toReportCsv, type ReportColumn } from "./report-viewer";

const columns: ReportColumn[] = [{ key: "name", label: "Nombre" }, { key: "quantity", label: "Cantidad" }];
const rows = [{ name: "Cliente, Norte", quantity: 2 }, { name: 'Tienda "Centro"', quantity: 3 }];

describe("central report viewer", () => {
  it("filters all visible columns without changing the source", () => {
    assert.deepEqual(filterReportRows(rows, columns, "centro"), [rows[1]]);
    assert.equal(rows.length, 2);
  });

  it("exports UTF-8 CSV escaping commas and quotes", () => {
    const csv = toReportCsv(rows, columns);
    assert.ok(csv.startsWith("\uFEFF"));
    assert.match(csv, /"Cliente, Norte"/);
    assert.match(csv, /"Tienda ""Centro"""/);
  });

  it("sanitizes file names for Windows, Android and iOS downloads", () => {
    assert.equal(safeReportFileName("Despacho: 001/2026"), "Despacho-001-2026");
  });
});
