import { NextRequest, NextResponse } from "next/server";
import { AUTH_COOKIE_NAMES } from "@/lib/auth-cookies";

const BACKEND_URL =
  process.env.NEXT_PUBLIC_API_URL || "http://localhost:5057/api";

export async function POST(
  request: NextRequest,
  { params }: { params: { businessId: string } }
) {
  const accessToken = request.cookies.get(AUTH_COOKIE_NAMES.accessToken)?.value;
  const formData = await request.formData();

  const url = `${BACKEND_URL}/businesses/${params.businessId}/catalog/extract`;

  try {
    const res = await fetch(url, {
      method: "POST",
      headers: {
        ...(accessToken && { Authorization: `Bearer ${accessToken}` }),
      },
      body: formData,
    });

    const data = await res.text();
    const contentType = res.headers.get("Content-Type") || "application/json";

    return new NextResponse(data, {
      status: res.status,
      headers: { "Content-Type": contentType },
    });
  } catch (error) {
    console.error("[catalog/extract]", error);
    return NextResponse.json(
      { message: "Error de conexión con el servidor" },
      { status: 502 }
    );
  }
}
