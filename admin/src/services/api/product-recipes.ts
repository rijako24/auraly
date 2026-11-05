import { apiClient } from "./client";

export interface ProductRecipeIngredient {
  ingredientProductId: string;
  productCode: string;
  name: string;
  quantityPerUnit: number;
  baseUnitCode: string;
  unitCostWithTax?: number | null;
}

export interface ProductRecipe {
  productId: string;
  version: number;
  isEnabled: boolean;
  publishedAt: string;
  compositionKind: "Ingredients" | "Combo";
  ingredients: ProductRecipeIngredient[];
}

export interface RecipeIngredientOption {
  productId: string;
  productCode: string;
  name: string;
  baseUnitCode: string;
  unitCostWithTax?: number | null;
}

export const productRecipesApi = {
  async get(productId: string): Promise<ProductRecipe | null> {
    try {
      return await apiClient.get<ProductRecipe>(`/commerce/v1/products/${productId}/recipe`);
    } catch (error) {
      if ((error as { statusCode?: number }).statusCode === 404) return null;
      throw error;
    }
  },
  compositionCost: (components: Array<{ ingredientProductId: string; quantityPerUnit: number }>) =>
    apiClient.post<{ costBasisAmount: number }>("/commerce/v1/products/composition-cost", components),
  ingredientOptions: (search: string, page: number, pageSize: number, compositionKind = "Ingredients") =>
    apiClient.get<{ items: RecipeIngredientOption[]; page: number; pageSize: number; totalCount: number; totalPages: number }>(
      `/commerce/v1/products/recipe-ingredients?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}&compositionKind=${encodeURIComponent(compositionKind)}`,
    ),
  publish: (productId: string, expectedVersion: number, ingredients: Array<{ ingredientProductId: string; quantityPerUnit: number }>, compositionKind = "Ingredients") =>
    apiClient.put<ProductRecipe>(`/commerce/v1/products/${productId}/recipe`, { expectedVersion, ingredients, compositionKind }),
};
