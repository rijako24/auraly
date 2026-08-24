import { NextRequest, NextResponse } from "next/server";
import {
  AUTH_COOKIE_NAMES,
  getAccessTokenCookieOptions,
  getAuthenticationClientCookieOptions,
  getRefreshTokenCookieOptions,
} from "@/lib/auth-cookies";
import {
  AUTHENTICATION_CLIENT_ID_HEADER,
  resolveAuthenticationClientId,
} from "@/lib/auth-client";
import { getBackendRequestUrl } from "@/lib/backend-request-url";
import type { AuthResponse, LoginRequest } from "@/types/api";

export async function POST(request: NextRequest) {
  try {
    const body = (await request.json()) as LoginRequest;
    const clientId = resolveAuthenticationClientId(
      request.cookies.get(AUTH_COOKIE_NAMES.clientId)?.value,
    );
    const res = await fetch(getBackendRequestUrl("auth/login"), {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        [AUTHENTICATION_CLIENT_ID_HEADER]: clientId,
      },
      body: JSON.stringify(body),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      return NextResponse.json(
        {
          message: res.status === 401
            ? "Usuario, empresa o contraseña incorrectos."
            : err.detail || err.message || err.title || "Error al iniciar sesión",
        },
        { status: res.status },
      );
    }

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
    response.cookies.set(
      AUTH_COOKIE_NAMES.clientId,
      clientId,
      getAuthenticationClientCookieOptions(),
    );
    return response;
  } catch (error) {
    console.error("[auth/login]", error);
    return NextResponse.json(
      { message: "Error de conexión con el servidor" },
      { status: 500 },
    );
  }
}
