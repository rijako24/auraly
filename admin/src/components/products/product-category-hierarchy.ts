import type { MasterHierarchyNode } from "@/components/masters/master-hierarchy-explorer";
import type { ProductCategory } from "@/services/api/products";

export function toProductCategoryHierarchyNode(category: ProductCategory): MasterHierarchyNode {
  return {
    id: category.productCategoryId,
    parentId: category.parentProductCategoryId,
    level: category.depth,
    name: category.name,
    active: category.isActive,
  };
}
