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
  lastSynchronizationFailed?: boolean;
  lastSynchronizationError?: string | null;
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

  if (health.lastSynchronizationFailed) {
    return {
      title: "La preparación se detuvo",
      detail: health.lastSynchronizationError ?? "No fue posible terminar la preparación de esta caja.",
      currentResource: activeStages || "Preparación pendiente",
      resourceProgress: health.catalogProgressPercent ?? null,
      overallProgress,
      processedLabel: "Corrige la causa y reintenta cuando estés listo",
      connectionLabel: health.serverConnected ? "Conectada a Auraly" : "Sin conexión con Auraly",
      resumeLabel: "El reintento es manual",
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
      connectionLabel: health.serverConnected ? "Conectada a Auraly" : "Esperando conexión con Auraly",
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
    connectionLabel: health.serverConnected ? "Conectada a Auraly" : "Esperando conexión con Auraly",
    resumeLabel: "Auraly abrirá facturación apenas termine la validación",
  };
}
