import assert from "node:assert/strict";
import test from "node:test";

import { capturedLineAfterAddition } from "./pos-capture-presentation";
import { acceptsPosQuantityDraft, blocksPosQuantityKey, validatePosQuantity } from "./pos-quantity-validation";
import {
  capturePosFunctionShortcut,
  resolvePosFunctionShortcut,
} from "./pos-function-shortcut";

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

test("la pantalla y el modal comparten reglas de cantidad, fracción e inventario", () => {
  assert.deepEqual(validatePosQuantity("1.5", {
    allowsFractionalSale: false, managesInventory: false,
  }), { valid: false, reason: "whole-units" });
  assert.deepEqual(validatePosQuantity("1.5", {
    allowsFractionalSale: true, managesInventory: false,
  }), { valid: true, quantity: 1.5 });
  assert.deepEqual(validatePosQuantity(8, {
    allowsFractionalSale: false, managesInventory: true, maximumQuantity: 7,
  }), { valid: false, reason: "inventory-limit" });
  assert.equal(acceptsPosQuantityDraft("1.2", {
    allowsFractionalSale: false, managesInventory: false,
  }), false);
  assert.equal(blocksPosQuantityKey("e", {
    allowsFractionalSale: true, managesInventory: false,
  }), true);
});

test("reconoce las teclas F por su código físico aunque el sistema cambie event.key", () => {
  assert.equal(resolvePosFunctionShortcut("AudioVolumeDown", "F2"), "F2");
  assert.equal(resolvePosFunctionShortcut("Unidentified", "F10"), "F10");
  assert.equal(resolvePosFunctionShortcut("F3", ""), "F3");
  assert.equal(resolvePosFunctionShortcut("LaunchApplication1", "LaunchApp1"), "F2");
  assert.equal(resolvePosFunctionShortcut("Unidentified", "", 113), "F2");
  assert.equal(resolvePosFunctionShortcut("a", "KeyA", 65), "");
});

test("cancela la acción de Windows antes de ejecutar el atajo POS", () => {
  const calls: string[] = [];
  const event = {
    key: "LaunchApplication1",
    code: "LaunchApp1",
    keyCode: 0,
    preventDefault: () => calls.push("preventDefault"),
    stopImmediatePropagation: () => calls.push("stopImmediatePropagation"),
  };

  assert.equal(capturePosFunctionShortcut(event, shortcut => calls.push(shortcut)), true);
  assert.deepEqual(calls, ["preventDefault", "stopImmediatePropagation", "F2"]);
});

test("no interfiere con teclas que no pertenecen al POS", () => {
  let cancelled = false;
  const captured = capturePosFunctionShortcut({
    key: "a",
    code: "KeyA",
    keyCode: 65,
    preventDefault: () => { cancelled = true; },
    stopImmediatePropagation: () => { cancelled = true; },
  }, () => { cancelled = true; });

  assert.equal(captured, false);
  assert.equal(cancelled, false);
});
