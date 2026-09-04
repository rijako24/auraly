import { NextRequest, NextResponse } from "next/server";
import { AUTH_COOKIE_NAMES } from "@/lib/auth-cookies";
import { readBackendProxyBody } from "@/lib/backend-proxy-body";
import { buildBackendProxyHeaders } from "@/lib/backend-proxy-headers";
import { getBackendRequestUrl } from "@/lib/backend-request-url";

export async function GET(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> }
) {
  const { path } = await params;
  return proxy(request, path, "GET");
}

export async function POST(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> }
) {
  const { path } = await params;
  return proxy(request, path, "POST");
}

export async function PUT(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> }
) {
  const { path } = await params;
  return proxy(request, path, "PUT");
}

export async function PATCH(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> }
) {
  const { path } = await params;
  return proxy(request, path, "PATCH");
}

export async function DELETE(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> }
) {
  const { path } = await params;
  return proxy(request, path, "DELETE");
}

async function proxy(
  request: NextRequest,
  pathSegments: string[],
  method: string
) {
  const path = pathSegments.join("/");
  const searchParams = request.nextUrl.searchParams.toString();
  const url = getBackendRequestUrl(path, searchParams);

  const accessToken = request.cookies.get(AUTH_COOKIE_NAMES.accessToken)?.value;
  const authenticationClientId = request.cookies.get(
    AUTH_COOKIE_NAMES.clientId,
  )?.value;
  const headers = buildBackendProxyHeaders(
    request.headers,
    accessToken,
    authenticationClientId,
  );

  const body = await readBackendProxyBody(request, method);

  try {
    const res = await fetch(url, {
      method,
      headers,
      body,
    });

    const responseHeaders = new Headers();
    for (const name of [
      "content-type",
      "content-disposition",
      "content-length",
      "cache-control",
      "accept-ranges",
      "etag",
      "last-modified",
    ]) {
      const value = res.headers.get(name);
      if (value) responseHeaders.set(name, value);
    }

    return new NextResponse(res.body, {
      status: res.status,
      headers: responseHeaders,
    });
  } catch (error) {
    console.error("[api/proxy]", method, path, error);
    return NextResponse.json(
      { message: "Error de conexión con el servidor" },
      { status: 502 }
    );
  }
}
