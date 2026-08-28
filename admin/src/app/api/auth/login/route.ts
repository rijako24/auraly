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
import {
  AUTH_SERVICE_UNAVAILABLE_MESSAGE,
  translateLoginFailure,
} from "@/lib/auth-login-error";
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
      const failure = await translateLoginFailure(res);
      console.warn("[auth/login] upstream request failed", {
        upstreamStatus: res.status,
        returnedStatus: failure.status,
      });
      return NextResponse.json(
        { message: failure.message },
        { status: failure.status },
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
      { message: AUTH_SERVICE_UNAVAILABLE_MESSAGE },
      { status: 503 },
    );
  }
}
