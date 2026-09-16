const endpointPattern = /(?:https?:\/\/|(?:[a-z0-9-]+\.)+(?:com|net|org|io|cloud|azurewebsites)(?::\d+)?|\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?)/i;
const cjkPattern = /[\u3400-\u9fff]/g;

function decodePackedUtf8(value: string): string {
  const characters = Array.from(value);
  const cjkCount = value.match(cjkPattern)?.length ?? 0;
  if (cjkCount < 2 || cjkCount * 2 < characters.length) return value;
  const bytes: number[] = [];
  for (const character of characters) {
    const code = character.charCodeAt(0);
    bytes.push(code & 0xff, code >> 8);
  }
  while (bytes.at(-1) === 0) bytes.pop();
  try {
    const decoded = new TextDecoder("utf-8", { fatal: true }).decode(new Uint8Array(bytes));
    return /[\p{L}\p{N}]/u.test(decoded) && !/[\u0000-\u0008\u000e-\u001f]/.test(decoded)
      ? decoded
      : value;
  } catch {
    return value;
  }
}

export function posPublicError(
  value: string | null | undefined,
  fallback = "No fue posible conectar con Auraly. Se volverá a intentar automáticamente.",
): string | null {
  if (!value?.trim()) return null;
  const normalized = decodePackedUtf8(value).replace(/[\r\n]+/g, " ").trim();
  if (endpointPattern.test(normalized)) return fallback;
  if (/Permission 'sales\.below-cost' is required\.?/i.test(normalized))
    return "Esta venta queda por debajo del costo y requiere autorización de un usuario con ese permiso.";
  if (/Permission '[^']+' is required\.?/i.test(normalized))
    return "Tu usuario no tiene permiso para completar esta acción.";
  return normalized.length <= 300 ? normalized : `${normalized.slice(0, 300)}…`;
}
