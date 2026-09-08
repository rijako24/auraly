import assert from "node:assert/strict";
import test from "node:test";

import { posPreparationView } from "./pos-preparation-progress";

test("shows real catalog counts and percentage", () => {
  const view = posPreparationView({
    serverConnected: true,
    identityReady: true,
    catalogStatus: "Bootstrapping",
    catalogProcessedProducts: 500,
    catalogTotalProducts: 2_000,
    catalogProgressPercent: 25,
    preparationStage: "Catalog",
    preparationCompletedSteps: 1,
    preparationTotalSteps: 2,
    preparationCanResume: true,
  });

  assert.equal(view.resourceProgress, 25);
  assert.equal(view.overallProgress, 50);
  assert.equal(view.processedLabel, "500 de 2.000 productos");
  assert.match(view.resumeLabel, /Reintentar/);
});

test("shows the reason and requires a manual retry after preparation fails", () => {
  const view = posPreparationView({
    serverConnected: false,
    identityReady: true,
    catalogStatus: "Bootstrapping",
    lastSynchronizationFailed: true,
    lastSynchronizationError: "No hay conexión válida con Auraly Server.",
    preparationCompletedSteps: 1,
    preparationTotalSteps: 2,
  });

  assert.equal(view.title, "La preparación se detuvo");
  assert.match(view.detail, /No hay conexión válida/);
  assert.equal(view.resumeLabel, "El reintento es manual");
});

test("does not invent a percentage while identity totals are unknown", () => {
  const view = posPreparationView({
    serverConnected: false,
    identityReady: false,
    catalogStatus: "Empty",
    preparationStage: "Identity",
    preparationCompletedSteps: 0,
    preparationTotalSteps: 2,
    synchronizationStages: ["usuarios y permisos"],
  });

  assert.equal(view.resourceProgress, null);
  assert.equal(view.currentResource, "usuarios y permisos");
  assert.equal(view.connectionLabel, "Esperando conexión con Auraly");
});

test("explains an empty catalog instead of looking stuck", () => {
  const view = posPreparationView({
    serverConnected: true,
    identityReady: true,
    catalogStatus: "Bootstrapping",
    catalogProcessedProducts: 0,
    catalogTotalProducts: 0,
    catalogProgressPercent: null,
    preparationStage: "Catalog",
    preparationCompletedSteps: 1,
    preparationTotalSteps: 2,
    preparationCanResume: true,
  });

  assert.equal(view.title, "No hay productos por descargar");
  assert.equal(view.processedLabel, "0 productos en este negocio");
});
