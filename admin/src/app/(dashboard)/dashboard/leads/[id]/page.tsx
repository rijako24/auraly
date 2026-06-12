"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, MessageSquare, Phone } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useLead } from "@/hooks/use-leads";
import { cn, formatDateTime } from "@/lib/utils";
import { LeadStatusColors, LeadStatusLabels } from "@/types/enums";

export default function LeadDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: lead, isLoading, isError, refetch } = useLead(id);

  if (isLoading) return <PageLoading cards={2} />;
  if (isError || !lead) return <PageError onRetry={refetch} />;

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
            <CardTitle>Informacion del lead</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <p className="text-sm font-medium text-muted-foreground">Nombre</p>
                <p>{lead.customerName ?? "Sin nombre"}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Telefono</p>
                <p className="flex items-center gap-2">
                  <Phone className="h-4 w-4" />
                  {lead.userNumber}
                </p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Estado</p>
                <Badge className={cn(LeadStatusColors[lead.status])}>
                  {LeadStatusLabels[lead.status]}
                </Badge>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Fecha</p>
                <p>{formatDateTime(lead.timestamp)}</p>
              </div>
            </div>
            {lead.notes && (
              <div>
                <p className="text-sm font-medium text-muted-foreground">Notas</p>
                <p className="mt-1 rounded-md bg-muted/50 p-3 text-sm">
                  {lead.notes}
                </p>
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
                Sin conversaciones asociadas en esta vista.
              </p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
