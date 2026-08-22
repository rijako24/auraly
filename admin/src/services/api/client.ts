import type { ApiError } from "@/types/api";
import { buildLoginRedirect } from "@/lib/login-redirect";
import { shouldIncludeExecutionContext } from "@/lib/api-execution-context";

import {
  isInstalledApplicationDisplay,
  retryAuthenticatedRequest,
  SESSION_EXPIRED_EVENT,
} from "@/lib/auth-session";

const API_BASE = "/api";
const SELECTED_TENANT_STORAGE_KEY = "selected_tenant_id";
const SELECTED_BUSINESS_STORAGE_KEY = "selected_business_id";

let activeRefresh: Promise<boolean> | null = null;

let applicationRuntime: Promise<boolean> | null = null;
const REQUEST_TIMEOUT_MS = 45_000;
function getSelectedTenantId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return localStorage.getItem(SELECTED_TENANT_STORAGE_KEY);
  } catch {
    return null;
  }
}

function getSelectedBusinessId(): string | null {
  if (typeof window === "undefined") return null;
  try {
    return localStorage.getItem(SELECTED_BUSINESS_STORAGE_KEY);
  } catch {
    return null;
  }
}

function buildJsonHeaders(includeExecutionContext = true): HeadersInit {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
  };
  if (includeExecutionContext) {
    const tenantId = getSelectedTenantId();
    if (tenantId) headers["X-Tenant-Id"] = tenantId;
    const businessId = getSelectedBusinessId();
    if (businessId) headers["X-Business-Id"] = businessId;
  }
  return headers;
}

function buildExecutionHeaders(includeExecutionContext = true): Record<string, string> {
  const headers = { ...buildJsonHeaders(includeExecutionContext) } as Record<string, string>;
  delete headers["Content-Type"];
  return headers;
}

function fetchWithTimeout(
  input: RequestInfo | URL,
  init: RequestInit,
  timeoutMs = REQUEST_TIMEOUT_MS,
): Promise<Response> {
  return fetch(input, { ...init, signal: AbortSignal.timeout(timeoutMs) });
}

async function isApplicationRuntime(): Promise<boolean> {
  if (typeof window === "undefined") return false;
  const iosNavigator = navigator as Navigator & { standalone?: boolean };
  if (isInstalledApplicationDisplay(
    window.matchMedia?.("(display-mode: standalone)").matches ?? false,
    iosNavigator.standalone === true,
  )) return true;

  applicationRuntime ??= fetchWithTimeout(
    "/api/runtime",
    { method: "GET", credentials: "same-origin" },
    5_000,
  )
    .then(async (response) => response.ok
      ? Boolean((await response.json() as { desktop?: boolean }).desktop)
      : false)
    .catch(() => false);
  return applicationRuntime;
}

async function expireWebSession(): Promise<void> {
  if (typeof window === "undefined" || await isApplicationRuntime()) return;
  try {
    localStorage.removeItem("auth-state");
  } catch {
    // Storage can be unavailable in hardened browsers; navigation still expires the shell.
  }
  window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
  const destination = buildLoginRedirect(
    window.location.pathname,
    window.location.search,
  );
  window.location.replace(destination);
}

async function refreshSession(): Promise<boolean> {
  try {
    const res = await fetchWithTimeout(`${API_BASE}/auth/refresh`, {
      method: "POST",
      credentials: "include",
      headers: buildJsonHeaders(false),
    }, 15_000);
    return res.ok;
  } catch {
    return false;
  }
}

export async function fetchWithSessionRetry(
  url: string,
  options: RequestInit,
): Promise<Response> {
  const send = () => fetchWithTimeout(url, {
    ...options,
    credentials: "include",
  });
  return retryAuthenticatedRequest(
    url,
    send,
    () => {
      activeRefresh ??= refreshSession().finally(() => {
        activeRefresh = null;
      });
      return activeRefresh;
    },
    expireWebSession,
  );
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
        error.message = body.detail || body.message || body.title || "An error occurred";
        error.errors = body.errors;
      } catch {
        error.message = response.statusText;
      }
      throw error;
    }
    if (response.status === 204) return undefined as T;
    return response.json();
  }

  async get<T>(
    path: string,
    params?: Record<string, string | number | boolean | undefined>
  ): Promise<T> {
    const url = this.buildUrl(path, params);
    const response = await fetchWithSessionRetry(url, {
      method: "GET",
      headers: buildJsonHeaders(shouldIncludeExecutionContext(path)),
    });
    return this.handleResponse<T>(response);
  }

  async post<T>(path: string, body?: unknown): Promise<T> {
    const response = await fetchWithSessionRetry(this.buildUrl(path), {
      method: "POST",
      headers: buildJsonHeaders(shouldIncludeExecutionContext(path)),
      body: body ? JSON.stringify(body) : undefined,
    });
    return this.handleResponse<T>(response);
  }

  async postIdempotent<T>(path: string, body: unknown, idempotencyKey: string): Promise<T> {
    const response = await fetchWithSessionRetry(this.buildUrl(path), {
      method: "POST",
      headers: {
        ...buildJsonHeaders(shouldIncludeExecutionContext(path)),
        "Idempotency-Key": idempotencyKey,
      },
      body: JSON.stringify(body),
    });
    return this.handleResponse<T>(response);
  }

  async put<T>(path: string, body?: unknown): Promise<T> {
    const response = await fetchWithSessionRetry(this.buildUrl(path), {
      method: "PUT",
      headers: buildJsonHeaders(shouldIncludeExecutionContext(path)),
      body: body ? JSON.stringify(body) : undefined,
    });
    return this.handleResponse<T>(response);
  }

  async patch<T>(path: string, body?: unknown): Promise<T> {
    const response = await fetchWithSessionRetry(this.buildUrl(path), {
      method: "PATCH",
      headers: buildJsonHeaders(shouldIncludeExecutionContext(path)),
      body: body ? JSON.stringify(body) : undefined,
    });
    return this.handleResponse<T>(response);
  }

  async postForm<T>(path: string, body: FormData): Promise<T> {
    const response = await fetchWithSessionRetry(this.buildUrl(path), {
      method: "POST",
      headers: buildExecutionHeaders(shouldIncludeExecutionContext(path)),
      body,
    });
    return this.handleResponse<T>(response);
  }

  async delete<T = void>(path: string): Promise<T> {
    const response = await fetchWithSessionRetry(this.buildUrl(path), {
      method: "DELETE",
      headers: buildJsonHeaders(shouldIncludeExecutionContext(path)),
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
