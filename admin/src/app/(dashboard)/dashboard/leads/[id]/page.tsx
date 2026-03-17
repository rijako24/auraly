"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, MessageSquare, Phone } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  LeadStatus,
  LeadStatusLabels,
  LeadStatusColors,
} from "@/types/enums";
import type { Lead } from "@/types/entities";
import { formatDate, formatDateTime } from "@/lib/utils";
import { cn } from "@/lib/utils";

const MOCK_LEADS: Record<string, Lead> = {
  "lead-1": {
    leadId: "lead-1",
    businessId: "bus-1",
    userNumber: "+57 300 123 4567",
    status: LeadStatus.New,
    timestamp: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
    customerName: "María García",
    notes:
      "Interesada en el paquete premium para bebé de 4 meses. Mencionó que prefiere horarios de la mañana.",
  },
  "lead-2": {
    leadId: "lead-2",
    businessId: "bus-1",
    userNumber: "+57 310 234 5678",
    status: LeadStatus.Contacted,
    timestamp: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
    customerName: "Carlos Rodríguez",
    notes:
      "Llamada realizada. Agenda cita para próxima semana. Cliente muy receptivo.",
  },
  "lead-3": {
    leadId: "lead-3",
    businessId: "bus-1",
    userNumber: "+57 320 345 6789",
    status: LeadStatus.Closed,
    timestamp: new Date(Date.now() - 3 * 24 * 60 * 60 * 1000).toISOString(),
    customerName: "Ana López",
    notes: "Cliente cerró reserva. Satisfecho con el servicio. Alta probabilidad de retorno.",
  },
};

export default function LeadDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const [lead, setLead] = useState<Lead | null>(
    MOCK_LEADS[id] ?? (Object.values(MOCK_LEADS)[0] as Lead)
  );

  const handleStatusChange = (newStatus: LeadStatus) => {
    if (lead) setLead({ ...lead, status: newStatus });
  };

  if (!lead) {
    return (
      <div className="space-y-6">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/leads">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <p className="text-muted-foreground">Lead no encontrado.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/leads">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">
              {lead.customerName ?? lead.userNumber}
            </h1>
            <Badge className={cn(LeadStatusColors[lead.status])}>
              {LeadStatusLabels[lead.status]}
            </Badge>
          </div>
          <p className="text-muted-foreground">Detalle del lead</p>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Información del lead</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Nombre
                </p>
                <p>{lead.customerName ?? "—"}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Teléfono
                </p>
                <p className="flex items-center gap-2">
                  <Phone className="h-4 w-4" />
                  {lead.userNumber}
                </p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Estado
                </p>
                <Badge className={cn(LeadStatusColors[lead.status])}>
                  {LeadStatusLabels[lead.status]}
                </Badge>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Fecha
                </p>
                <p>{formatDateTime(lead.timestamp)}</p>
              </div>
            </div>
            {lead.notes && (
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Notas
                </p>
                <p className="mt-1 rounded-md bg-muted/50 p-3 text-sm">
                  {lead.notes}
                </p>
              </div>
            )}
            {lead.status !== LeadStatus.Closed && (
              <div className="flex flex-wrap gap-2 pt-2">
                {lead.status === LeadStatus.New && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleStatusChange(LeadStatus.Contacted)}
                  >
                    Marcar como Contactado
                  </Button>
                )}
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => handleStatusChange(LeadStatus.Closed)}
                >
                  Marcar como Cerrado
                </Button>
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Conversaciones relacionadas</CardTitle>
            <p className="text-sm text-muted-foreground">
              Historial de conversaciones con este lead
            </p>
          </CardHeader>
          <CardContent>
            <div className="flex flex-col items-center justify-center rounded-md border border-dashed py-12 text-center">
              <MessageSquare className="h-12 w-12 text-muted-foreground/50" />
              <p className="mt-2 text-sm text-muted-foreground">
                Sin conversaciones asociadas
              </p>
              <p className="text-xs text-muted-foreground">
                Las conversaciones vinculadas aparecerán aquí
              </p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
