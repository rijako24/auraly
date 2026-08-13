import { NextResponse } from "next/server";

export const dynamic = "force-dynamic";

export function GET() {
  return NextResponse.json({
    desktop: process.env.AURALY_DESKTOP_LOCAL === "true",
  });
}
