import { NextRequest, NextResponse } from "next/server";
import { getBackendRequestUrl } from "@/lib/backend-request-url";

export async function POST(request: NextRequest) {
  const response = await fetch(getBackendRequestUrl("auth/password-recovery/request"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(await request.json()),
    cache: "no-store",
  });
  const body = await response.json().catch(() => ({}));
  return NextResponse.json(body, { status: response.status });
}