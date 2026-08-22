import { apiClient } from "./client";

export type PriceChannelStrategy = "TieredProductPrice"|"PercentageOverBasePrice"|"PercentageBelowBasePrice"|"PercentageOverAverageCost"|"FixedMarginOverAverageCost"|"SellAtAverageCost";
export interface PriceSegmentSummary { id:string; code:string; name:string; isActive:boolean; createdAt:string; productCount:number; customerCount:number; strategy:PriceChannelStrategy; value:number|null; }
export interface PriceSegmentItem { productId:string; productCode:string; productName:string; amount:number; currencyCode:string; minimumQuantity:number; validFrom:string; validUntil:string|null; excluded:boolean; }

export const priceSegmentsApi = {
  list: () => apiClient.get<PriceSegmentSummary[]>("/commerce/v1/pricing/segments"),
  create: (data:{name:string;channelStrategy:PriceChannelStrategy;channelValue?:number|null;items?:Array<{productId:string;amount:number;minimumQuantity:number;validFrom:string|null;validUntil:string|null}>}) => apiClient.post<PriceSegmentSummary>("/commerce/v1/pricing/segments",data),
  items: (id:string) => apiClient.get<PriceSegmentItem[]>(`/commerce/v1/pricing/segments/${id}/items`),
  saveItem: (id:string,productId:string,data:{amount:number;minimumQuantity:number;validFrom:string|null;validUntil:string|null;excluded:boolean}) => apiClient.put<void>(`/commerce/v1/pricing/segments/${id}/items/${productId}`,data),
  deleteItem: (id:string,productId:string,minimumQuantity:number) => apiClient.delete<void>(`/commerce/v1/pricing/segments/${id}/items/${productId}?minimumQuantity=${encodeURIComponent(minimumQuantity)}`),
  saveChannelSettings: (id:string,name:string,channelStrategy:PriceChannelStrategy,channelValue:number|null) => apiClient.put<void>(`/commerce/v1/pricing/segments/${id}/settings`,{name,channelStrategy,channelValue}),
};
