import assert from "node:assert/strict";
import test from "node:test";
import { printPosHtmlDocument } from "./pos-browser-print";

test("browser printing completes when the dialog is dispatched, before it closes", async () => {
  const originalDocument = globalThis.document;
  const originalWindow = globalThis.window;
  const timers: Array<() => void> = [];
  let printCalls = 0;

  const printWindow = {
    addEventListener: () => undefined,
    focus: () => undefined,
    print: () => { printCalls += 1; },
  };
  const frame = {
    contentWindow: printWindow,
    onload: null as null | (() => void),
    remove: () => undefined,
    setAttribute: () => undefined,
    style: {},
    srcdoc: "",
  };

  Object.defineProperty(globalThis, "window", {
    configurable: true,
    value: {
      setTimeout: (callback: () => void) => {
        timers.push(callback);
        return timers.length;
      },
    },
  });
  Object.defineProperty(globalThis, "document", {
    configurable: true,
    value: {
      createElement: () => frame,
      body: { appendChild: () => frame.onload?.() },
    },
  });

  try {
    const dispatched = printPosHtmlDocument("<p>Factura</p>", "No se pudo imprimir");
    assert.equal(timers.length, 1);
    timers.shift()?.();
    await dispatched;
    assert.equal(printCalls, 0);

    timers.shift()?.();
    assert.equal(printCalls, 1);
  } finally {
    Object.defineProperty(globalThis, "document", { configurable: true, value: originalDocument });
    Object.defineProperty(globalThis, "window", { configurable: true, value: originalWindow });
  }
});
