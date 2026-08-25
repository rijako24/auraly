import assert from "node:assert/strict";
import test from "node:test";

import { capturedLineAfterAddition } from "./pos-capture-presentation";
import { resolvePosFunctionShortcut } from "./pos-function-shortcut";

test("selecciona la nueva línea aunque el mismo producto ya exista", () => {
  const previous = [
    { lineId: "linea-anterior", productId: "producto-1" },
    { lineId: "otra-linea", productId: "producto-2" },
  ];
  const current = [
    ...previous,
    { lineId: "linea-capturada", productId: "producto-1" },
  ];

  assert.equal(
    capturedLineAfterAddition(previous, current)?.lineId,
    "linea-capturada",
  );
});

test("usa la última línea como respaldo del contrato de captura", () => {
  const lines = [
    { lineId: "primera" },
    { lineId: "ultima" },
  ];

  assert.equal(capturedLineAfterAddition(lines, lines)?.lineId, "ultima");
});

test("no inventa una línea cuando el borrador está vacío", () => {
  assert.equal(capturedLineAfterAddition([], []), undefined);
});

test("reconoce las teclas F por su código físico aunque el sistema cambie event.key", () => {
  assert.equal(resolvePosFunctionShortcut("AudioVolumeDown", "F2"), "F2");
  assert.equal(resolvePosFunctionShortcut("Unidentified", "F10"), "F10");
  assert.equal(resolvePosFunctionShortcut("F3", ""), "F3");
});
