"use client";

import {useState} from "react";
import {useQuery} from "@tanstack/react-query";
import {ExpenseForm} from "@/components/expenses/expense-form";
import {Button} from "@/components/ui/button";
import {Dialog,DialogContent,DialogDescription,DialogHeader,DialogTitle} from "@/components/ui/dialog";
import {expensesApi,type ExpenseAcceptance} from "@/services/api/expenses";
import type {DispatchExpense} from "@/services/api/dispatches";
import {useAuthStore} from "@/stores/auth-store";
import {useBusinessContextStore} from "@/stores/business-context-store";

export function DispatchExpenseApprovalDialog({dispatchId,expense,onClose,onSaved}:{dispatchId:string;expense:DispatchExpense;onClose:()=>void;onSaved:(value:ExpenseAcceptance)=>void}){
  const business=useBusinessContextStore(state=>state.selectedBusinessId);
  const allowed=useAuthStore(state=>state.user?.permissions.includes("expenses.read")&&state.user.permissions.includes("expenses.create"));
  const [busy,setBusy]=useState(false);
  const options=useQuery({queryKey:["expense-options",business],queryFn:expensesApi.options,enabled:!!business&&!!allowed,staleTime:5*60*1000,refetchOnWindowFocus:false,refetchOnReconnect:false});
  return <Dialog open onOpenChange={open=>!open&&!busy&&onClose()}><DialogContent showClose={!busy} className="max-h-[94dvh] w-[calc(100%-1rem)] max-w-4xl overflow-y-auto rounded-3xl"><DialogHeader><DialogTitle>Aprobar gasto del despacho</DialogTitle><DialogDescription>Completa el proveedor real y su soporte. La aprobación y el registro del gasto se guardan juntos.</DialogDescription></DialogHeader>
    {!allowed?<p role="alert">Necesitas permisos para consultar y crear gastos, además de liquidar el despacho.</p>:options.isError?<div role="alert">No fue posible cargar la configuración de Gastos. <Button variant="outline" onClick={()=>void options.refetch()}>Reintentar</Button></div>:!options.data||!business?<p role="status">Cargando configuración de Gastos…</p>:<ExpenseForm businessId={business} options={options.data} dispatchReport={{...expense,dispatchId}} onBusyChange={setBusy} onSaved={async accepted=>onSaved(accepted)}/>}
  </DialogContent></Dialog>;
}
