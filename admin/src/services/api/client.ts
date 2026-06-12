import type { ApiError } from "@/types/api";

const API_BASE = "/api";

let isRefreshing = false;
let refreshSubscribers: Array<() => void> = [];

function onTokenRefreshed() {
  refreshSubscribers.forEach((cb) => cb());
  refreshSubscribers = [];
}

function addRefreshSubscriber(cb: () => void) {
  refreshSubscribers.push(cb);
}

class ApiClient {
  private baseUrl: string;

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl;
  }

  private buildUrl(
    path: string,
    params?: Record<string, string | number | boolean | undefined>
  ): string {
    const p = path.startsWith("/") ? path.slice(1) : path;
    const pathname = `${this.baseUrl}/${p}`;
    if (!params) return pathname;
    const search = new URLSearchParams();
    Object.entries(params).forEach(([k, v]) => {
      if (v !== undefined) search.append(k, String(v));
    });
    const q = search.toString();
    return q ? `${pathname}?${q}` : pathname;
  }

  private async handleResponse<T>(response: Response): Promise<T> {
    if (!response.ok) {
      const error: ApiError = {
        message: "An error occurred",
        statusCode: response.status,
      };
      try {
        const body = await response.json();
        error.message = body.message || body.title || "An error occurred";
        error.errors = body.errors;
      } catch {
        error.message = response.statusText;
      }
      throw error;
    }
    if (response.status === 204) return undefined as T;
    return response.json();
  }

  private async refreshSession(): Promise<boolean> {
    try {
      const res = await fetch(`${API_BASE}/auth/refresh`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
      });
      return res.ok;
    } catch {
      return false;
    }
  }

  private async fetchWithRetry(
    url: string,
    options: RequestInit
  ): Promise<Response> {
    const response = await fetch(url, {
      ...options,
      credentials: "include",
    });

    if (response.status !== 401) return response;

    const isAuthPath = url.includes("/auth/login") || url.includes("/auth/refresh") || url.includes("/auth/register");
    if (isAuthPath) return response;

    if (isRefreshing) {
      return new Promise<Response>((resolve) => {
        addRefreshSubscriber(() => {
          resolve(
            fetch(url, {
              ...options,
              credentials: "include",
            })
          );
        });
      });
    }

    isRefreshing = true;
    const refreshed = await this.refreshSession();
    isRefreshing = false;

    if (!refreshed) {
      if (typeof window !== "undefined") {
        try {
          localStorage.removeItem("auth-state");
        } catch {
          /* ignore */
        }
        window.location.href = "/login";
      }
      return new Promise<Response>(() => {});
    }

    onTokenRefreshed();

    return fetch(url, {
      ...options,
      credentials: "include",
    });
  }

  async get<T>(
    path: string,
    params?: Record<string, string | number | boolean | undefined>
  ): Promise<T> {
    const url = this.buildUrl(path, params);
    const response = await this.fetchWithRetry(url, {
      method: "GET",
      headers: { "Content-Type": "application/json" },
    });
    return this.handleResponse<T>(response);
  }

  async post<T>(path: string, body?: unknown): Promise<T> {
    const response = await this.fetchWithRetry(this.buildUrl(path), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    });
    return this.handleResponse<T>(response);
  }

  async put<T>(path: string, body?: unknown): Promise<T> {
    const response = await this.fetchWithRetry(this.buildUrl(path), {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    });
    return this.handleResponse<T>(response);
  }

  async patch<T>(path: string, body?: unknown): Promise<T> {
    const response = await this.fetchWithRetry(this.buildUrl(path), {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: body ? JSON.stringify(body) : undefined,
    });
    return this.handleResponse<T>(response);
  }

  async delete<T = void>(path: string): Promise<T> {
    const response = await this.fetchWithRetry(this.buildUrl(path), {
      method: "DELETE",
      headers: { "Content-Type": "application/json" },
    });
    return this.handleResponse<T>(response);
  }
}

export const apiClient = new ApiClient(API_BASE);

export function withPagedDefaults<T extends Record<string, unknown> | undefined>(
  params?: T
): Record<string, string | number | boolean | undefined> {
  return {
    page: 1,
    pageSize: 20,
    ...(params ?? {}),
  } as Record<string, string | number | boolean | undefined>;
}
