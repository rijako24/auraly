const AUTHENTICATION_PATHS = [
  "/auth/login",
  "/auth/refresh",
  "/auth/register",
  "/auth/invitations/accept",
];

export const SESSION_EXPIRED_EVENT = "auraly:session-expired";
export type SessionExpiredEventDetail = {
  destination: string;
  message: string;
};
export type SessionRefreshResult = "refreshed" | "expired" | "unavailable";

export function announceSessionReplacement(destination: string): void {
  if (typeof window === "undefined") return;
  const event = new CustomEvent<SessionExpiredEventDetail>(SESSION_EXPIRED_EVENT, {
    cancelable: true,
    detail: {
      destination,
      message: "Tu usuario inició sesión en otro navegador o caja. Por seguridad, esta sesión se cerrará.",
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
  expire: () => Promise<void>,
): Promise<T> {
  const response = await send();
  if (!shouldRefreshSession(response.status, url)) return response;

  const refreshResult = await refresh();
  if (refreshResult === "expired") {
    await expire();
    return response;
  }
  if (refreshResult === "unavailable") return response;

  const retried = await send();
  if (retried.status === 401) await expire();
  return retried;
}
