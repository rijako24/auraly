"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, ChevronDown, ChevronUp, ExternalLink } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  PaymentTransactionStatus,
  PaymentTransactionSource,
  PaymentStatusLabels,
  PaymentStatusColors,
  PaymentSourceLabels,
} from "@/types/enums";
import type { PaymentTransaction } from "@/types/entities";
import { formatCurrency, formatDateTime } from "@/lib/utils";
import { cn } from "@/lib/utils";

// Mock data
const MOCK_PAYMENTS: Record<string, PaymentTransaction> = {
  "pay-1": {
    paymentTransactionId: "pay-1",
    businessId: "bus-1",
    conversationId: "conv-1",
    paymentReferenceId: "REF-2025-001",
    providerTransactionId: "prov-txn-001",
    amountInCents: 12000000,
    currency: "COP",
    status: PaymentTransactionStatus.Confirmed,
    source: PaymentTransactionSource.Automated,
    createdAt: "2025-03-14T10:30:00Z",
    confirmedAt: "2025-03-14T10:32:15Z",
    webhookPayloadJson: JSON.stringify(
      {
        event: "payment.confirmed",
        transactionId: "prov-txn-001",
        amount: 120000,
        currency: "COP",
        timestamp: "2025-03-14T10:32:15Z",
        metadata: { source: "whatsapp" },
      },
      null,
      2
    ),
  },
};

export default function PaymentDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const payment = MOCK_PAYMENTS[id] ?? MOCK_PAYMENTS["pay-1"];
  const [webhookOpen, setWebhookOpen] = useState(false);

  if (!payment) {
    return (
      <div className="space-y-6">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/payments">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <p className="text-muted-foreground">Pago no encontrado.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/payments">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">
            {payment.paymentReferenceId}
          </h1>
          <p className="text-muted-foreground">Detalle del pago</p>
        </div>
        <Badge className={cn(PaymentStatusColors[payment.status])}>
          {PaymentStatusLabels[payment.status]}
        </Badge>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Información del pago</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  ID Referencia
                </p>
                <p className="font-mono text-sm">{payment.paymentReferenceId}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">Monto</p>
                <p className="font-semibold">
                  {formatCurrency(payment.amountInCents, payment.currency)}
                </p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Moneda
                </p>
                <p>{payment.currency}</p>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Estado
                </p>
                <Badge className={cn(PaymentStatusColors[payment.status])}>
                  {PaymentStatusLabels[payment.status]}
                </Badge>
              </div>
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Origen
                </p>
                <Badge variant="outline">
                  {PaymentSourceLabels[payment.source]}
                </Badge>
              </div>
              {payment.providerTransactionId && (
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    ID Proveedor
                  </p>
                  <p className="font-mono text-sm">
                    {payment.providerTransactionId}
                  </p>
                </div>
              )}
              <div>
                <p className="text-sm font-medium text-muted-foreground">
                  Creado
                </p>
                <p>{formatDateTime(payment.createdAt)}</p>
              </div>
              {payment.confirmedAt && (
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Confirmado
                  </p>
                  <p>{formatDateTime(payment.confirmedAt)}</p>
                </div>
              )}
            </div>

            <Button asChild variant="outline" className="mt-4">
              <Link href={`/dashboard/conversations/${payment.conversationId}`}>
                <ExternalLink className="mr-2 h-4 w-4" />
                Ver conversación
              </Link>
            </Button>
          </CardContent>
        </Card>

        {payment.webhookPayloadJson && (
          <Card>
            <Collapsible open={webhookOpen} onOpenChange={setWebhookOpen}>
              <CollapsibleTrigger asChild>
                <CardHeader className="cursor-pointer hover:bg-muted/50 rounded-t-lg transition-colors">
                  <div className="flex items-center justify-between">
                    <CardTitle>Webhook payload</CardTitle>
                    {webhookOpen ? (
                      <ChevronUp className="h-4 w-4" />
                    ) : (
                      <ChevronDown className="h-4 w-4" />
                    )}
                  </div>
                  <p className="text-sm text-muted-foreground">
                    JSON recibido del webhook de pago
                  </p>
                </CardHeader>
              </CollapsibleTrigger>
              <CollapsibleContent>
                <CardContent>
                  <pre className="overflow-auto rounded-md bg-muted p-4 text-xs">
                    {payment.webhookPayloadJson}
                  </pre>
                </CardContent>
              </CollapsibleContent>
            </Collapsible>
          </Card>
        )}
      </div>
    </div>
  );
}
