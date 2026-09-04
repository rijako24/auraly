import type { ApiError } from "@/types/api";
import { buildLoginRedirect } from "@/lib/login-redirect";
import { shouldIncludeExecutionContext } from "@/lib/api-execution-context";

import {
  announceSessionReplacement,
  announceSessionClosure,
  clearPreviousWebIdentityContext,
  isActiveLocalPosSession,
  isCurrentWebSessionVersion,
  retryAuthenticatedRequest,
  type SessionRefreshResult,
} from "@/lib/auth-session";

const API_BASE = "/api";
const SELECTED_TENANT_STORAGE_KEY = "selected_tenant_id";
const SELECTED_BUSINESS_STORAGE_KEY = "selected_business_id";
const WEB_SESSION_VERSION_STORAGE_KEY = "auraly.auth.session-version";

let fallbackWebSessionVersion = "initial";
let activeRefresh: {
  version: string;
  promise: Promise<SessionRefreshResult>;
} | null = null;

const REQUEST_TIMEOUT_MS = 45_000;

class ApiClientError extends Error implements ApiError {
  statusCode: number;
  errors?: Record<string, string[]>;

  constructor(message: string, statusCode: number, errors?: Record<string, string[]>) {
    super(message);
    this.name = "ApiClientError";
    this.statusCode = statusCode;
    this.errors = errors;
  }
}
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

async function expireWebSession(reason: "replaced" | "expired"): Promise<void> {
  if (typeof window === "undefined") return;
  clearPreviousWebIdentityContext();
  if (isActiveLocalPosSession(
    window.location.pathname,
    window.sessionStorage.getItem("auraly.pos.edge-token"),
    window.localStorage.getItem("auraly.pos.user-session"),
  )) return;
  const destination = buildLoginRedirect(
    window.location.pathname,
    window.location.search,
  );
  if (reason === "replaced") announceSessionReplacement(destination);
  else announceSessionClosure(
    destination,
    "Sesión finalizada",
    "Esta sesión expiró o dejó de estar disponible. Inicia sesión nuevamente para continuar.",
  );
}

async function refreshSession(): Promise<SessionRefreshResult> {
  try {
    const res = await fetchWithTimeout(`${API_BASE}/auth/refresh`, {
      method: "POST",
      credentials: "include",
      headers: buildJsonHeaders(false),
    }, 15_000);
    if (res.ok) return "refreshed";
    if (res.status === 409) {
      const problem = await res.json().catch(() => null) as { code?: string; title?: string } | null;
      if (problem?.code === "AuthenticationSessionReplaced" ||
          problem?.title === "AuthenticationSessionReplaced") return "replaced";
    }
    return res.status === 401 || res.status === 403 ? "expired" : "unavailable";
  } catch {
    return "unavailable";
  }
}

/**
 * Marks a successful login as a new browser-session boundary. Requests that
 * started under the replaced session must never expire the newly issued one.
 */
export function establishWebSession(): void {
  fallbackWebSessionVersion = typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random()}`;
  if (typeof window !== "undefined") {
    try {
      window.localStorage.setItem(
        WEB_SESSION_VERSION_STORAGE_KEY,
        fallbackWebSessionVersion,
      );
    } catch {
      // Hardened browsers can disable storage; the current tab remains safe.
    }
  }
  activeRefresh = null;
}

function currentWebSessionVersion(): string {
  if (typeof window !== "undefined") {
    try {
      return window.localStorage.getItem(WEB_SESSION_VERSION_STORAGE_KEY)
        ?? fallbackWebSessionVersion;
    } catch {
      // Use the in-memory boundary when browser storage is unavailable.
    }
  }
  return fallbackWebSessionVersion;
}

export async function fetchWithSessionRetry(
  url: string,
  options: RequestInit,
): Promise<Response> {
  const requestSessionVersion = currentWebSessionVersion();
  const send = () => fetchWithTimeout(url, {
    ...options,
    credentials: "include",
  });
  return retryAuthenticatedRequest(
    url,
    send,
    () => {
      if (!activeRefresh || activeRefresh.version !== requestSessionVersion) {
        const refresh = refreshSession();
        const clearRefresh = () => {
          if (activeRefresh?.promise === refresh) activeRefresh = null;
        };
        activeRefresh = { version: requestSessionVersion, promise: refresh };
        void refresh.then(clearRefresh, clearRefresh);
      }
      return activeRefresh.promise;
    },
    (reason) => isCurrentWebSessionVersion(
      requestSessionVersion,
      currentWebSessionVersion(),
    )
      ? expireWebSession(reason)
      : Promise.resolve(),
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
      let message = "An error occurred";
      let errors: Record<string, string[]> | undefined;
      let actionUrl: string | undefined;
      try {
        const body = await response.json();
        message = body.detail || body.message || body.title || message;
        errors = body.errors;
        actionUrl = body.actionUrl;
      } catch {
        message = response.statusText || message;
      }
      if (response.status === 402 && actionUrl && typeof window !== "undefined"
          && !window.location.pathname.startsWith("/dashboard/subscription")) {
        window.location.assign(actionUrl);
      }
      throw new ApiClientError(message, response.status, errors);
    }
    if (response.status === 204) return undefined as T;
    return response.json();
  }

  async get<T>(
    path: string,
    params?: Record<string, string | number | boolean | undefined>,
    options?: Pick<RequestInit, "cache">,
  ): Promise<T> {
    const url = this.buildUrl(path, params);
    const response = await fetchWithSessionRetry(url, {
      method: "GET",
      cache: options?.cache,
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
