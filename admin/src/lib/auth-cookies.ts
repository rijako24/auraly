/**
 * Auth cookie configuration for BFF pattern.
 * Tokens are stored in HttpOnly cookies - not accessible to JavaScript.
 */

export const AUTH_COOKIE_NAMES = {
  accessToken: "auth_token",
  refreshToken: "auth_refresh",
} as const;

const isProduction = process.env.NODE_ENV === "production";

export function getAuthCookieOptions(maxAgeSeconds: number) {
  return {
    httpOnly: true,
    secure: isProduction,
    sameSite: "lax" as const,
    path: "/",
    maxAge: maxAgeSeconds,
  };
}

export function getAccessTokenCookieOptions() {
  return getAuthCookieOptions(24 * 60 * 60); // 24 hours
}

export function getRefreshTokenCookieOptions() {
  return getAuthCookieOptions(7 * 24 * 60 * 60); // 7 days
}
