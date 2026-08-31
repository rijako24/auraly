"use client";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { purchaseOrdersApi, type PurchaseOrderStatus } from "@/services/api/purchase-orders";
import { useBusinessContextStore } from "@/stores/business-context-store";

export function usePurchaseOrders(params: { search?: string; status?: PurchaseOrderStatus; page: number; pageSize: number }) {
  const businessId=useBusinessContextStore(s=>s.selectedBusinessId);
  return useQuery({queryKey:["purchase-orders",businessId,params],queryFn:()=>purchaseOrdersApi.list(params),enabled:!!businessId,placeholderData:keepPreviousData});
}
export function useSavePurchaseOrder(){const client=useQueryClient();const businessId=useBusinessContextStore(s=>s.selectedBusinessId);return useMutation({mutationFn:purchaseOrdersApi.saveDraft,onSuccess:()=>client.invalidateQueries({queryKey:["purchase-orders",businessId]})});}
export function useConfirmPurchaseOrder(){const client=useQueryClient();const businessId=useBusinessContextStore(s=>s.selectedBusinessId);return useMutation({mutationFn:purchaseOrdersApi.confirm,onSuccess:()=>client.invalidateQueries({queryKey:["purchase-orders",businessId]})});}
export function useClosePurchaseOrder(){const client=useQueryClient();const businessId=useBusinessContextStore(s=>s.selectedBusinessId);return useMutation({mutationFn:({id,reason,token}:{id:string;reason:string;token:string})=>purchaseOrdersApi.close(id,reason,token),onSuccess:()=>client.invalidateQueries({queryKey:["purchase-orders",businessId]})});}
