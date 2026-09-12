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

test("shows the reason and offers a manual retry after automatic retries fail", () => {
  const view = posPreparationView({
    serverConnected: false,
    identityReady: true,
    catalogStatus: "Bootstrapping",
    preparationStage: "Catalog",
    lastSynchronizationFailed: true,
    lastSynchronizationError: "No hay conexión válida con Auraly Server.",
    preparationCompletedSteps: 1,
    preparationTotalSteps: 2,
  });

  assert.equal(view.title, "La preparación se detuvo");
  assert.match(view.detail, /No hay conexión válida/);
  assert.equal(view.currentResource, "Productos y precios");
  assert.equal(view.resumeLabel, "Los 3 reintentos automáticos terminaron");
});

test("shows which automatic recovery attempt is pending", () => {
  const view = posPreparationView({
    serverConnected: false,
    identityReady: false,
    catalogStatus: "Empty",
    automaticRetryScheduled: true,
    automaticRetryAttempt: 2,
    preparationCompletedSteps: 0,
    preparationTotalSteps: 2,
  });

  assert.equal(view.title, "Recuperando la conexión");
  assert.match(view.resumeLabel, /2 de 3/);
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
  assert.equal(view.connectionLabel, "Verificando conexión con Auraly");
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
