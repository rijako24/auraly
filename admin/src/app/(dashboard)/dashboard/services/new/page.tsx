"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FormattedNumberInput } from "@/components/ui/formatted-number-input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ServiceTier, ServiceType } from "@/types/enums";
import { ServiceTierLabels, ServiceTypeLabels } from "@/types/enums";
import { useCreateService, useServiceCategories } from "@/hooks/use-services";
import { useBusinessContextStore } from "@/stores/business-context-store";
import { PageLoading } from "@/components/ui/page-loading";
import { PageError } from "@/components/ui/page-error";

export default function NewServicePage() {
  const router = useRouter();
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data: categoriesData, isLoading, isError, refetch } = useServiceCategories({ pageSize: 100 });
  const createService = useCreateService();
  const [serviceName, setServiceName] = useState("");
  const [description, setDescription] = useState("");
  const [keywords, setKeywords] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [durationMinutes, setDurationMinutes] = useState("");
  const [price, setPrice] = useState("");
  const [tier, setTier] = useState<string>(String(ServiceTier.Base));
  const [serviceType, setServiceType] = useState<string>(String(ServiceType.Standard));
  const [includeInCheckoutTotal, setIncludeInCheckoutTotal] = useState(true);
  const [isActive, setIsActive] = useState(true);
  const [errors, setErrors] = useState<Record<string, string>>({});

  const validate = () => {
    const newErrors: Record<string, string> = {};
    if (!serviceName.trim()) newErrors.serviceName = "El nombre es requerido";
    if (!categoryId) newErrors.categoryId = "Seleccione una categoría";
    const duration = parseInt(durationMinutes, 10);
    if (!durationMinutes || isNaN(duration) || duration < 0) {
      newErrors.durationMinutes = "Duración inválida (minutos)";
    }
    const priceNum = Number(price);
    if (!price || isNaN(priceNum) || priceNum < 0) {
      newErrors.price = "Precio invalido en COP";
    }
    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validate() || !businessId) return;

    await createService.mutateAsync({
      businessId,
      serviceName: serviceName.trim(),
      description: description.trim(),
      keywords: keywords.trim() || null,
      categoryId,
      durationMinutes: parseInt(durationMinutes, 10),
      price: Number(price),
      includeInCheckoutTotal,
      tier: Number(tier),
      serviceType: Number(serviceType),
      isActive,
    });

    router.push("/dashboard/services");
  };

  const handleCancel = () => {
    router.push("/dashboard/services");
  };

  if (isLoading) return <PageLoading cards={0} />;
  if (isError) return <PageError onRetry={refetch} />;

  const categories = categoriesData?.items ?? [];

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/services">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Nuevo Servicio
          </h1>
          <p className="text-muted-foreground">
            Crear un nuevo servicio en el catálogo
          </p>
        </div>
      </div>

      <form onSubmit={handleSubmit}>
        <Card>
          <CardHeader>
            <CardTitle>Datos del servicio</CardTitle>
            <p className="text-sm text-muted-foreground">
              Complete los campos requeridos
            </p>
          </CardHeader>
          <CardContent className="space-y-6">
            <div className="space-y-2">
              <Label htmlFor="serviceName">Nombre del servicio</Label>
              <Input
                id="serviceName"
                value={serviceName}
                onChange={(e) => setServiceName(e.target.value)}
                placeholder="Nombre del servicio"
                className={errors.serviceName ? "border-destructive" : ""}
              />
              {errors.serviceName && (
                <p className="text-sm text-destructive">{errors.serviceName}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="description">Descripción</Label>
              <Textarea
                id="description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Descripción del servicio..."
                rows={4}
              />
            </div>


            <div className="space-y-2">
              <Label htmlFor="keywords">Keywords</Label>
              <Textarea
                id="keywords"
                value={keywords}
                onChange={(e) => setKeywords(e.target.value)}
                placeholder="Palabras o frases separadas por coma"
                rows={3}
              />
            </div>
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="categoryId">Categoría</Label>
                <Select
                  value={categoryId}
                  onValueChange={setCategoryId}
                >
                  <SelectTrigger
                    id="categoryId"
                    className={errors.categoryId ? "border-destructive" : ""}
                  >
                    <SelectValue placeholder="Seleccionar categoría" />
                  </SelectTrigger>
                  <SelectContent>
                    {categories.map((c) => (
                      <SelectItem key={c.serviceCategoryId} value={c.serviceCategoryId}>
                        {c.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {errors.categoryId && (
                  <p className="text-sm text-destructive">{errors.categoryId}</p>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="durationMinutes">Duración (minutos)</Label>
                <Input
                  id="durationMinutes"
                  type="number"
                  min={0}
                  value={durationMinutes}
                  onChange={(e) => setDurationMinutes(e.target.value)}
                  placeholder="60"
                  className={errors.durationMinutes ? "border-destructive" : ""}
                />
                {errors.durationMinutes && (
                  <p className="text-sm text-destructive">{errors.durationMinutes}</p>
                )}
              </div>
            </div>
<div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="price">Precio COP</Label>
                <FormattedNumberInput
                  id="price"
                  kind="currency"
                  value={price}
                  invalid={Boolean(errors.price)}
                  onValueChange={(value) => setPrice(value?.toString() ?? "")}
                  placeholder="120000.00"
                />
                <p className="text-xs text-muted-foreground">
                  Ej: 120000 = $120.000 COP
                </p>
                {errors.price && (
                  <p className="text-sm text-destructive">{errors.price}</p>
                )}
              </div>

              <div className="space-y-2">
                <Label htmlFor="tier">Tier</Label>
                <Select value={tier} onValueChange={setTier}>
                  <SelectTrigger id="tier">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {Object.entries(ServiceTierLabels).map(([val, label]) => (
                      <SelectItem key={val} value={val}>
                        {label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>
<div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="serviceType">Tipo de servicio</Label>
                <Select value={serviceType} onValueChange={setServiceType}>
                  <SelectTrigger id="serviceType">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {Object.entries(ServiceTypeLabels).map(([val, label]) => (
                      <SelectItem key={val} value={val}>
                        {label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="flex items-center gap-2 space-y-2 pt-8">
                <Switch
                  id="isActive"
                  checked={isActive}
                  onCheckedChange={setIsActive}
                />
                <Label htmlFor="isActive">Activo</Label>
              </div>
            </div>

            <div className="flex items-center gap-2 rounded-md border p-3">
              <Switch
                id="includeInCheckoutTotal"
                checked={includeInCheckoutTotal}
                onCheckedChange={setIncludeInCheckoutTotal}
              />
              <Label htmlFor="includeInCheckoutTotal">Suma al total del checkout</Label>
            </div>
          </CardContent>
        </Card>

        <div className="mt-6 flex gap-4">
          <Button type="submit" disabled={createService.isPending || categories.length === 0}>Crear Servicio</Button>
          <Button type="button" variant="outline" onClick={handleCancel}>
            Cancelar
          </Button>
        </div>
      </form>
    </div>
  );
}
