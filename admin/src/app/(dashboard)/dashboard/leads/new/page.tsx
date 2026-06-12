"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  LeadStatus,
  LeadStatusLabels,
} from "@/types/enums";
import { leadsApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function NewLeadPage() {
  const router = useRouter();
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const [customerName, setCustomerName] = useState("");
  const [userNumber, setUserNumber] = useState("");
  const [status, setStatus] = useState<LeadStatus>(LeadStatus.New);
  const [notes, setNotes] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);

  const validate = () => {
    const newErrors: Record<string, string> = {};
    if (!businessId) newErrors.business = "Seleccione un negocio";
    if (!userNumber.trim()) newErrors.userNumber = "El telefono es requerido";
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validate()) return;

    setIsSubmitting(true);
    try {
      const created = await leadsApi.create({
        businessId: businessId!,
        userNumber: userNumber.trim(),
        customerName: customerName.trim() || null,
        notes: notes.trim() || null,
      });

      if (status !== LeadStatus.New) {
        await leadsApi.update(created.leadId, {
          status,
          customerName: customerName.trim() || null,
          notes: notes.trim() || null,
        });
      }

      toast.success("Lead creado");
      router.push(`/dashboard/leads/${created.leadId}`);
    } catch {
      toast.error("No se pudo crear el lead");
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/leads">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Nuevo Lead
          </h1>
          <p className="text-muted-foreground">
            Crear un nuevo lead manualmente
          </p>
        </div>
      </div>

      <form onSubmit={handleSubmit}>
        <Card>
          <CardHeader>
            <CardTitle>Datos del lead</CardTitle>
            <p className="text-sm text-muted-foreground">
              Completa la información del potencial cliente
            </p>
          </CardHeader>
          <CardContent className="space-y-4">
            {errors.business && (
              <p className="text-sm text-destructive">{errors.business}</p>
            )}
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="customerName">Nombre del cliente</Label>
                <Input
                  id="customerName"
                  placeholder="Nombre del cliente"
                  value={customerName}
                  onChange={(e) => setCustomerName(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="userNumber">Número de teléfono</Label>
                <Input
                  id="userNumber"
                  placeholder="Ej: +57 300 123 4567"
                  value={userNumber}
                  onChange={(e) => setUserNumber(e.target.value)}
                  className={errors.userNumber ? "border-destructive" : ""}
                  required
                />
                {errors.userNumber && (
                  <p className="text-sm text-destructive">{errors.userNumber}</p>
                )}
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="status">Estado</Label>
              <Select
                value={status}
                onValueChange={(v) => setStatus(v as LeadStatus)}
              >
                <SelectTrigger id="status">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {Object.entries(LeadStatusLabels).map(([value, label]) => (
                    <SelectItem key={value} value={value}>
                      {label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="notes">Notas</Label>
              <Textarea
                id="notes"
                placeholder="Notas sobre el lead, preferencias, seguimiento..."
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                rows={4}
              />
            </div>
            <div className="flex gap-2 pt-4">
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? "Creando..." : "Crear Lead"}
              </Button>
              <Button type="button" variant="outline" asChild>
                <Link href="/dashboard/leads">Cancelar</Link>
              </Button>
            </div>
          </CardContent>
        </Card>
      </form>
    </div>
  );
}
