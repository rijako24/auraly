const AUTHENTICATION_PATHS = [
  "/auth/login",
  "/auth/refresh",
  "/auth/register",
  "/auth/invitations/accept",
];

export const SESSION_EXPIRED_EVENT = "auraly:session-expired";

export function isAuthenticationRequest(url: string): boolean {
  return AUTHENTICATION_PATHS.some((path) => url.includes(path));
}

export function shouldRefreshSession(status: number, url: string): boolean {
  return status === 401 && !isAuthenticationRequest(url);
}

export async function retryAuthenticatedRequest<T extends { status: number }>(
  url: string,
  send: () => Promise<T>,
  refresh: () => Promise<boolean>,
  expire: () => Promise<void>,
): Promise<T> {
  const response = await send();
  if (!shouldRefreshSession(response.status, url)) return response;

  if (!await refresh()) {
    await expire();
    return response;
  }

  const retried = await send();
  if (retried.status === 401) await expire();
  return retried;
}

export function isInstalledApplicationDisplay(
  standaloneDisplayMode: boolean,
  iosStandalone: boolean,
): boolean {
  return standaloneDisplayMode || iosStandalone;
}
