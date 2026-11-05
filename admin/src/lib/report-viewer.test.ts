import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { commercialReportMoney, filterReportRows, safeReportFileName, toReportCsv, type ReportColumn } from "./report-viewer";

const columns: ReportColumn[] = [{ key: "name", label: "Nombre" }, { key: "quantity", label: "Cantidad" }];
const rows = [{ name: "Cliente, Norte", quantity: 2 }, { name: 'Tienda "Centro"', quantity: 3 }];

describe("central report viewer", () => {
  it("displays projected money with two decimals while preserving fractional amounts in exports", () => {
    for (const [amount, fraction] of [[350598.0291, "03"], [6938250.0009, "00"], [288488.0733, "07"]] as const) {
      assert.equal(commercialReportMoney.formatToParts(amount).find(part => part.type === "fraction")?.value, fraction);
    }
    assert.ok(toReportCsv([{ quantity: 350598.0291 }], columns).includes("350598.0291"));
  });

  it("filters all visible columns without changing the source", () => {
    assert.deepEqual(filterReportRows(rows, columns, "centro"), [rows[1]]);
    assert.equal(rows.length, 2);
  });

  it("exports Excel-compatible UTF-8 CSV with semicolon delimiters", () => {
    const csv = toReportCsv(rows, columns);
    assert.ok(csv.startsWith("\uFEFF"));
    assert.equal(csv.split("\r\n")[0], '\uFEFF"Nombre";"Cantidad"');
    assert.match(csv, /"Cliente, Norte"/);
    assert.match(csv, /"Tienda ""Centro"""/);
  });

  it("sanitizes file names for Windows, Android and iOS downloads", () => {
    assert.equal(safeReportFileName("Despacho: 001/2026"), "Despacho-001-2026");
  });

  it("exports user supplied formulas as text", () => {
    const csv = toReportCsv([{ name: "=HYPERLINK(1)", quantity: 2 }, { name: "\t+CMD", quantity: 3 }], columns);
    assert.ok(csv.includes('"\'=HYPERLINK(1)"'));
    assert.ok(csv.includes('"\'\t+CMD"'));
  });
});
