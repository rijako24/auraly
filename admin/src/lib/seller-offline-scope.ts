export const dailyRouteSnapshotKey = (
  userId: string,
  businessId: string,
  warehouseId: string,
  date: string,
  routeId: string,
) => `${userId}:${businessId}:${warehouseId}:${date}:${routeId}`;

export const sellerOfflinePreparationKey = (
  userId: string,
  businessId: string,
  warehouseId: string,
) => `${userId}:${businessId}:${warehouseId}`;

export const sellerLocalModeKey = (
  userId: string,
  businessId: string,
  warehouseId: string,
) => `auraly:seller-local-mode:${sellerOfflinePreparationKey(userId, businessId, warehouseId)}`;
