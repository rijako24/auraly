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

export function isInstalledApplicationDisplay(
  standaloneDisplayMode: boolean,
  iosStandalone: boolean,
): boolean {
  return standaloneDisplayMode || iosStandalone;
}
