import { apiClient } from "./client";

export type SellerCatalogItem={productId:string;productCode:string;name:string;unitCode:string;unitPrice:number;priceSource:"PriceList"|"PriceChannel"|"Public";quantityOnHand:number;manageStock:boolean};
export type SellerCatalogPage={items:SellerCatalogItem[];hasMore:boolean;nextOffset:number|null};
export type SellerOrderRequest={businessId:string;warehouseId:string;customerId:string;partySiteId:string|null;routeId:string|null;routeStopId:string|null;capturedOffline:boolean;notes:string|null;idempotencyKey:string;lines:Array<{productId:string;quantity:number}>};
export type SellerOrderResult={orderId:string;orderNumber:string;status:"Confirmed"|"InReview";total:number;requiresReview:boolean;warnings:string[]};

export const sellerOrdersApi={
  catalog:(request:{businessId:string;warehouseId:string;customerId:string;search?:string;skip?:number;take?:number})=>apiClient.post<SellerCatalogPage>("/commerce/v1/seller-orders/catalog",request),
  create:(request:SellerOrderRequest)=>apiClient.post<SellerOrderResult>("/commerce/v1/seller-orders",request),
  update:(orderId:string,request:{notes:string|null;idempotencyKey:string;lines:Array<{productId:string;quantity:number}>})=>apiClient.put<SellerOrderResult>(`/commerce/v1/seller-orders/${orderId}`,request),
};
