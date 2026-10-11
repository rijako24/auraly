import { apiClient } from "./client";

export type RestaurantRoom = {
  roomId: string;
  businessId: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
  rowVersion: string;
};

export type RestaurantFloorPlanObject = {
  objectId: string;
  kind: "Table" | "Chair" | "Bench" | "Wall" | "Door" | "Counter" | "Plant" | "Label" |
    "Window" | "Column" | "Zone" | "Kitchen" | "Bathroom" | "Entrance";
  tableId: string | null;
  x: number; y: number; width: number; height: number; rotation: number;
  label: string | null;
  shape?: "Round" | "Oval" | "Square" | "Rectangle" | "Rounded" | null;
  color?: string | null;
  chairs?: number | null;
};

export type RestaurantFloorPlan = {
  roomId: string; roomName: string; rowVersion: string;
  hasCustomLayout: boolean;
  hasUnpublishedChanges: boolean;
  objects: RestaurantFloorPlanObject[];
  tables: RestaurantTable[];
};

export type RestaurantTable = {
  tableId: string;
  businessId: string;
  roomId: string | null;
  code: string;
  name: string | null;
  capacity: number | null;
  isActive: boolean;
  rowVersion: string;
};

export type RestaurantStation = {
  stationId: string;
  businessId: string;
  code: string;
  name: string;
  sortOrder: number;
  kind: "Preparation" | "Pickup";
  targetMinutes: number | null;
  isActive: boolean;
  rowVersion: string;
};

export type RestaurantCommandConfiguration = {
  configurationId: string; stationId: string; name: string;
  mode: "Virtual" | "Physical" | "Both";
  printerDeviceId: string | null; printerProfileKey: string | null;
  isActive: boolean; rowVersion: string;
};

export type RestaurantPrintDevice = { deviceId: string; name: string };

export type RestaurantProductRoute = {
  businessId: string;
  productId: string;
  stationId: string;
  shortName: string | null;
  rowVersion: string;
  productName: string | null;
  productCode: string | null;
};

export type RestaurantMasterPage<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

export type CreateRestaurantRoomRequest = {
  roomId: string;
  businessId: string;
  name: string;
  sortOrder: number;
};

export type CreateRestaurantTableRequest = {
  tableId: string;
  businessId: string;
  roomId: string | null;
  code: string;
  name: string | null;
  capacity: number | null;
};

export const restaurantMasterApi = {
  rooms: (page = 1) => apiClient.get<RestaurantMasterPage<RestaurantRoom>>(
    "/commerce/v1/restaurant/rooms", { page, pageSize: 100 }, { cache: "no-store" }),
  floorPlan: (roomId: string, draft = false) => apiClient.get<RestaurantFloorPlan>(
    `/commerce/v1/restaurant/rooms/${roomId}/floorplan`,
    draft ? { draft: true } : undefined, { cache: "no-store" }),
  saveFloorPlan: (roomId: string, request: {
    rowVersion: string; objects: RestaurantFloorPlanObject[];
  }) => apiClient.put<RestaurantFloorPlan>(
    `/commerce/v1/restaurant/rooms/${roomId}/floorplan`, request),
  publishFloorPlan: (roomId: string, rowVersion: string) =>
    apiClient.post<RestaurantFloorPlan>(
      `/commerce/v1/restaurant/rooms/${roomId}/floorplan/publish`,
      { rowVersion }),
  tables: (page = 1) => apiClient.get<RestaurantMasterPage<RestaurantTable>>(
    "/commerce/v1/restaurant/tables", { page, pageSize: 100 }, { cache: "no-store" }),
  stations: (page = 1) => apiClient.get<RestaurantMasterPage<RestaurantStation>>(
    "/commerce/v1/restaurant/stations", { page, pageSize: 100 }, { cache: "no-store" }),
  printDevices: () => apiClient.get<RestaurantPrintDevice[]>(
    "/commerce/v1/restaurant/print-devices", undefined, { cache: "no-store" }),
  commandConfiguration: (stationId: string) => apiClient.get<RestaurantCommandConfiguration>(
    `/commerce/v1/restaurant/stations/${stationId}/command-configuration`, undefined, { cache: "no-store" }),
  updateCommandConfiguration: (stationId: string, request: Omit<RestaurantCommandConfiguration,
    "configurationId" | "stationId" | "isActive">) => apiClient.put<RestaurantCommandConfiguration>(
    `/commerce/v1/restaurant/stations/${stationId}/command-configuration`, request),
  productRoutes: (page = 1) => apiClient.get<RestaurantMasterPage<RestaurantProductRoute>>(
    "/commerce/v1/restaurant/product-routes", { page, pageSize: 25 }, { cache: "no-store" }),
  productRoute: async (productId: string) => (await apiClient.get<RestaurantProductRoute | null>(
    `/commerce/v1/restaurant/product-routes/${productId}`, undefined,
    { cache: "no-store" })) ?? null,
  createRoom: (request: CreateRestaurantRoomRequest) =>
    apiClient.post<RestaurantRoom>("/commerce/v1/restaurant/rooms", request),
  createTable: (request: CreateRestaurantTableRequest) =>
    apiClient.post<RestaurantTable>("/commerce/v1/restaurant/tables", request),
  createStation: (request: Omit<RestaurantStation, "isActive" | "rowVersion">) =>
    apiClient.post<RestaurantStation>("/commerce/v1/restaurant/stations", request),
  updateRoom: (roomId: string, request: Omit<RestaurantRoom, "roomId" | "businessId">) =>
    apiClient.put<RestaurantRoom>(`/commerce/v1/restaurant/rooms/${roomId}`, request),
  updateTable: (tableId: string, request: Omit<RestaurantTable, "tableId" | "businessId">) =>
    apiClient.put<RestaurantTable>(`/commerce/v1/restaurant/tables/${tableId}`, request),
  updateStation: (stationId: string, request: Omit<RestaurantStation, "stationId" | "businessId">) =>
    apiClient.put<RestaurantStation>(`/commerce/v1/restaurant/stations/${stationId}`, request),
  setProductRoute: (productId: string, request: {
    businessId: string; stationId: string; shortName: string | null; rowVersion: string | null;
  }) => apiClient.put<RestaurantProductRoute>(
    `/commerce/v1/restaurant/product-routes/${productId}`, request),
  deleteProductRoute: (productId: string, rowVersion: string) =>
    apiClient.delete<RestaurantProductRoute>(
      `/commerce/v1/restaurant/product-routes/${productId}?rowVersion=${encodeURIComponent(rowVersion)}`),
};
