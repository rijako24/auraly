import { NextRequest, NextResponse } from "next/server";
import {
  AUTH_COOKIE_NAMES,
  getAccessTokenCookieOptions,
  getRefreshTokenCookieOptions,
} from "@/lib/auth-cookies";
import { AUTHENTICATION_CLIENT_ID_HEADER } from "@/lib/auth-client";
import { getBackendRequestUrl } from "@/lib/backend-request-url";
import type { AuthResponse } from "@/types/api";

export async function POST(request: NextRequest) {
  try {
    const accessToken = request.cookies.get(AUTH_COOKIE_NAMES.accessToken)?.value;
    const refreshToken = request.cookies.get(AUTH_COOKIE_NAMES.refreshToken)?.value;
    const clientId = request.cookies.get(AUTH_COOKIE_NAMES.clientId)?.value;

    if (!accessToken || !refreshToken || !clientId) {
      return expiredResponse();
    }

    const res = await fetch(getBackendRequestUrl("auth/refresh"), {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        [AUTHENTICATION_CLIENT_ID_HEADER]: clientId,
      },
      body: JSON.stringify({ accessToken, refreshToken }),
    });

    if (!res.ok) return expiredResponse();

    const data = (await res.json()) as AuthResponse;
    const response = NextResponse.json(
      { user: data.user, correlationId: data.correlationId },
      { status: 200 },
    );
    response.cookies.set(
      AUTH_COOKIE_NAMES.accessToken,
      data.accessToken,
      getAccessTokenCookieOptions(),
    );
    response.cookies.set(
      AUTH_COOKIE_NAMES.refreshToken,
      data.refreshToken,
      getRefreshTokenCookieOptions(),
    );
    return response;
  } catch (error) {
    console.error("[auth/refresh]", error);
    return NextResponse.json(
      { message: "Error al renovar la sesión" },
      { status: 500 },
    );
  }
}

function expiredResponse() {
  const response = NextResponse.json(
    { message: "Sesión expirada. Inicie sesión de nuevo." },
    { status: 401 },
  );
  response.cookies.delete(AUTH_COOKIE_NAMES.accessToken);
  response.cookies.delete(AUTH_COOKIE_NAMES.refreshToken);
  return response;
}
