"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  ArrowLeft,
  ExternalLink,
  FileText,
  Package,
  ReceiptText,
  User,
} from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { useOrder } from "@/hooks/use-orders";
import { cn, formatCurrency, formatDateTime, truncate } from "@/lib/utils";
import {
  OrderFulfillmentModeLabels,
  OrderSourceLabels,
  OrderStatusColors,
  OrderStatusLabels,
} from "@/types/enums";

export default function OrderDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const { data: order, isLoading, isError, refetch } = useOrder(id);

  if (isLoading) return <PageLoading cards={3} />;
  if (isError || !order) return <PageError onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/orders">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex flex-1 flex-wrap items-center justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">
              Pedido {truncate(order.orderId, 12)}
            </h1>
            <p className="text-muted-foreground">Detalle del pedido</p>
          </div>
          <Badge className={cn("text-sm", OrderStatusColors[order.status])}>
            {OrderStatusLabels[order.status]}
          </Badge>
        </div>
      </div>

      <div className="grid gap-6 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ReceiptText className="h-5 w-5" />
              Totales
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <InfoRow label="Subtotal" value={formatCurrency(order.subtotal, order.currency)} />
            <InfoRow label="Descuento" value={formatCurrency(order.discountTotal, order.currency)} />
            <InfoRow label="Impuestos" value={formatCurrency(order.taxTotal, order.currency)} />
            <div className="flex justify-between gap-4 border-t pt-4">
              <span className="font-medium">Total</span>
              <span className="text-right text-lg font-semibold">
                {formatCurrency(order.total, order.currency)}
              </span>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <User className="h-5 w-5" />
              Cliente
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <InfoRow label="Nombre" value={order.customerName ?? "Sin cliente"} />
            <InfoRow label="Telefono" value={order.customerPhone ?? "Sin telefono"} />
            <InfoRow label="Email" value={order.customerEmail ?? "Sin email"} />
            <InfoRow label="Documento" value={order.customerDocument ?? "Sin documento"} />
            <InfoRow label="Direccion" value={order.deliveryAddress ?? "Sin direccion"} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <FileText className="h-5 w-5" />
              Registro
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <InfoRow label="Creado" value={formatDateTime(order.createdAt)} />
            <InfoRow
              label="Actualizado"
              value={order.updatedAt ? formatDateTime(order.updatedAt) : "Sin cambios"}
            />
            <InfoRow label="Origen" value={OrderSourceLabels[order.source]} />
            <InfoRow label="Entrega" value={OrderFulfillmentModeLabels[order.fulfillmentMode]} />
            <InfoRow label="Confirmado" value={order.customerConfirmed ? "Si" : "No"} />
          </CardContent>
        </Card>
      </div>

      <div className="flex flex-wrap gap-2">
        {order.conversationId && (
          <Button variant="outline" asChild>
            <Link href={`/dashboard/conversations/${order.conversationId}`}>
              <ExternalLink className="mr-2 h-4 w-4" />
              Ver conversacion
            </Link>
          </Button>
        )}
        {order.paymentTransactionId && (
          <Button variant="outline" asChild>
            <Link href={`/dashboard/payments/${order.paymentTransactionId}`}>
              <ExternalLink className="mr-2 h-4 w-4" />
              Ver pago
            </Link>
          </Button>
        )}
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Package className="h-5 w-5" />
            Items
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="rounded-md border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Producto</TableHead>
                  <TableHead>SKU</TableHead>
                  <TableHead className="text-right">Cantidad</TableHead>
                  <TableHead className="text-right">Unitario</TableHead>
                  <TableHead className="text-right">Total</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {order.items.length > 0 ? (
                  order.items.map((item) => (
                    <TableRow key={item.orderItemId}>
                      <TableCell>
                        <div className="space-y-1">
                          <p className="font-medium">{item.productName}</p>
                          {item.description && (
                            <p className="text-xs text-muted-foreground">{item.description}</p>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="font-mono text-xs">
                        {item.sku ?? item.externalProductId ?? "-"}
                      </TableCell>
                      <TableCell className="text-right">{item.quantity}</TableCell>
                      <TableCell className="text-right">
                        {formatCurrency(item.unitPrice, order.currency)}
                      </TableCell>
                      <TableCell className="text-right font-medium">
                        {formatCurrency(item.lineTotal, order.currency)}
                      </TableCell>
                    </TableRow>
                  ))
                ) : (
                  <TableRow>
                    <TableCell colSpan={5} className="h-24 text-center">
                      Sin items.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      {(order.notes || order.externalOrderId || order.externalStatus) && (
        <Card>
          <CardHeader>
            <CardTitle>Datos adicionales</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {order.notes && <InfoRow label="Notas" value={order.notes} />}
            {order.externalOrderId && <InfoRow label="ID externo" value={order.externalOrderId} />}
            {order.externalStatus && <InfoRow label="Estado externo" value={order.externalStatus} />}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4">
      <span className="text-muted-foreground">{label}</span>
      <span className="break-words text-right font-medium">{value}</span>
    </div>
  );
}
