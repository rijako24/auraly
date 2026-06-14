"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";

import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { User, Lock, Eye, EyeOff, AlertCircle } from "lucide-react";
import { authApi } from "@/services/api/auth";
import { useAuthStore } from "@/stores/auth-store";
import type { ApiError } from "@/types/api";

function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const setAuth = useAuthStore((s) => s.setAuth);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError(null);
    setIsLoading(true);

    try {
      const response = await authApi.login({ username, password });
      setAuth(response.user);
      const redirect = searchParams.get("redirect") ?? "/dashboard";
      router.push(redirect.startsWith("/") ? redirect : "/dashboard");
    } catch (err) {
      const apiError = err as ApiError;
      setError(
        apiError?.message || "Error al iniciar sesión. Verifica tus credenciales."
      );
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <Card className="border-0 bg-transparent text-[#151515] shadow-none">
      <CardHeader className="px-0">
        <CardTitle className="text-2xl text-[#151515]">Bienvenido de vuelta</CardTitle>
        <CardDescription className="text-[#667085]">
          Ingresa tus credenciales para acceder
        </CardDescription>
      </CardHeader>
      <CardContent className="px-0">
        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="flex items-center gap-2 rounded-md bg-destructive/10 p-3 text-sm text-destructive">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          <div className="space-y-2">
            <Label htmlFor="username" className="text-[#151515]">Usuario</Label>
            <div className="relative">
              <User className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[#667085]" />
              <Input
                id="username"
                type="text"
                placeholder="tu_usuario"
                className="border-[#101828] bg-white pl-10 text-[#151515] placeholder:text-[#667085] [--autofill-background:#fff] [--autofill-foreground:#151515]"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                required
                autoComplete="username"
                disabled={isLoading}
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="password">Contraseña</Label>
            <div className="relative">
              <Lock className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[#667085]" />
              <Input
                id="password"
                type={showPassword ? "text" : "password"}
                placeholder="••••••••"
                className="border-[#101828] bg-white pl-10 pr-10 text-[#151515] placeholder:text-[#667085] [--autofill-background:#fff] [--autofill-foreground:#151515]"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                autoComplete="current-password"
                disabled={isLoading}
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-[#667085] hover:text-[#151515]"
              >
                {showPassword ? (
                  <EyeOff className="h-4 w-4" />
                ) : (
                  <Eye className="h-4 w-4" />
                )}
              </button>
            </div>
          </div>

          <div className="flex justify-end">
            <Link
              href="/forgot-password"
              className="text-sm text-[#1A5860] hover:underline"
            >
              ¿Olvidaste tu contraseña?
            </Link>
          </div>

          <Button type="submit" className="w-full bg-[#151515] text-white hover:bg-black" disabled={isLoading}>
            {isLoading ? "Iniciando sesión..." : "Iniciar sesión"}
          </Button>
        </form>

        <p className="mt-6 text-center text-sm text-[#667085]">
          ¿No tienes cuenta?{" "}
          <Link
            href="/register"
            className="font-medium text-[#1A5860] hover:underline"
          >
            Regístrate
          </Link>
        </p>
      </CardContent>
    </Card>
  );
}

export default function LoginPage() {
  return (
    <Suspense
      fallback={
        <Card className="border-0 shadow-none bg-transparent">
          <CardHeader>
            <CardTitle className="text-2xl">Bienvenido de vuelta</CardTitle>
            <CardDescription>Ingresa tus credenciales para acceder</CardDescription>
          </CardHeader>
          <CardContent className="px-0">
            <div className="h-64 animate-pulse bg-muted/30 rounded" />
          </CardContent>
        </Card>
      }
    >
      <LoginForm />
    </Suspense>
  );
}
