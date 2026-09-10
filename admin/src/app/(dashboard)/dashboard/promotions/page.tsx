"use client";

import { useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { BadgePercent, Building2, MoreHorizontal, Pencil, Plus, PowerOff, ShoppingBasket } from "lucide-react";
import { toast } from "sonner";

import { PromotionEditDialog } from "@/components/promotions/promotion-edit-dialog";
import { DataTable } from "@/components/tables/data-table";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { PageError } from "@/components/ui/page-error";
import { useBusinesses } from "@/hooks/use-businesses";
import { useProductCategories } from "@/hooks/use-products";
import { useDeletePromotion, usePromotions } from "@/hooks/use-promotions";
import { useAuthStore } from "@/stores/auth-store";
import { useBusinessContextStore } from "@/stores/business-context-store";
import type { Promotion } from "@/types/entities";
import { PromotionBenefitType, PromotionBenefitTypeLabels, PromotionItemType } from "@/types/enums";

export default function PromotionsPage() {
  const businessId = useBusinessContextStore((state) => state.selectedBusinessId);
  const permissionValues = useAuthStore((state) => state.user?.permissions);
  const permissions = useMemo(() => new Set(permissionValues ?? []), [permissionValues]);
  const canCreate = permissions.has("promotions.create");
  const canUpdate = permissions.has("promotions.update");
  const canDelete = permissions.has("promotions.delete");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState("");
  const [editingPromotion, setEditingPromotion] = useState<Promotion | null>(null);
  const [creating, setCreating] = useState(false);
  const [pendingDeactivation, setPendingDeactivation] = useState<Promotion | null>(null);
  const promotionsQuery = usePromotions({ page, pageSize, search: search || undefined });
  const businessesQuery = useBusinesses({ page: 1, pageSize: 200 });
  const categoriesQuery = useProductCategories();
  const deactivate = useDeletePromotion();
  const promotions = promotionsQuery.data?.items ?? [];
  const categoryNames = useMemo(
    () => new Map((categoriesQuery.data ?? []).map((category) => [category.productCategoryId, category.name])),
    [categoriesQuery.data],
  );

  async function confirmDeactivation() {
    if (!pendingDeactivation) return;
    try {
      await deactivate.mutateAsync(pendingDeactivation.promotionId);
      toast.success("Promoción desactivada y sincronizada");
      setPendingDeactivation(null);
    } catch {
      toast.error("No fue posible desactivar la promoción");
    }
  }

  const columns: ColumnDef<Promotion>[] = useMemo(() => [
    {
      accessorKey: "name",
      header: "Promoción",
      cell: ({ row }) => <div><p className="font-medium">{row.original.name}</p><p className="mt-0.5 max-w-sm truncate text-xs text-muted-foreground">{describePromotion(row.original, categoryNames)}</p></div>,
    },
    { id: "discount", header: "Descuento", cell: ({ row }) => describeBenefit(row.original) },
    { id: "condition", header: "Condición", cell: ({ row }) => describeCondition(row.original, categoryNames) },
    { id: "scope", header: "Sedes", cell: ({ row }) => <span className="inline-flex items-center gap-1.5"><Building2 className="h-4 w-4 text-emerald-600" />{row.original.appliesToAllBusinesses ? "Todas" : `${row.original.applicableBusinessIds?.length ?? 0} seleccionada(s)`}</span> },
    { accessorKey: "isActive", header: "Estado", cell: ({ row }) => <Badge className={row.original.isActive ? "bg-emerald-100 text-emerald-800 hover:bg-emerald-100" : "bg-slate-100 text-slate-600 hover:bg-slate-100"}>{row.original.isActive ? "Activa" : "Inactiva"}</Badge> },
    {
      id: "actions",
      cell: ({ row }) => !canUpdate && (!canDelete || !row.original.isActive) ? null : <DropdownMenu>
        <DropdownMenuTrigger asChild><Button variant="ghost" size="icon" aria-label={`Acciones de ${row.original.name}`}><MoreHorizontal className="h-4 w-4" /></Button></DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {canUpdate && <DropdownMenuItem onClick={() => setEditingPromotion(row.original)}><Pencil className="mr-2 h-4 w-4" />Editar configuración</DropdownMenuItem>}
          {canDelete && row.original.isActive && <><DropdownMenuSeparator /><DropdownMenuItem className="text-destructive" onClick={() => setPendingDeactivation(row.original)}><PowerOff className="mr-2 h-4 w-4" />Desactivar</DropdownMenuItem></>}
        </DropdownMenuContent>
      </DropdownMenu>,
    },
  ], [canDelete, canUpdate, categoryNames]);

  if (promotionsQuery.isError) return <PageError onRetry={promotionsQuery.refetch} />;
  if (!businessId) return <div className="p-10 text-center text-muted-foreground">Selecciona una sede para administrar promociones.</div>;

  const allBusinesses = businessesQuery.data?.items ?? [];
  return <div className="space-y-6 p-6">
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div><p className="text-sm font-medium text-emerald-600">Precios y ventas</p><h1 className="text-3xl font-bold tracking-tight">Promociones</h1><p className="mt-1 text-muted-foreground">Crea descuentos directos o activa un descuento cuando el cliente compra otro producto.</p></div>
      {canCreate && <Button className="rounded-xl" onClick={() => setCreating(true)}><Plus className="mr-2 h-4 w-4" />Nueva promoción</Button>}
    </header>

    <div className="grid gap-3 md:grid-cols-3">
      <Metric icon={BadgePercent} label="Promociones registradas" value={promotionsQuery.data?.totalCount ?? 0} />
      <Metric icon={ShoppingBasket} label="Configuración disponible" value="Producto o categoría" />
      <Metric icon={Building2} label="Cobertura predeterminada" value="Todas las sedes" />
    </div>

    <Card className="rounded-2xl border-emerald-100 bg-emerald-50/30"><CardContent className="grid gap-3 pt-6 sm:grid-cols-3"><GuideStep number="1" title="Elige el beneficio" text="Producto, categoría o todo el catálogo." /><GuideStep number="2" title="Agrega la compra" text="Opcional: exige otro producto o categoría." /><GuideStep number="3" title="Confirma las sedes" text="Todas por defecto o una selección." /></CardContent></Card>

    <Card className="rounded-2xl"><CardContent className="pt-6">
      <DataTable
        columns={columns}
        data={promotions}
        searchKey="name"
        searchPlaceholder="Buscar promociones por nombre..."
        isLoading={promotionsQuery.isLoading}
        page={page}
        pageSize={pageSize}
        pageCount={promotionsQuery.data?.totalPages ?? 0}
        totalItems={promotionsQuery.data?.totalCount ?? 0}
        onSearch={(value) => { setSearch(value.trim()); setPage(1); }}
        onPaginationChange={(nextPage, nextPageSize) => { setPage(nextPage); setPageSize(nextPageSize); }}
        enableRowSelection={false}
      />
    </CardContent></Card>

    {creating && <PromotionEditDialog businessId={businessId} businesses={allBusinesses} onClose={() => setCreating(false)} />}
    {editingPromotion && <PromotionEditDialog promotion={editingPromotion} businessId={businessId} businesses={allBusinesses} onClose={() => setEditingPromotion(null)} />}

    <Dialog open={Boolean(pendingDeactivation)} onOpenChange={(open) => !open && setPendingDeactivation(null)}>
      <DialogContent><DialogHeader><DialogTitle>Desactivar promoción</DialogTitle><DialogDescription>“{pendingDeactivation?.name}” dejará de aplicarse y el cambio se enviará a las cajas de las sedes incluidas.</DialogDescription></DialogHeader><DialogFooter><Button variant="outline" onClick={() => setPendingDeactivation(null)}>Cancelar</Button><Button variant="destructive" onClick={() => void confirmDeactivation()} disabled={deactivate.isPending}>{deactivate.isPending ? "Desactivando..." : "Desactivar"}</Button></DialogFooter></DialogContent>
    </Dialog>
  </div>;
}

function Metric({ icon: Icon, label, value }: { icon: typeof BadgePercent; label: string; value: string | number }) { return <Card><CardContent className="flex items-center gap-3 pt-6"><span className="rounded-xl bg-emerald-50 p-3 text-emerald-700"><Icon className="h-5 w-5" /></span><div><p className="text-sm text-muted-foreground">{label}</p><p className="text-xl font-bold">{value}</p></div></CardContent></Card>; }
function GuideStep({ number, title, text }: { number: string; title: string; text: string }) { return <div className="flex gap-3"><span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-emerald-600 text-sm font-bold text-white">{number}</span><div><p className="font-medium">{title}</p><p className="text-sm text-muted-foreground">{text}</p></div></div>; }
function describeBenefit(promotion: Promotion) { const benefit = promotion.benefits[0]; if (!benefit) return "Sin beneficio"; if (benefit.benefitType === PromotionBenefitType.PercentageDiscount) return <span className="font-semibold text-emerald-700">{benefit.discountPercentage ?? 0}%</span>; return PromotionBenefitTypeLabels[benefit.benefitType]; }
function describeCondition(promotion: Promotion, categoryNames: Map<string, string>) { const condition = promotion.conditions[0]; if (!condition) return "Sin compra previa"; if (condition.itemType === PromotionItemType.Product) return `Comprar ${condition.minQuantity} de un producto`; if (condition.itemType === PromotionItemType.ProductCategory) return `Comprar ${condition.minQuantity} de ${categoryNames.get(condition.productCategoryId ?? "") ?? "una categoría"}`; return condition.minSubtotal ? `Compra mínima configurada` : "Sin compra previa"; }
function describePromotion(promotion: Promotion, categoryNames: Map<string, string>) { const benefit = promotion.benefits[0]; if (!benefit) return "Sin regla de descuento"; const target = benefit.targetItemType === PromotionItemType.Product ? "un producto" : benefit.targetItemType === PromotionItemType.ProductCategory ? categoryNames.get(benefit.productCategoryId ?? "") ?? "una categoría" : "todos los productos"; const units = benefit.appliesToQuantity ? `, hasta ${benefit.appliesToQuantity} unidad(es)` : ""; return `Descuento sobre ${target}${units}`; }
