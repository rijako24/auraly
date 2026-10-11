import { apiClient } from "./client";

export interface ProductRecipeIngredient {
  ingredientProductId: string;
  productCode: string;
  name: string;
  quantityPerUnit: number;
  baseUnitCode: string;
}

export interface ProductRecipe {
  productId: string;
  version: number;
  isEnabled: boolean;
  publishedAt: string;
  ingredients: ProductRecipeIngredient[];
}

export interface RecipeIngredientOption {
  productId: string;
  productCode: string;
  name: string;
  baseUnitCode: string;
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
  ingredientOptions: (search: string, page: number, pageSize: number) =>
    apiClient.get<{ items: RecipeIngredientOption[]; page: number; pageSize: number; totalCount: number; totalPages: number }>(
      `/commerce/v1/products/recipe-ingredients?search=${encodeURIComponent(search)}&page=${page}&pageSize=${pageSize}`,
    ),
  publish: (productId: string, expectedVersion: number, ingredients: Array<{ ingredientProductId: string; quantityPerUnit: number }>) =>
    apiClient.put<ProductRecipe>(`/commerce/v1/products/${productId}/recipe`, { expectedVersion, ingredients }),
};
