/**
 * Auth cookie configuration for the BFF pattern.
 * Tokens and the durable browser identifier are HttpOnly.
 */

export const AUTH_COOKIE_NAMES = {
  accessToken: "auth_token",
  refreshToken: "auth_refresh",
  clientId: "auraly_auth_client",
} as const;

export function shouldUseSecureAuthCookies(
  nodeEnvironment = process.env.NODE_ENV,
  desktopLocal = process.env.AURALY_DESKTOP_LOCAL,
): boolean {
  return nodeEnvironment === "production" && desktopLocal !== "true";
}

export function getAuthCookieOptions(maxAgeSeconds: number) {
  return {
    httpOnly: true,
    secure: shouldUseSecureAuthCookies(),
    sameSite: "lax" as const,
    path: "/",
    maxAge: maxAgeSeconds,
  };
}

export function getAccessTokenCookieOptions() {
  return getAuthCookieOptions(24 * 60 * 60);
}

export function getRefreshTokenCookieOptions() {
  return getAuthCookieOptions(7 * 24 * 60 * 60);
}

export function getAuthenticationClientCookieOptions() {
  return getAuthCookieOptions(365 * 24 * 60 * 60);
}
