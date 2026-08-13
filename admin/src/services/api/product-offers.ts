import { apiClient } from "./client";

export interface ProductOffer {
  productOfferId: string;
  productId: string;
  condition: "new" | "used" | "refurbished";
  storageGb?: number | null;
  color?: string | null;
  variantLabel?: string | null;
  unitPrice: number;
  currency: string;
  minimumBatteryHealthPercent?: number | null;
  isAvailable: boolean;
  isActive: boolean;
  priceSourceUrl?: string | null;
  priceObservedAtUtc?: string | null;
}

export type SaveProductOfferRequest = Omit<ProductOffer, "productOfferId" | "productId">;

export interface ProductImage {
  productImageId: string;
  productId: string;
  productOfferId?: string | null;
  mediaUrl: string;
  altText?: string | null;
  displayOrder: number;
  isPrimary: boolean;
  isActive: boolean;
}

export const productOffersApi = {
  list: (businessId: string, productId: string) =>
    apiClient.get<ProductOffer[]>(`/businesses/${businessId}/products/${productId}/offers`),
  create: (businessId: string, productId: string, request: SaveProductOfferRequest) =>
    apiClient.post<ProductOffer>(`/businesses/${businessId}/products/${productId}/offers`, request),
  update: (
    businessId: string,
    productId: string,
    productOfferId: string,
    request: SaveProductOfferRequest
  ) =>
    apiClient.put<ProductOffer>(
      `/businesses/${businessId}/products/${productId}/offers/${productOfferId}`,
      request
    ),
  images: (businessId: string, productId: string) =>
    apiClient.get<ProductImage[]>(`/businesses/${businessId}/products/${productId}/images`),
  addImageUrl: (
    businessId: string,
    productId: string,
    request: {
      productOfferId?: string | null;
      mediaUrl: string;
      altText?: string | null;
      displayOrder: number;
      isPrimary: boolean;
    }
  ) =>
    apiClient.post<ProductImage>(
      `/businesses/${businessId}/products/${productId}/images/url`,
      request
    ),
  uploadImage: async (
    businessId: string,
    productId: string,
    file: File,
    productOfferId?: string | null,
    isPrimary = false
  ): Promise<ProductImage> => {
    const body = new FormData();
    body.append("file", file);
    if (productOfferId) body.append("productOfferId", productOfferId);
    body.append("isPrimary", String(isPrimary));
    return apiClient.postForm<ProductImage>(
      `/businesses/${businessId}/products/${productId}/images/upload`, body);
  },
  setPrimaryImage: (businessId: string, productId: string, productImageId: string) =>
    apiClient.put<ProductImage>(
      `/businesses/${businessId}/products/${productId}/images/${productImageId}/primary`,
      {}
    ),

  deleteImage: (businessId: string, productId: string, productImageId: string) =>
    apiClient.delete(`/businesses/${businessId}/products/${productId}/images/${productImageId}`),
};
