type HttpLikeError = Error & { statusCode?: number };

export const SELLER_ORDER_SYNC_REQUEST_EVENT = "auraly:seller-order-sync-request";
export const SELLER_ORDER_SYNC_COMPLETED_EVENT = "auraly:seller-order-sync-completed";

export function shouldQueueSellerOrder(error: unknown): boolean {
  if (!(error instanceof Error)) return false;
  const status = (error as HttpLikeError).statusCode;
  if (typeof status === "number")
    return status === 408 || status === 425 || status === 429 || status >= 500;
  return error.name === "TypeError" || error.name === "AbortError" ||
    error.name === "TimeoutError" || /fetch|network|conexi[oó]n|servidor/i.test(error.message);
}

export function sellerOrderErrorMessage(
  error: unknown,
  fallback = "No fue posible guardar el pedido.",
): string {
  if (!(error instanceof Error)) return fallback;
  const status = (error as HttpLikeError).statusCode;
  if (status === 401) return "Tu sesión venció. Conéctate e inicia sesión nuevamente.";
  if (status === 403) return "No tienes permiso para crear pedidos.";
  if (shouldQueueSellerOrder(error))
    return "El servidor no está disponible. Revisa la conexión e intenta nuevamente.";
  return error.message.trim() || fallback;
}
