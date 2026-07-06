import { NextRequest, NextResponse } from "next/server";
import {
  AUTH_COOKIE_NAMES,
  getAccessTokenCookieOptions,
  getRefreshTokenCookieOptions,
} from "@/lib/auth-cookies";
import { getBackendUrl } from "@/lib/backend-url";
import type { AuthResponse } from "@/types/api";


export async function POST(request: NextRequest) {
  try {
    const accessToken = request.cookies.get(AUTH_COOKIE_NAMES.accessToken)?.value;
    const refreshToken =
      request.cookies.get(AUTH_COOKIE_NAMES.refreshToken)?.value;

    if (!accessToken || !refreshToken) {
      return NextResponse.json(
        { message: "Sesión expirada. Inicie sesión de nuevo." },
        { status: 401 }
      );
    }

    const res = await fetch(`${getBackendUrl()}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        accessToken,
        refreshToken,
      }),
    });

    if (!res.ok) {
      const response = NextResponse.json(
        { message: "Sesión expirada. Inicie sesión de nuevo." },
        { status: 401 }
      );
      response.cookies.delete(AUTH_COOKIE_NAMES.accessToken);
      response.cookies.delete(AUTH_COOKIE_NAMES.refreshToken);
      return response;
    }

    const data = (await res.json()) as AuthResponse;
    const nextResponse = NextResponse.json(
      { user: data.user, correlationId: data.correlationId },
      { status: 200 }
    );

    nextResponse.cookies.set(
      AUTH_COOKIE_NAMES.accessToken,
      data.accessToken,
      getAccessTokenCookieOptions()
    );
    nextResponse.cookies.set(
      AUTH_COOKIE_NAMES.refreshToken,
      data.refreshToken,
      getRefreshTokenCookieOptions()
    );

    return nextResponse;
  } catch (error) {
    console.error("[auth/refresh]", error);
    return NextResponse.json(
      { message: "Error al renovar la sesión" },
      { status: 500 }
    );
  }
}
