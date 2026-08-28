export const AUTH_SERVICE_UNAVAILABLE_MESSAGE =
  "No hay conexión con Auraly en este momento. Revisa tu conexión e intenta nuevamente.";

const INVALID_CREDENTIALS_MESSAGE =
  "Usuario, empresa o contraseña incorrectos.";

type UpstreamProblem = {
  detail?: unknown;
  error?: unknown;
  message?: unknown;
  title?: unknown;
};

export type LoginFailure = {
  message: string;
  status: number;
};

const PROVIDER_OUTAGE_MARKERS = [
  "admindisabled",
  "readonlydisabledsubscription",
  "subscriptiondisabled",
  "web app - unavailable",
];

function isProviderOutage(problem: unknown): boolean {
  if (problem === null || problem === undefined) {
    return false;
  }

  const serialized = JSON.stringify(problem).toLowerCase();
  return PROVIDER_OUTAGE_MARKERS.some((marker) => serialized.includes(marker));
}

export async function translateLoginFailure(
  response: Response,
): Promise<LoginFailure> {
  if (response.status === 401) {
    return { message: INVALID_CREDENTIALS_MESSAGE, status: 401 };
  }

  const contentType = response.headers.get("content-type")?.toLowerCase() ?? "";
  const isJson = contentType.includes("application/json") ||
    contentType.includes("application/problem+json");
  const problem = isJson
    ? await response.json().catch(() => null) as UpstreamProblem | null
    : null;
  const isUnavailable = response.status === 408 ||
    response.status === 429 ||
    response.status >= 500 ||
    !isJson ||
    isProviderOutage(problem);

  if (isUnavailable) {
    return { message: AUTH_SERVICE_UNAVAILABLE_MESSAGE, status: 503 };
  }

  const message = [problem?.detail, problem?.message, problem?.title]
    .find((value): value is string => typeof value === "string" && value.trim().length > 0);

  return {
    message: message?.trim() || "No fue posible iniciar sesión.",
    status: response.status,
  };
}
