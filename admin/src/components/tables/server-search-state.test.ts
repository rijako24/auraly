import assert from "node:assert/strict";
import test from "node:test";

import { shouldShowServerSearchSpinner } from "./server-search-state";

test("no presenta el cargador de búsqueda durante la carga general de una grilla", () => {
  assert.equal(shouldShowServerSearchSpinner({
    draft: "",
    value: "",
    isSearching: true,
  }), false);
});

test("presenta el cargador mientras espera o ejecuta una búsqueda escrita", () => {
  assert.equal(shouldShowServerSearchSpinner({
    draft: "cliente",
    value: "",
    isSearching: false,
  }), true);
  assert.equal(shouldShowServerSearchSpinner({
    draft: "cliente",
    value: "cliente",
    isSearching: true,
  }), true);
});

test("retira el cargador cuando la búsqueda escrita terminó", () => {
  assert.equal(shouldShowServerSearchSpinner({
    draft: "cliente",
    value: "cliente",
    isSearching: false,
  }), false);
});
