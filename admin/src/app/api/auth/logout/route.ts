import { NextRequest, NextResponse } from "next/server";
import { AUTH_COOKIE_NAMES } from "@/lib/auth-cookies";
import { AUTHENTICATION_CLIENT_ID_HEADER } from "@/lib/auth-client";
import { getBackendRequestUrl } from "@/lib/backend-request-url";

export async function POST(request: NextRequest) {
  const accessToken = request.cookies.get(AUTH_COOKIE_NAMES.accessToken)?.value;
  const refreshToken = request.cookies.get(AUTH_COOKIE_NAMES.refreshToken)?.value;
  const clientId = request.cookies.get(AUTH_COOKIE_NAMES.clientId)?.value;

  if (accessToken && refreshToken && clientId) {
    try {
      await fetch(getBackendRequestUrl("auth/revoke"), {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${accessToken}`,
          [AUTHENTICATION_CLIENT_ID_HEADER]: clientId,
        },
        body: JSON.stringify({ refreshToken }),
      });
    } catch {
      // The local web session still ends. Expiry protects an unreachable server session.
    }
  }

  const response = NextResponse.json({}, { status: 200 });
  response.cookies.delete(AUTH_COOKIE_NAMES.accessToken);
  response.cookies.delete(AUTH_COOKIE_NAMES.refreshToken);
  return response;
}
