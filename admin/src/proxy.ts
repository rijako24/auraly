import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";
import { AUTH_COOKIE_NAMES } from "@/lib/auth-cookies";

const PUBLIC_PATHS = ["/login", "/register", "/forgot-password"];

export function proxy(request: NextRequest) {
  const { pathname } = request.nextUrl;

  if (
    PUBLIC_PATHS.some((p) => pathname.startsWith(p)) ||
    pathname === "/" ||
    pathname.startsWith("/_next") ||
    pathname.startsWith("/api") ||
    pathname.includes(".")
  ) {
    return NextResponse.next();
  }

  const hasToken = request.cookies.has(AUTH_COOKIE_NAMES.accessToken);

  if (!hasToken) {
    const loginUrl = new URL("/login", request.url);
    loginUrl.searchParams.set("redirect", pathname);
    return NextResponse.redirect(loginUrl);
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|favicon.ico).*)"],
};
