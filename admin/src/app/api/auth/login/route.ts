import { NextRequest, NextResponse } from "next/server";
import {
  AUTH_COOKIE_NAMES,
  getAccessTokenCookieOptions,
  getRefreshTokenCookieOptions,
} from "@/lib/auth-cookies";
import { getBackendUrl } from "@/lib/backend-url";
import type { AuthResponse, LoginRequest } from "@/types/api";


export async function POST(request: NextRequest) {
  try {
    const body = (await request.json()) as LoginRequest;
    const res = await fetch(`${getBackendUrl()}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });

    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      return NextResponse.json(
        { message: err.message || err.title || "Error al iniciar sesión" },
        { status: res.status }
      );
    }

    const data = (await res.json()) as AuthResponse;
    const response = NextResponse.json(
      { user: data.user, correlationId: data.correlationId },
      { status: 200 }
    );

    response.cookies.set(
      AUTH_COOKIE_NAMES.accessToken,
      data.accessToken,
      getAccessTokenCookieOptions()
    );
    response.cookies.set(
      AUTH_COOKIE_NAMES.refreshToken,
      data.refreshToken,
      getRefreshTokenCookieOptions()
    );

    return response;
  } catch (error) {
    console.error("[auth/login]", error);
    return NextResponse.json(
      { message: "Error de conexión con el servidor" },
      { status: 500 }
    );
  }
}
