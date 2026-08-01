import { NextResponse } from "next/server";

export async function POST() {
  return NextResponse.json(
    {
      message:
        "La creación pública de cuentas aún no está habilitada. Solicita el alta de tu empresa al equipo de Auraly.",
    },
    { status: 410 },
  );
}
