"use client";

import Link from "next/link";
import { ArrowLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export default function NotificationsSettingsPage() {
  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/settings"><ArrowLeft className="h-4 w-4" /></Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Notificaciones</h1>
          <p className="text-muted-foreground">Preferencias de notificaciones</p>
        </div>
      </div>

      <Card>
        <CardHeader><CardTitle>Preferencias</CardTitle></CardHeader>
        <CardContent className="py-8 text-sm text-muted-foreground">
          La API aun no expone preferencias de notificaciones para este usuario.
        </CardContent>
      </Card>
    </div>
  );
}
