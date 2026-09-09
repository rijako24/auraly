import assert from "node:assert/strict";
import test from "node:test";
import { toProductCategoryHierarchyNode } from "./product-category-hierarchy";
import type { ProductCategory } from "@/services/api/products";

test("maps zero-based product category depths to the four hierarchy columns", () => {
  const ids = ["area", "line", "group", "subgroup"];
  const categories: ProductCategory[] = ids.map((id, depth) => ({
    productCategoryId: id,
    parentProductCategoryId: depth === 0 ? null : ids[depth - 1],
    name: id,
    displayOrder: depth,
    isActive: true,
    isBrowsable: true,
    depth,
    path: ids.slice(0, depth + 1).join(" > "),
  }));

  const nodes = categories.map(toProductCategoryHierarchyNode);

  assert.deepEqual(nodes.map((node) => node.level), [0, 1, 2, 3]);
  assert.deepEqual(nodes.map((node) => node.parentId), [null, "area", "line", "group"]);
});
