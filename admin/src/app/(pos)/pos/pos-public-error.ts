const endpointPattern = /(?:https?:\/\/|(?:[a-z0-9-]+\.)+(?:com|net|org|io|cloud|azurewebsites)(?::\d+)?|\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?)/i;

export function posPublicError(
  value: string | null | undefined,
  fallback = "No fue posible conectar con Auraly. Se volverá a intentar automáticamente.",
): string | null {
  if (!value?.trim()) return null;
  const normalized = value.replace(/[\r\n]+/g, " ").trim();
  if (endpointPattern.test(normalized)) return fallback;
  return normalized.length <= 300 ? normalized : `${normalized.slice(0, 300)}…`;
}
