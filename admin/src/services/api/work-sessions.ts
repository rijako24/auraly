import { apiClient } from "./client";

export const workSessionsApi = {
  currentOrOpen: (businessId: string) =>
    apiClient.post<{ workSessionId: string }>("/commerce/v1/work-sessions/current", {
      businessId,
      warehouseId: null,
      deviceId: null,
    }),
};
