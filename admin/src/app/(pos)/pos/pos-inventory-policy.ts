export function posInventoryPolicyPresentation(allowsNegativeStock: boolean) {
  return allowsNegativeStock
    ? {
        label: "Venta sin límite de existencias",
        setupLabel: "Sin control de existencias",
        detail: "La bodega permite vender aunque la existencia llegue a cero o quede negativa.",
      }
    : {
        label: "Control de inventario",
        setupLabel: "Controlando inventario",
        detail: "La bodega exige existencia disponible cuando hay conexión.",
      };
}
