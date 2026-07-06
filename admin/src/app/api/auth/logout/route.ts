import { NextRequest, NextResponse } from "next/server";
import { AUTH_COOKIE_NAMES } from "@/lib/auth-cookies";
import { getBackendUrl } from "@/lib/backend-url";

export async function POST(request: NextRequest) {
  const accessToken = request.cookies.get(AUTH_COOKIE_NAMES.accessToken)?.value;
  const refreshToken =
    request.cookies.get(AUTH_COOKIE_NAMES.refreshToken)?.value;

  if (refreshToken) {
    try {
      await fetch(`${getBackendUrl()}/auth/revoke`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          ...(accessToken && { Authorization: `Bearer ${accessToken}` }),
        },
        body: JSON.stringify({ refreshToken }),
      });
    } catch {
      // Best effort - clear cookies even if revoke fails
    }
  }

  const response = NextResponse.json({}, { status: 200 });
  response.cookies.delete(AUTH_COOKIE_NAMES.accessToken);
  response.cookies.delete(AUTH_COOKIE_NAMES.refreshToken);

  return response;
}
