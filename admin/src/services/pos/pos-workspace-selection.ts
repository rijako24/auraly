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
): PosWorkspaceSelection {
  const businessIds = Array.from(
    new Set(options.map((option) => option.businessId)),
  );
  const businessId = businessIds.includes(currentBusinessId)
    ? currentBusinessId
    : businessIds.length === 1
      ? businessIds[0]
      : "";
  const warehouses = options.filter(
    (option) => option.businessId === businessId,
  );
  const warehouseId = warehouses.some(
    (option) => option.warehouseId === currentWarehouseId,
  )
    ? currentWarehouseId
    : warehouses.length === 1
      ? warehouses[0].warehouseId
      : "";

  return { businessId, warehouseId };
}
