import test from "node:test";
import assert from "node:assert/strict";
import { cashMovementKeyboardAction } from "./pos-cash-movement-keyboard";

test("the reason select keeps native enter and arrow behavior", () => {
  assert.equal(cashMovementKeyboardAction({ key: "Enter", control: "select" }), "native");
  assert.equal(cashMovementKeyboardAction({ key: "ArrowDown", control: "select" }), "native");
});

test("ordinary fields navigate with arrows and submit with enter", () => {
  assert.equal(cashMovementKeyboardAction({ key: "ArrowDown", control: "field" }), "next");
  assert.equal(cashMovementKeyboardAction({ key: "ArrowUp", control: "field" }), "previous");
  assert.equal(cashMovementKeyboardAction({ key: "Enter", control: "field" }), "submit");
});

test("textarea and buttons retain native enter behavior", () => {
  assert.equal(cashMovementKeyboardAction({ key: "Enter", control: "textarea" }), "native");
  assert.equal(cashMovementKeyboardAction({ key: "Enter", control: "button" }), "native");
});
