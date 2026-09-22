import { posPublicError } from "./pos-public-error";

type EdgeFailure = {
  status: number;
  message: string;
};

export function isOnlinePosTransportFailure(
  mode: "online" | "edge" | null,
  caught: unknown,
) {
  return mode === "online" && (
    caught instanceof TypeError ||
    (caught instanceof DOMException &&
      (caught.name === "AbortError" || caught.name === "TimeoutError"))
  );
}

export function posOperationErrorMessage(
  mode: "online" | "edge" | null,
  caught: unknown,
  edgeFailure: EdgeFailure | null = null,
): string {
  const status = edgeFailure?.status ?? 0;
  const publicError = caught instanceof Error
    ? posPublicError(caught.message, "No fue posible completar la operación.")
    : null;
  if (isOnlinePosTransportFailure(mode, caught))
    return "No hay conexión con Auraly. La venta en línea requiere conexión con el servidor.";
  if (mode === "online")
    return publicError ?? "No fue posible completar la operación en Auraly.";
  if (status === 409 && edgeFailure)
    return publicError ?? "No fue posible completar la operación local.";
  if (status === 404) return "Producto no encontrado en el catálogo local";
  if (status === 503 && edgeFailure?.message.includes("tirilla"))
    return "La factura fue emitida, pero la tirilla no pudo imprimirse. Reintenta sin modificar la venta.";
  if (status === 503) return "La bodega exige validar inventario y no hay conexión";
  return "No fue posible acceder a los servicios locales del equipo";
}
