import { apiClient } from "./client";

export type PriceSegmentKind = "PriceList" | "PriceChannel";
export interface PriceSegmentSummary { id:string; kind:PriceSegmentKind; code:string; name:string; isActive:boolean; createdAt:string; productCount:number; customerCount:number; }
export interface PriceSegmentItem { productId:string; productCode:string; productName:string; amount:number; currencyCode:string; minimumQuantity:number; validFrom:string; validUntil:string|null; excluded:boolean; }

export const priceSegmentsApi = {
  list: () => apiClient.get<PriceSegmentSummary[]>("/commerce/v1/pricing/segments"),
  create: (data:{kind:PriceSegmentKind;code:string;name:string}) => apiClient.post<{id:string}>("/commerce/v1/pricing/segments",data),
  items: (kind:PriceSegmentKind,id:string) => apiClient.get<PriceSegmentItem[]>(`/commerce/v1/pricing/segments/${kind}/${id}/items`),
  saveItem: (kind:PriceSegmentKind,id:string,productId:string,data:{amount:number;minimumQuantity:number;validFrom:string|null;validUntil:string|null;excluded:boolean}) => apiClient.put<void>(`/commerce/v1/pricing/segments/${kind}/${id}/items/${productId}`,data),
};
