import { posPublicError } from "./pos-public-error";

export type PosPreparationHealth = {
  serverConnected: boolean;
  identityReady: boolean;
  catalogStatus: string;
  catalogProcessedProducts?: number;
  catalogTotalProducts?: number;
  catalogProgressPercent?: number | null;
  preparationStage?: "Identity" | "CatalogStarting" | "Catalog" | "Finalizing";
  preparationCompletedSteps?: number;
  preparationTotalSteps?: number;
  preparationCanResume?: boolean;
  synchronizationStages?: string[];
  failedSynchronizationStage?: string | null;
  lastSynchronizationFailed?: boolean;
  lastSynchronizationError?: string | null;
  automaticRetryScheduled?: boolean;
  automaticRetryAttempt?: number;
};

export type PosPreparationView = {
  title: string;
  detail: string;
  currentResource: string;
  resourceProgress: number | null;
  overallProgress: number | null;
  processedLabel: string | null;
  connectionLabel: string;
  resumeLabel: string;
};

export function posPreparationView(
  health: PosPreparationHealth | null,
): PosPreparationView {
  if (!health) {
    return {
      title: "Conectando con esta caja",
      detail: "Estamos leyendo el checkpoint local para continuar justo donde quedó.",
      currentResource: "Estado del equipo",
      resourceProgress: null,
      overallProgress: null,
      processedLabel: null,
      connectionLabel: "Conectando…",
      resumeLabel: "Buscando un punto de reanudación seguro",
    };
  }

  const completed = Math.max(0, health.preparationCompletedSteps ?? 0);
  const totalSteps = Math.max(0, health.preparationTotalSteps ?? 0);
  const overallProgress = totalSteps > 0
    ? Math.min(100, Math.floor(completed * 100 / totalSteps))
    : null;
  const activeStages = (health.synchronizationStages ?? []).join(", ");

  if (health.automaticRetryScheduled) {
    const attempt = Math.min(3, Math.max(1, health.automaticRetryAttempt ?? 1));
    return {
      title: "Recuperando la conexión",
      detail: `La conexión se interrumpió. Auraly ejecutará el reintento automático ${attempt} de 3.`,
      currentResource: activeStages || "Preparación pendiente",
      resourceProgress: health.catalogProgressPercent ?? null,
      overallProgress,
      processedLabel: "No necesitas intervenir; el avance local está guardado",
      connectionLabel: health.serverConnected ? "Conexión recuperada" : "Esperando conexión con Auraly",
      resumeLabel: `Reintento automático ${attempt} de 3`,
    };
  }

  if (health.lastSynchronizationFailed) {
    const failedResource = health.preparationStage === "Catalog"
      ? "Productos y precios"
      : activeStages || health.failedSynchronizationStage || "Preparación pendiente";
    return {
      title: "La preparación se detuvo",
      detail: posPublicError(
        health.lastSynchronizationError,
        "No fue posible terminar la preparación de esta caja.",
      ) ?? "No fue posible terminar la preparación de esta caja.",
      currentResource: failedResource,
      resourceProgress: health.catalogProgressPercent ?? null,
      overallProgress,
      processedLabel: "Corrige la causa y reintenta cuando estés listo",
      connectionLabel: health.serverConnected ? "Conectada a Auraly" : "Verificando conexión con Auraly",
      resumeLabel: "Los 3 reintentos automáticos terminaron",
    };
  }

  if (health.preparationStage === "Catalog") {
    const processed = Math.max(0, health.catalogProcessedProducts ?? 0);
    const total = Math.max(0, health.catalogTotalProducts ?? 0);
    return {
      title: total === 0 ? "No hay productos por descargar" : "Descargando el catálogo",
      detail: total === 0
        ? "La caja continuará con precios y configuración operativa."
        : "Productos, códigos de barras, impuestos y precios públicos.",
      currentResource: "Productos y precios",
      resourceProgress: health.catalogProgressPercent ?? null,
      overallProgress,
      processedLabel: total > 0
        ? `${processed.toLocaleString("es-CO")} de ${total.toLocaleString("es-CO")} productos`
        : "0 productos en este negocio",
      connectionLabel: health.serverConnected ? "Conectada a Auraly" : "Sin conexión con Auraly",
      resumeLabel: health.preparationCanResume
        ? "Progreso guardado · usa Reintentar si se detiene"
        : "Validando el catálogo descargado",
    };
  }

  if (health.preparationStage === "Identity" || !health.identityReady) {
    return {
      title: "Preparando el acceso local",
      detail: "Descargando únicamente los usuarios y permisos autorizados para esta caja.",
      currentResource: activeStages || "Usuarios y permisos",
      resourceProgress: null,
      overallProgress,
      processedLabel: null,
      connectionLabel: health.serverConnected ? "Conectada a Auraly" : "Verificando conexión con Auraly",
      resumeLabel: "La información se guarda de forma segura en este equipo",
    };
  }

  return {
    title: "Terminando la preparación",
    detail: "Validando precios, medios de pago y configuración de la caja.",
    currentResource: activeStages || "Configuración operativa",
    resourceProgress: null,
    overallProgress,
    processedLabel: null,
    connectionLabel: health.serverConnected ? "Conectada a Auraly" : "Verificando conexión con Auraly",
    resumeLabel: "Auraly abrirá facturación apenas termine la validación",
  };
}
