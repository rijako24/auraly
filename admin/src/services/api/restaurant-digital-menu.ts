import { apiClient } from "./client";

export type RestaurantDigitalMenuConfiguration = {
  publicCode: string;
  title: string;
  introduction: string | null;
  isPublished: boolean;
  rowVersion: string;
};

export type RestaurantDigitalMenuView = {
  title: string;
  introduction: string | null;
  categories: { categoryId: string; name: string; productCount: number }[];
};

export type RestaurantDigitalMenuProductPage = {
  items: { productId: string; name: string; description: string | null;
    price: number; currencyCode: string; imageUrl: string | null }[];
  hasMore: boolean;
};

export const restaurantDigitalMenuApi = {
  configuration: async () => (await apiClient.get<RestaurantDigitalMenuConfiguration | null>(
    "/commerce/v1/restaurant/digital-menu", undefined,
    { cache: "no-store" })) ?? null,
  save: (request: { title: string; introduction: string | null;
    isPublished: boolean; rowVersion: string | null }) =>
    apiClient.put<RestaurantDigitalMenuConfiguration>(
      "/commerce/v1/restaurant/digital-menu", request),
  preview: () => apiClient.get<RestaurantDigitalMenuView>(
    "/commerce/v1/restaurant/digital-menu/preview", undefined,
    { cache: "no-store" }),
  previewProducts: (categoryId: string, page: number) =>
    apiClient.get<RestaurantDigitalMenuProductPage>(
      "/commerce/v1/restaurant/digital-menu/preview/products",
      { categoryId, page }, { cache: "no-store" }),
};
