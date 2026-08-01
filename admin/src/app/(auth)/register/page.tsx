import Link from "next/link";

import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export default function RegisterPage() {
  return (
    <Card className="border-0 bg-transparent text-[#151515] shadow-none">
      <CardHeader className="px-0">
        <CardTitle className="text-2xl">Crear una cuenta en Auraly</CardTitle>
        <CardDescription>
          El alta pública todavía no está habilitada. Así evitamos crear empresas
          incompletas sin negocio, permisos ni configuración operativa.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3 px-0">
        <Button asChild className="w-full bg-[#151515] text-white hover:bg-black">
          <Link href="/?contact=demo">Solicitar acceso</Link>
        </Button>
        <Button asChild variant="outline" className="w-full">
          <Link href="/login">Volver a iniciar sesión</Link>
        </Button>
      </CardContent>
    </Card>
  );
}
