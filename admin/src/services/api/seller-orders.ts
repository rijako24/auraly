import { apiClient } from "./client";

export type SellerCatalogItem={productId:string;productCode:string;reference?:string|null;name:string;unitCode:string;unitPrice:number;priceSource:string;quantityOnHand:number;manageStock:boolean;documentUnitCost:number};
export type SellerCatalogPage={items:SellerCatalogItem[];hasMore:boolean;nextOffset:number|null};
export type SellerOrderLineRequest={productId:string;description:string;quantity:number;unitPrice:number;discountAmount:number;priceSource:string;documentUnitCost?:number|null};
export type SellerOrderRequest={businessId:string;warehouseId:string;customerId:string;partySiteId:string;routeId:string|null;routeStopId:string|null;capturedOffline:boolean;notes:string|null;idempotencyKey:string;lines:SellerOrderLineRequest[]};
export type SellerOrderResult={orderId:string;orderNumber:string;status:"Confirmed"|"InReview";total:number;requiresReview:boolean;warnings:string[]};

export const sellerOrdersApi={
  catalog:(request:{businessId:string;warehouseId:string;customerId:string;search?:string;skip?:number;take?:number})=>apiClient.post<SellerCatalogPage>("/commerce/v1/seller-orders/catalog",request),
  create:(request:SellerOrderRequest)=>apiClient.post<SellerOrderResult>("/commerce/v1/seller-orders",request),
  update:(orderId:string,request:{customerId:string;partySiteId:string;notes:string|null;idempotencyKey:string;lines:SellerOrderLineRequest[];workSessionId?:string|null})=>apiClient.put<SellerOrderResult>(`/commerce/v1/seller-orders/${orderId}`,request),
};
