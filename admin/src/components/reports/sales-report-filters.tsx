"use client";

import {useState,type ReactNode} from "react";
import {Button} from "@/components/ui/button";
import {Card,CardContent} from "@/components/ui/card";
import {Input} from "@/components/ui/input";
import {DatePicker} from "@/components/ui/date-picker";
import {Select,SelectContent,SelectItem,SelectTrigger,SelectValue} from "@/components/ui/select";
import {PagedEntitySelect,type PagedEntityOption} from "@/components/forms/paged-entity-select";
import {PartyRoleSelect} from "@/components/parties/party-role-select";
import {salesReportingApi,type SalesReportContext,type SalesReportFilter,type SalesReportOption,type SalesReportProductFilter} from "@/services/api/sales-reporting";

export const reportSelectionFields=[
  {key:"areaIds",label:"Área",dimension:"area"},
  {key:"lineIds",label:"Línea",dimension:"category-line"},
  {key:"groupIds",label:"Grupo",dimension:"category-group"},
  {key:"subgroupIds",label:"Subgrupo",dimension:"subgroup"},
  {key:"customerIds",legacy:"customerId",label:"Clientes",role:"Customer"},
  {key:"supplierIds",legacy:"supplierId",label:"Proveedores",role:"Supplier"},
  {key:"sellerIds",legacy:"sellerId",label:"Vendedores",role:"Seller"},
  {key:"billedByUserIds",legacy:"billedByUserId",label:"Usuarios que facturaron",role:"User"},
  {key:"waiterPartyIds",legacy:"waiterPartyId",label:"Meseros",role:"Waiter"},
  {key:"tableIds",label:"Mesas",dimension:"table"},
  {key:"productIds",legacy:"productId",label:"Productos",dimension:"product"},
  {key:"warehouseIds",legacy:"warehouseId",label:"Bodegas",dimension:"warehouse"},
  {key:"categoryIds",legacy:"categoryId",label:"Categoría final histórica",dimension:"category"},
] as const;

export function reportFilterFromQuery(get:(key:string)=>string|null,defaults:{from:string;to:string}):SalesReportFilter{
  return {from:get("from")??defaults.from,to:get("to")??defaults.to,
    documentType:(get("documentType")??undefined) as SalesReportFilter["documentType"],
    ...Object.fromEntries(reportSelectionFields.map(field=>[field.key,
      [...new Set((get(field.key)??("legacy" in field?get(field.legacy):null)??"").split(",").filter(Boolean))]]))};
}

export function reportFilterQueryChanges(filter:SalesReportFilter):Record<string,string|null>{
  const changes:Record<string,string|null>={from:filter.from,to:filter.to,documentType:filter.documentType??null};
  for(const field of reportSelectionFields){changes[field.key]=filter[field.key]?.join(",")||null;if("legacy" in field)changes[field.legacy]=null;}
  return changes;
}

function productCriteriaCount(value?:SalesReportProductFilter){return !value?0:Number(value.marginFrom!==undefined)+Number(value.marginTo!==undefined)+Number(value.returnActivity!=="all")+Number(Boolean(value.compositionKind));}

export function SalesReportFilters({value,context,scope,onApply,children,supplier=false,products}:{
  value:SalesReportFilter;context:SalesReportContext;scope:string;onApply:(filter:SalesReportFilter,products?:SalesReportProductFilter)=>void;children?:ReactNode;supplier?:boolean;products?:SalesReportProductFilter;
}){
  const [draft,setDraft]=useState(value),[open,setOpen]=useState(()=>reportSelectionFields.some(field=>value[field.key]?.length)||productCriteriaCount(products)>0);
  const [productDraft,setProductDraft]=useState(products);
  const [picked,setPicked]=useState<Partial<Record<typeof reportSelectionFields[number]["key"],PagedEntityOption[]>>>({});
  const count=reportSelectionFields.reduce((sum,field)=>sum+(draft[field.key]?.length??0),0);
  const validProducts=!productDraft||([productDraft.marginFrom,productDraft.marginTo].every(value=>value===undefined||(Number.isFinite(value)&&Math.abs(value)<=1000000&&Math.round(value*100)/100===value))&&(productDraft.marginFrom===undefined||productDraft.marginTo===undefined||productDraft.marginFrom<=productDraft.marginTo));
  const valid=validProducts&&Boolean(draft.from&&draft.to&&draft.from<=draft.to&&count<=100);
  const dirty=JSON.stringify(value)!==JSON.stringify(draft)||JSON.stringify(products)!==JSON.stringify(productDraft);
  return <Card><CardContent className="space-y-4 p-5">
    <div className="grid gap-3 md:grid-cols-4"><FilterField label="Desde"><DatePicker value={draft.from} onChange={from=>setDraft(current=>({...current,from}))}/></FilterField>
      <FilterField label="Hasta"><DatePicker value={draft.to} onChange={to=>setDraft(current=>({...current,to}))}/></FilterField>
      <FilterField label="Tipo de documento de venta"><Select value={draft.documentType??"all"} onValueChange={value=>setDraft(current=>({...current,documentType:value==="all"?undefined:value as SalesReportFilter["documentType"]}))}><SelectTrigger><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Todos</SelectItem>{context.documentTypes.map(option=><SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></FilterField>{children}</div>
    <div className="flex flex-wrap items-center justify-between gap-3"><Button variant="ghost" className="h-auto max-w-full whitespace-normal text-left" aria-expanded={open} onClick={()=>setOpen(current=>!current)}>{open?"Ocultar filtros":"Expandir filtros"}{count?` · ${count} selecciones`:""}{productCriteriaCount(productDraft)?` · ${productCriteriaCount(productDraft)} criterios de rentabilidad`:""}</Button>
      <div className="flex gap-2"><Button variant="outline" onClick={()=>{const cleared={from:draft.from,to:draft.to,documentType:draft.documentType};setDraft(cleared);setPicked({});const clearedProducts=products?{returnActivity:"all"}:undefined;setProductDraft(clearedProducts);onApply(cleared,clearedProducts);}}>Limpiar selecciones</Button><Button disabled={!valid||!dirty} onClick={()=>onApply(draft,productDraft)}>Aplicar filtros</Button></div></div>
    {open&&<div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">{reportSelectionFields.filter(field=>(field.key!=="waiterPartyIds"&&field.key!=="tableIds")||context.isRestaurant).map(field=>{
      const selected=draft[field.key]??[];
      const changed=(ids:string[],options:PagedEntityOption[])=>{setDraft(current=>({...current,[field.key]:ids}));setPicked(current=>({...current,[field.key]:options}));};
      return <FilterField key={field.key} label={field.label}>{"role" in field?<PartyRoleSelect role={field.role} includePartyId={field.role==="Waiter"} value="" onChange={()=>{}} values={selected} selectedOptions={picked[field.key]} onValuesChange={changed} placeholder="Todos" ariaLabel={field.label}/>
        :<PagedEntitySelect queryKey={["sales-report-filter-options",scope,field.dimension,value.from,value.to,supplier?"supplier":"sales"]} value="" onChange={()=>{}} values={selected} selectedOptions={picked[field.key]} onValuesChange={changed} placeholder="Todos" ariaLabel={field.label}
          getOption={(row:SalesReportOption)=>({value:row.code,label:row.label})}
          loadPage={async(search,page,pageSize)=>{const result=await salesReportingApi.filterOptions({filter:{from:value.from,to:value.to},dimension:field.dimension,page,pageSize,search:search||undefined,includePurchases:supplier});return {...result,totalPages:Math.ceil(result.totalCount/pageSize)};}}/>}
        {selected.length>0&&<button type="button" className="text-xs text-muted-foreground underline" onClick={()=>changed([],[])}>Quitar {selected.length} selecciones</button>}
      </FilterField>;
    })}</div>}
    {open&&productDraft&&<div className="space-y-3 rounded-xl border bg-muted/30 p-4"><p className="text-sm font-semibold">Análisis de rentabilidad por producto</p><div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
      <FilterField label="Margen mínimo (%)"><Input type="number" step="0.01" aria-label="Margen mínimo (%)" value={productDraft.marginFrom??""} onChange={event=>setProductDraft({...productDraft,marginFrom:event.target.value===""?undefined:Number(event.target.value)})}/></FilterField>
      <FilterField label="Margen máximo (%)"><Input type="number" step="0.01" aria-label="Margen máximo (%)" value={productDraft.marginTo??""} onChange={event=>setProductDraft({...productDraft,marginTo:event.target.value===""?undefined:Number(event.target.value)})}/></FilterField>
      <FilterField label="Devoluciones del producto"><Select value={productDraft.returnActivity} onValueChange={returnActivity=>setProductDraft({...productDraft,returnActivity})}><SelectTrigger aria-label="Devoluciones del producto"><SelectValue/></SelectTrigger><SelectContent>{context.productReturnActivities.map(option=><SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></FilterField>
      <FilterField label="Composición histórica"><Select value={productDraft.compositionKind??"all"} onValueChange={value=>setProductDraft({...productDraft,compositionKind:value==="all"?undefined:value})}><SelectTrigger aria-label="Composición histórica"><SelectValue/></SelectTrigger><SelectContent><SelectItem value="all">Todas las composiciones</SelectItem>{context.productCompositions.map(option=><SelectItem key={option.code} value={option.code}>{option.label}</SelectItem>)}</SelectContent></Select></FilterField>
    </div><p className="text-xs text-muted-foreground">El rango evalúa el margen sin redondear; las cifras visibles se redondean. El margen y las devoluciones seleccionan productos por su resultado en todo el período y con los filtros comunes aplicados. Abrir categorías o comprobantes conserva ese mismo conjunto. El rango de margen excluye productos sin margen calculable.</p></div>}
    {!validProducts&&<p role="alert" className="text-sm text-destructive">Revisa el rango de margen: usa hasta dos decimales y un mínimo que no supere el máximo.</p>}
    {count>100&&<p role="alert" className="text-sm text-destructive">Selecciona hasta 100 valores entre todos los filtros.</p>}
    {!Boolean(draft.from&&draft.to&&draft.from<=draft.to)&&count<=100&&<p role="alert" className="text-sm text-destructive">Selecciona un rango de fechas válido.</p>}
    {dirty&&<p className="text-xs text-amber-700">Hay cambios pendientes. Usa Aplicar filtros para actualizar el reporte.</p>}
    <p className="text-xs text-muted-foreground">Puedes elegir varios valores en cada filtro. Dentro del mismo filtro se acepta cualquiera de los seleccionados; los distintos filtros se cumplen a la vez. Las selecciones se conservan en el detalle.</p>
    {supplier&&<p className="rounded-lg bg-amber-50 p-3 text-xs text-amber-950">Clientes, vendedores, usuarios, meseros y mesas filtran solo las ventas. Las compras conservan el alcance de fechas, categorías, proveedores, productos y bodegas; la relación venta/compra usa esos dos alcances.</p>}
    {(draft.areaIds?.length||draft.lineIds?.length||draft.groupIds?.length||draft.subgroupIds?.length)?<p className="text-xs text-muted-foreground">La jerarquía usa la clasificación histórica guardada al emitir. Los movimientos antiguos sin esa clasificación no coinciden con filtros jerárquicos.</p>:null}
  </CardContent></Card>;
}

function FilterField({label,children}:{label:string;children:ReactNode}){return <div className="space-y-1.5"><span className="text-xs font-semibold text-muted-foreground">{label}</span>{children}</div>;}
