"use client";

import { Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import {
  AlertCircle,
  ArrowRight,
  Building2,
  CheckCircle2,
  Eye,
  EyeOff,
  Lock,
  ShieldCheck,
  User,
} from "lucide-react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { authApi } from "@/services/api/auth";
import { readEdgeTokenFromLaunch } from "@/services/pos/pos-edge-client";
import { readRememberedTenantKey, rememberTenantKey } from "@/lib/remembered-tenant-key";
import { rememberOfflineLogin, verifyOfflineLogin } from "@/lib/offline-login-store";
import { defaultStartRoute } from "@/lib/default-start-route";
import { useAuthStore } from "@/stores/auth-store";
import type { ApiError } from "@/types/api";

const fieldClassName =
  "h-12 rounded-xl border-[#cddbd9] bg-white pl-11 pr-4 text-[#102a2f] shadow-[0_1px_2px_rgba(7,22,26,.03)] placeholder:text-[#8ba19f] hover:border-[#9ebfbb] focus-visible:border-[#2a7a82] focus-visible:ring-4 focus-visible:ring-[#69d9d0]/15 disabled:bg-[#f1f6f5] disabled:text-[#31555a] disabled:opacity-100 [--autofill-background:#fff] [--autofill-foreground:#102a2f]";

function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const setAuth = useAuthStore((state) => state.setAuth);
  const tenantFromUrl = searchParams.get("tenant")?.trim() ?? "";
  const [tenantKey, setTenantKey] = useState(tenantFromUrl);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [isHydrated, setIsHydrated] = useState(false);
  const [wasTenantRemembered, setWasTenantRemembered] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [offlineAccess, setOfflineAccess] = useState(false);

  useEffect(() => {
    if (!tenantFromUrl) {
      const rememberedTenantKey = readRememberedTenantKey();
      if (rememberedTenantKey) {
        setTenantKey(rememberedTenantKey);
        setWasTenantRemembered(true);
      }
    }
    setIsHydrated(true);
    readEdgeTokenFromLaunch();
  }, [tenantFromUrl]);
  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setIsLoading(true);

    try {
      const response = await authApi.login({ tenantKey: tenantKey.trim(), username, password });
      await rememberOfflineLogin(tenantKey, username, password, response.user).catch(() => undefined);
      rememberTenantKey(response.user.tenantKey || tenantKey);
      setAuth(response.user);
      const redirect = searchParams.get("redirect") ?? "/dashboard";
      router.push(redirect.startsWith("/") ? redirect : "/dashboard");
    } catch (err) {
      const apiError = err as ApiError;
      const mayUseOffline = !navigator.onLine || !apiError?.statusCode || apiError.statusCode >= 500;
      const savedUser = mayUseOffline ? await verifyOfflineLogin(tenantKey, username, password).catch(() => null) : null;
      if (savedUser) {
        rememberTenantKey(savedUser.tenantKey || tenantKey);
        setAuth(savedUser);
        setOfflineAccess(true);
        const redirect = searchParams.get("redirect") ?? defaultStartRoute(savedUser.roles, savedUser.permissions);
        window.location.replace(redirect.startsWith("/") ? redirect : "/dashboard");
        return;
      }
      setError(mayUseOffline
        ? "No hay conexión y este usuario no quedó preparado en este teléfono, o la contraseña no coincide."
        : apiError?.message || "Error al iniciar sesión. Verifica tus credenciales.");
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="rounded-[1.75rem] border border-white/70 bg-white px-5 py-7 text-[#102a2f] shadow-[0_24px_80px_rgba(3,16,19,.28)] sm:px-8 sm:py-9 lg:border-[#dce9e7] lg:shadow-[0_22px_65px_rgba(15,44,51,.11)]">
      <div className="mb-7">
        <div className="mb-5 flex items-center justify-between gap-4">
          <span className="inline-flex items-center gap-2 rounded-full bg-[#e9f8f5] px-3 py-1.5 text-[11px] font-semibold uppercase tracking-[0.12em] text-[#176a65]">
            <ShieldCheck className="h-3.5 w-3.5" />
            Acceso seguro
          </span>
          <span className="text-xs text-[#77918f]">Auraly Cloud</span>
        </div>
        <h2 className="text-3xl font-semibold tracking-[-0.035em] text-[#07161a]">
          Bienvenido de vuelta
        </h2>
        <p className="mt-2 text-sm leading-6 text-[#667f7d]">
          Ingresa a tu espacio de trabajo empresarial.
        </p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-5">
        {error && (
          <div
            role="alert"
            className="flex items-start gap-2.5 rounded-xl border border-[#d86c6c]/25 bg-[#d86c6c]/[0.08] p-3.5 text-sm text-[#a63f3f]"
          >
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
        )}
        {offlineAccess && <p className="rounded-xl bg-amber-50 p-3 text-sm font-medium text-amber-900">Entrando con la sesión preparada en este teléfono…</p>}

        <div className="space-y-2">
          <div className="flex items-center justify-between gap-3">
            <Label htmlFor="tenantKey" className="font-medium text-[#17383c]">
              Empresa
            </Label>
            {tenantFromUrl && (
              <span className="flex items-center gap-1.5 text-xs font-medium text-[#17836f]">
                <CheckCircle2 className="h-3.5 w-3.5" />
                Empresa identificada
              </span>
            )}
          </div>
          <div className="relative">
            <Building2 className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#668c89]" />
            <Input
              id="tenantKey"
              type="text"
              placeholder="@auraly"
              className={fieldClassName}
              value={tenantKey}
              onChange={(event) => { setTenantKey(event.target.value); setWasTenantRemembered(false); }}
              required
              autoCapitalize="none"
              autoComplete="organization"
              spellCheck={false}
              disabled={!isHydrated || isLoading || Boolean(tenantFromUrl)}
              aria-describedby="tenant-key-help"
            />
          </div>
          <p id="tenant-key-help" className="text-xs leading-5 text-[#718986]">
            {tenantFromUrl
              ? "Este enlace ya está conectado con tu empresa."
              : wasTenantRemembered
                ? "Recordamos esta empresa en este dispositivo. Puedes cambiarla si lo necesitas."
                : "Escribe la clave incluida en el enlace de acceso de tu empresa."}
          </p>
        </div>

        <div className="space-y-2">
          <Label htmlFor="username" className="font-medium text-[#17383c]">
            Usuario
          </Label>
          <div className="relative">
            <User className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#668c89]" />
            <Input
              id="username"
              type="text"
              placeholder="tu_usuario"
              className={fieldClassName}
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              required
              autoComplete="username"
              disabled={!isHydrated || isLoading}
            />
          </div>
        </div>

        <div className="space-y-2">
          <Label htmlFor="password" className="font-medium text-[#17383c]">
            Contraseña
          </Label>
          <div className="relative">
            <Lock className="pointer-events-none absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#668c89]" />
            <Input
              id="password"
              type={showPassword ? "text" : "password"}
              placeholder="••••••••"
              className={`${fieldClassName} pr-12`}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
              autoComplete="current-password"
              disabled={!isHydrated || isLoading}
            />
            <button
              type="button"
              onClick={() => setShowPassword((current) => !current)}
              className="absolute right-2.5 top-1/2 flex h-8 w-8 -translate-y-1/2 items-center justify-center rounded-lg text-[#668c89] transition-colors hover:bg-[#e9f4f2] hover:text-[#17383c] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#2a7a82]"
              aria-label={showPassword ? "Ocultar contraseña" : "Mostrar contraseña"}
            >
              {showPassword ? (
                <EyeOff className="h-4 w-4" />
              ) : (
                <Eye className="h-4 w-4" />
              )}
            </button>
          </div>
          <div className="flex justify-end">
            <Link
              href={`/forgot-password${tenantKey ? `?tenant=${encodeURIComponent(tenantKey.trim())}` : ""}`}
              className="text-xs font-semibold text-[#176f6a] underline-offset-4 hover:underline"
            >
              ¿Olvidaste tu contraseña?
            </Link>
          </div>
        </div>

        <Button
          type="submit"
          className="group h-12 w-full rounded-xl bg-gradient-to-r from-[#0f5f5b] via-[#147a73] to-[#23988d] text-sm font-semibold text-white shadow-[0_10px_24px_rgba(20,122,115,.25)] transition-all hover:-translate-y-0.5 hover:from-[#0d5652] hover:to-[#1d897f] hover:shadow-[0_14px_30px_rgba(20,122,115,.3)]"
          disabled={!isHydrated || isLoading}
        >
          {isLoading ? "Iniciando sesión..." : "Iniciar sesión"}
          {!isLoading && (
            <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-0.5" />
          )}
        </Button>
      </form>

      <p className="mt-7 text-center text-sm text-[#718986]">
        ¿Aún no tienes cuenta?{" "}
        <Link
          href="/register"
          className="font-semibold text-[#176f6a] underline-offset-4 hover:underline"
        >
          Crea tu empresa
        </Link>
      </p>
    </div>
  );
}

function LoginSkeleton() {
  return (
    <div className="rounded-[1.75rem] border border-white/70 bg-white p-8 shadow-[0_24px_80px_rgba(3,16,19,.28)] lg:border-[#dce9e7] lg:shadow-[0_22px_65px_rgba(15,44,51,.11)]">
      <div className="h-[420px] animate-pulse rounded-2xl bg-[#eef5f4]" />
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<LoginSkeleton />}>
      <LoginForm />
    </Suspense>
  );
}
