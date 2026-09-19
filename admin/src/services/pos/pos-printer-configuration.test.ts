import assert from "node:assert/strict";
import test from "node:test";

import {
  completeInstalledPrinterConfiguration,
  readPosEdgeProblem,
} from "./pos-printer-configuration";
import type { PosPrinterConfiguration } from "./pos-edge-client";

const configuration: PosPrinterConfiguration = {
  receiptMode: "WindowsRaw",
  receiptPrinterName: null,
  receiptPaperWidthMillimeters: 80,
  letterPrinterName: null,
  orderMode: "WindowsPrint",
  posOutputFormat: "Receipt",
  orderOutputFormat: "HalfLetter",
  templateRoutes: null,
  scale: null,
};

test("the only Windows printer is selected for invoices and orders", () => {
  const completed = completeInstalledPrinterConfiguration(
    configuration,
    ["EPSON TM-T20III"],
  );

  assert.equal(completed.posPrinterName, "EPSON TM-T20III");
  assert.equal(completed.orderPrinterName, "EPSON TM-T20III");
});

test("printer validation errors show the actionable message", () => {
  const problem = readPosEdgeProblem(JSON.stringify({
    title: "One or more validation errors occurred.",
    errors: {
      PosPrinterConfiguration: ["Selecciona la impresora de pedidos."],
    },
  }), "Bad Request");

  assert.equal(problem.detail, "Selecciona la impresora de pedidos.");
});

test("legacy printer fields populate the independent workflows", () => {
  const completed = completeInstalledPrinterConfiguration({
    ...configuration,
    receiptPrinterName: "Tirilla anterior",
    letterPrinterName: "Documentos anteriores",
  }, ["Otra impresora"]);

  assert.equal(completed.posPrinterName, "Tirilla anterior");
  assert.equal(completed.orderPrinterName, "Documentos anteriores");
});
