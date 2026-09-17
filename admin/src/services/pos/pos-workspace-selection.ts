type WorkspaceOption = {
  businessId: string;
  warehouseId: string;
};

export type PosWorkspaceSelection = {
  businessId: string;
  warehouseId: string;
};

export function resolvePosWorkspaceSelection(
  options: WorkspaceOption[],
  currentBusinessId: string,
  currentWarehouseId: string,
  remembered: PosWorkspaceSelection | null = null,
): PosWorkspaceSelection {
  const businessIds = Array.from(
    new Set(options.map((option) => option.businessId)),
  );
  const selectedBusinessId = businessIds.includes(currentBusinessId)
    ? currentBusinessId
    : remembered && businessIds.includes(remembered.businessId)
      ? remembered.businessId
      : "";
  const businessId = selectedBusinessId
    ? selectedBusinessId
    : businessIds.length === 1
      ? businessIds[0]
      : "";
  const warehouses = options.filter(
    (option) => option.businessId === businessId,
  );
  const selectedWarehouseId = warehouses.some(
    (option) => option.warehouseId === currentWarehouseId,
  )
    ? currentWarehouseId
    : remembered?.businessId === businessId && warehouses.some(
      (option) => option.warehouseId === remembered.warehouseId,
    )
      ? remembered.warehouseId
      : "";
  const warehouseId = selectedWarehouseId
    ? selectedWarehouseId
    : warehouses.length === 1
      ? warehouses[0].warehouseId
      : "";

  return { businessId, warehouseId };
}
