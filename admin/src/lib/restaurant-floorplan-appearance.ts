import type { RestaurantFloorPlanObject } from "@/services/api/restaurant-master";

export function floorPlanTextColor(hex: string) {
  const rgb = [1, 3, 5].map((start) => Number.parseInt(hex.slice(start, start + 2), 16));
  return rgb[0] * .299 + rgb[1] * .587 + rgb[2] * .114 > 150 ? "#172033" : "#ffffff";
}

export function floorPlanShapeRadius(shape?: RestaurantFloorPlanObject["shape"]) {
  return shape === "Round" || shape === "Oval" ? "9999px" :
    shape === "Square" || shape === "Rectangle" ? "4px" : "16px";
}
