export async function orderHttpError(response: Response): Promise<Error> {
  const raw = await response.text();
  const contentType = response.headers.get("content-type")?.toLowerCase() ?? "";
  if (contentType.includes("json")) {
    try {
      const problem = JSON.parse(raw) as {
        detail?: string;
        message?: string;
        title?: string;
      };
      const detail = problem.detail || problem.message || problem.title;
      if (detail) return new Error(detail);
    } catch {
      // The fallback below keeps malformed upstream responses out of the UI.
    }
  }

  const status = response.status >= 500
    ? "El servidor no pudo consultar los pedidos. Intenta nuevamente."
    : response.status === 404
      ? "El pedido ya no está disponible. Actualiza la lista e intenta nuevamente."
      : response.status === 403
        ? "No tienes permiso para consultar este pedido."
        : "No fue posible consultar los pedidos.";
  return new Error(status);
}
