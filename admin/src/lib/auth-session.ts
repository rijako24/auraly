const AUTHENTICATION_PATHS = [
  "/auth/login",
  "/auth/refresh",
  "/auth/register",
  "/auth/invitations/accept",
];

export const SESSION_EXPIRED_EVENT = "auraly:session-expired";
export type SessionExpiredEventDetail = {
  destination: string;
  title: string;
  message: string;
};
export type SessionRefreshResult = "refreshed" | "replaced" | "expired" | "unavailable";

let activeLogin: Promise<unknown> | null = null;

const PREVIOUS_IDENTITY_STORAGE_KEYS = [
  "auth-state",
  "selected_tenant_id",
  "selected_business_id",
] as const;

export function clearPreviousWebIdentityContext(
  storage: Pick<Storage, "removeItem"> | null =
    typeof window === "undefined" ? null : window.localStorage,
): void {
  if (!storage) return;
  try {
    for (const key of PREVIOUS_IDENTITY_STORAGE_KEYS) storage.removeItem(key);
  } catch {
    // Browsers may disable storage; server-side session replacement remains authoritative.
  }
}

export function isActiveLocalPosSession(
  pathname: string,
  edgeToken: string | null,
  userSession: string | null,
): boolean {
  return pathname.startsWith("/pos") && Boolean(edgeToken) && Boolean(userSession);
}

export function shouldRunCloudBackgroundSynchronization(pathname: string): boolean {
  return pathname.startsWith("/dashboard");
}

export function isCurrentWebSessionVersion(
  requestVersion: string,
  currentVersion: string,
): boolean {
  return requestVersion === currentVersion;
}

export async function runAuthenticationSessionReplacement<T>(
  advanceBoundary: () => void,
  login: () => Promise<T>,
): Promise<T> {
  if (activeLogin) return activeLogin as Promise<T>;
  const operation = (async () => {
    advanceBoundary();
    const result = await login();
    advanceBoundary();
    return result;
  })();
  activeLogin = operation;
  try {
    return await operation;
  } finally {
    if (activeLogin === operation) activeLogin = null;
  }
}

export function announceSessionReplacement(destination: string): void {
  announceSessionClosure(
    destination,
    "Sesión iniciada en otro lugar",
    "Tu usuario inició sesión en otro navegador o caja. Por seguridad, esta sesión se cerrará.",
  );
}

export function announceSessionClosure(
  destination: string,
  title: string,
  message: string,
): void {
  if (typeof window === "undefined") return;
  const event = new CustomEvent<SessionExpiredEventDetail>(SESSION_EXPIRED_EVENT, {
    cancelable: true,
    detail: {
      destination,
      title,
      message,
    },
  });
  if (window.dispatchEvent(event)) window.location.replace(destination);
}

export function isAuthenticationRequest(url: string): boolean {
  return AUTHENTICATION_PATHS.some((path) => url.includes(path));
}
export function shouldRefreshSession(status: number, url: string): boolean {
  return status === 401 && !isAuthenticationRequest(url);
}

export async function retryAuthenticatedRequest<T extends { status: number }>(
  url: string,
  send: () => Promise<T>,
  refresh: () => Promise<SessionRefreshResult>,
  expire: (reason: "replaced" | "expired") => Promise<void>,
): Promise<T> {
  const response = await send();
  if (!shouldRefreshSession(response.status, url)) return response;

  const refreshResult = await refresh();
  if (refreshResult === "replaced" || refreshResult === "expired") {
    await expire(refreshResult);
    return response;
  }
  if (refreshResult === "unavailable") return response;

  // A successful refresh proves the authentication session is active. If the
  // resource still returns 401, preserve that resource error; it is not proof
  // that another browser replaced the login.
  return send();
}
