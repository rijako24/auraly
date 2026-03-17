import { apiClient } from "./client";
import type {
  AuthBffResponse,
  AuthUser,
  LoginRequest,
  RegisterRequest,
} from "@/types/api";

export const authApi = {
  login: (data: LoginRequest) =>
    apiClient.post<AuthBffResponse>("/auth/login", data),
  register: (data: RegisterRequest) =>
    apiClient.post<AuthBffResponse>("/auth/register", data),
  me: () => apiClient.get<AuthUser>("/auth/me"),
};
