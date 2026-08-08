import { NextRequest, NextResponse } from "next/server";
import { AUTH_COOKIE_NAMES } from "@/lib/auth-cookies";
import { getBackendUrl } from "@/lib/backend-url";

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
  const backendUrl = getBackendUrl();
  const url = `${backendUrl}/${path}${searchParams ? `?${searchParams}` : ""}`;

  const accessToken = request.cookies.get(AUTH_COOKIE_NAMES.accessToken)?.value;
  const businessId = request.headers.get("X-Business-Id");

  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(accessToken && { Authorization: `Bearer ${accessToken}` }),
    ...(businessId && { "X-Business-Id": businessId }),
  };

  let body: string | undefined;
  if (method !== "GET") {
    try {
      body = await request.text();
    } catch {
      body = undefined;
    }
  }

  try {
    const res = await fetch(url, {
      method,
      headers,
      body: body || undefined,
    });

    const data = await res.text();
    const contentType = res.headers.get("Content-Type") || "application/json";

    if (!res.ok) {
      return new NextResponse(data, {
        status: res.status,
        headers: { "Content-Type": contentType },
      });
    }

    return new NextResponse(data, {
      status: res.status,
      headers: { "Content-Type": contentType },
    });
  } catch (error) {
    console.error("[api/proxy]", method, path, error);
    return NextResponse.json(
      { message: "Error de conexión con el servidor" },
      { status: 502 }
    );
  }
}
