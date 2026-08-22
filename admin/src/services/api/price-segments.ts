import { apiClient } from "./client";

export type PriceSegmentKind = "PriceList" | "PriceChannel";
export interface PriceSegmentSummary { id:string; kind:PriceSegmentKind; code:string; name:string; isActive:boolean; createdAt:string; productCount:number; customerCount:number; priceVariationPercent:number|null; }
export interface PriceSegmentItem { productId:string; productCode:string; productName:string; amount:number; currencyCode:string; minimumQuantity:number; validFrom:string; validUntil:string|null; excluded:boolean; }

export const priceSegmentsApi = {
  list: () => apiClient.get<PriceSegmentSummary[]>("/commerce/v1/pricing/segments"),
  create: (data:{kind:PriceSegmentKind;name:string;priceVariationPercent?:number;items?:Array<{productId:string;amount:number;minimumQuantity:number;validFrom:string|null;validUntil:string|null}>}) => apiClient.post<PriceSegmentSummary>("/commerce/v1/pricing/segments",data),
  items: (kind:PriceSegmentKind,id:string) => apiClient.get<PriceSegmentItem[]>(`/commerce/v1/pricing/segments/${kind}/${id}/items`),
  saveItem: (kind:PriceSegmentKind,id:string,productId:string,data:{amount:number;minimumQuantity:number;validFrom:string|null;validUntil:string|null;excluded:boolean}) => apiClient.put<void>(`/commerce/v1/pricing/segments/${kind}/${id}/items/${productId}`,data),
  deleteItem: (kind:PriceSegmentKind,id:string,productId:string,minimumQuantity:number) => apiClient.delete<void>(`/commerce/v1/pricing/segments/${kind}/${id}/items/${productId}?minimumQuantity=${encodeURIComponent(minimumQuantity)}`),
  saveChannelSettings: (id:string,priceVariationPercent:number) => apiClient.put<void>(`/commerce/v1/pricing/segments/PriceChannel/${id}/settings`,{priceVariationPercent}),
};
