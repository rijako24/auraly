/**
 * Comprueba si faltan propiedades marcadas como `required` en un JSON Schema de catálogo.
 * Ignora claves internas del editor/orquestador.
 */
const IGNORED_CONFIG_KEYS = new Set(["_ui", "executeWhen", "setFlags", "setVariables"]);

export function isFlowNodeConfigMissingRequired(
  schemaJson: string | undefined,
  config: Record<string, unknown> | undefined
): boolean {
  if (!schemaJson?.trim() || !config) return false;
  try {
    const schema = JSON.parse(schemaJson) as {
      required?: string[];
    };
    const required = schema.required ?? [];
    for (const key of required) {
      if (IGNORED_CONFIG_KEYS.has(key)) continue;
      const v = config[key];
      if (v === undefined || v === null) return true;
      if (typeof v === "string" && v.trim() === "") return true;
      if (Array.isArray(v) && v.length === 0) return true;
    }
  } catch {
    return false;
  }
  return false;
}
