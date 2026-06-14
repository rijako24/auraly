"use client";

import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import Link from "next/link";
import { ArrowLeft, CalendarDays, CreditCard, Save } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { Switch } from "@/components/ui/switch";
import {
  useIntegrationSettings,
  useUpdateGoogleCalendarIntegration,
  useUpdateWompiIntegration,
} from "@/hooks/use-integrations";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function IntegrationsSettingsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data, isLoading, isError, refetch } = useIntegrationSettings();
  const updateGoogle = useUpdateGoogleCalendarIntegration();
  const updateWompi = useUpdateWompiIntegration();

  const [google, setGoogle] = useState({
    isEnabled: false,
    calendarId: "primary",
    timeZone: "America/Bogota",
    scopes: "",
    clientId: "",
    clientSecret: "",
    refreshToken: "",
  });
  const [wompi, setWompi] = useState({
    isEnabled: false,
    useSandbox: true,
    sandboxBaseUrl: "https://sandbox.wompi.co/v1",
    productionBaseUrl: "https://production.wompi.co/v1",
    requestTimeoutSeconds: 30,
    checkoutBaseUrl: "https://checkout.wompi.co/l/",
    privateKey: "",
    publicKey: "",
    eventsSecret: "",
    integritySecret: "",
  });

  useEffect(() => {
    if (!data) return;
    setGoogle((current) => ({
      ...current,
      isEnabled: data.googleCalendar.isEnabled,
      calendarId: data.googleCalendar.calendarId,
      timeZone: data.googleCalendar.timeZone,
      scopes: data.googleCalendar.scopes ?? "",
      clientId: "",
      clientSecret: "",
      refreshToken: "",
    }));
    setWompi((current) => ({
      ...current,
      isEnabled: data.wompi.isEnabled,
      useSandbox: data.wompi.useSandbox,
      sandboxBaseUrl: data.wompi.sandboxBaseUrl,
      productionBaseUrl: data.wompi.productionBaseUrl,
      requestTimeoutSeconds: data.wompi.requestTimeoutSeconds,
      checkoutBaseUrl: data.wompi.checkoutBaseUrl,
      privateKey: "",
      publicKey: "",
      eventsSecret: "",
      integritySecret: "",
    }));
  }, [data]);

  if (!businessId) {
    return (
      <div className="space-y-6">
        <Header />
        <p className="text-sm text-muted-foreground">
          Selecciona un negocio en el selector superior.
        </p>
      </div>
    );
  }

  if (isLoading) return <PageLoading />;
  if (isError || !data) return <PageError onRetry={() => refetch()} />;

  return (
    <div className="space-y-6">
      <Header />

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <CalendarDays className="h-4 w-4" />
                  Google Calendar
                </CardTitle>
                <CardDescription>
                  Guarda reservas confirmadas en el calendario del negocio.
                </CardDescription>
              </div>
              <StatusBadge active={data.googleCalendar.isEnabled} />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <ToggleRow
              label="Activo"
              checked={google.isEnabled}
              onCheckedChange={(isEnabled) => setGoogle({ ...google, isEnabled })}
            />
            <Field label="Calendar ID">
              <Input
                value={google.calendarId}
                onChange={(event) => setGoogle({ ...google, calendarId: event.target.value })}
              />
            </Field>
            <Field label="Zona horaria">
              <Input
                value={google.timeZone}
                onChange={(event) => setGoogle({ ...google, timeZone: event.target.value })}
              />
            </Field>
            <Field label="Scopes">
              <Input
                value={google.scopes}
                onChange={(event) => setGoogle({ ...google, scopes: event.target.value })}
              />
            </Field>
            <SecretField
              label="Client ID"
              configured={data.googleCalendar.hasClientId}
              value={google.clientId}
              onChange={(clientId) => setGoogle({ ...google, clientId })}
            />
            <SecretField
              label="Client Secret"
              configured={data.googleCalendar.hasClientSecret}
              value={google.clientSecret}
              onChange={(clientSecret) => setGoogle({ ...google, clientSecret })}
            />
            <SecretField
              label="Refresh Token"
              configured={data.googleCalendar.hasRefreshToken}
              value={google.refreshToken}
              onChange={(refreshToken) => setGoogle({ ...google, refreshToken })}
            />
            {data.googleCalendar.lastError && (
              <p className="text-sm text-destructive">{data.googleCalendar.lastError}</p>
            )}
            <Button
              onClick={() =>
                updateGoogle.mutate({
                  isEnabled: google.isEnabled,
                  calendarId: google.calendarId,
                  timeZone: google.timeZone,
                  scopes: google.scopes || null,
                  clientId: google.clientId || null,
                  clientSecret: google.clientSecret || null,
                  refreshToken: google.refreshToken || null,
                })
              }
              disabled={updateGoogle.isPending}
            >
              <Save className="mr-2 h-4 w-4" />
              Guardar Calendar
            </Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <CreditCard className="h-4 w-4" />
                  Wompi
                </CardTitle>
                <CardDescription>
                  Genera links de pago y verifica transacciones.
                </CardDescription>
              </div>
              <StatusBadge active={data.wompi.isEnabled} />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <ToggleRow
              label="Activo"
              checked={wompi.isEnabled}
              onCheckedChange={(isEnabled) => setWompi({ ...wompi, isEnabled })}
            />
            <ToggleRow
              label="Sandbox"
              checked={wompi.useSandbox}
              onCheckedChange={(useSandbox) => setWompi({ ...wompi, useSandbox })}
            />
            <Field label="Sandbox API">
              <Input
                value={wompi.sandboxBaseUrl}
                onChange={(event) => setWompi({ ...wompi, sandboxBaseUrl: event.target.value })}
              />
            </Field>
            <Field label="Produccion API">
              <Input
                value={wompi.productionBaseUrl}
                onChange={(event) => setWompi({ ...wompi, productionBaseUrl: event.target.value })}
              />
            </Field>
            <Field label="Checkout base">
              <Input
                value={wompi.checkoutBaseUrl}
                onChange={(event) => setWompi({ ...wompi, checkoutBaseUrl: event.target.value })}
              />
            </Field>
            <Field label="Timeout">
              <Input
                type="number"
                min={1}
                value={wompi.requestTimeoutSeconds}
                onChange={(event) =>
                  setWompi({
                    ...wompi,
                    requestTimeoutSeconds: Number(event.target.value),
                  })
                }
              />
            </Field>
            <SecretField
              label="Private Key"
              configured={data.wompi.hasPrivateKey}
              value={wompi.privateKey}
              onChange={(privateKey) => setWompi({ ...wompi, privateKey })}
            />
            <SecretField
              label="Public Key"
              configured={data.wompi.hasPublicKey}
              value={wompi.publicKey}
              onChange={(publicKey) => setWompi({ ...wompi, publicKey })}
            />
            <SecretField
              label="Events Secret"
              configured={data.wompi.hasEventsSecret}
              value={wompi.eventsSecret}
              onChange={(eventsSecret) => setWompi({ ...wompi, eventsSecret })}
            />
            <SecretField
              label="Integrity Secret"
              configured={data.wompi.hasIntegritySecret}
              value={wompi.integritySecret}
              onChange={(integritySecret) => setWompi({ ...wompi, integritySecret })}
            />
            {data.wompi.lastError && (
              <p className="text-sm text-destructive">{data.wompi.lastError}</p>
            )}
            <Button
              onClick={() =>
                updateWompi.mutate({
                  isEnabled: wompi.isEnabled,
                  useSandbox: wompi.useSandbox,
                  sandboxBaseUrl: wompi.sandboxBaseUrl,
                  productionBaseUrl: wompi.productionBaseUrl,
                  requestTimeoutSeconds: wompi.requestTimeoutSeconds,
                  checkoutBaseUrl: wompi.checkoutBaseUrl,
                  privateKey: wompi.privateKey || null,
                  publicKey: wompi.publicKey || null,
                  eventsSecret: wompi.eventsSecret || null,
                  integritySecret: wompi.integritySecret || null,
                })
              }
              disabled={updateWompi.isPending}
            >
              <Save className="mr-2 h-4 w-4" />
              Guardar Wompi
            </Button>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function Header() {
  return (
    <div className="flex items-center gap-4">
      <Button variant="ghost" size="icon" asChild>
        <Link href="/dashboard/settings">
          <ArrowLeft className="h-4 w-4" />
        </Link>
      </Button>
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Integraciones</h1>
        <p className="text-muted-foreground">
          Conexiones del negocio disponibles para los agentes
        </p>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="space-y-2">
      <Label>{label}</Label>
      {children}
    </div>
  );
}

function SecretField({
  label,
  configured,
  value,
  onChange,
}: {
  label: string;
  configured: boolean;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <Field label={`${label}${configured ? " configurado" : ""}`}>
      <Input
        type="password"
        value={value}
        placeholder={configured ? "Dejar vacio para conservar" : ""}
        onChange={(event) => onChange(event.target.value)}
      />
    </Field>
  );
}

function ToggleRow({
  label,
  checked,
  onCheckedChange,
}: {
  label: string;
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between gap-4">
      <Label>{label}</Label>
      <Switch checked={checked} onCheckedChange={onCheckedChange} />
    </div>
  );
}

function StatusBadge({ active }: { active: boolean }) {
  return (
    <Badge variant={active ? "default" : "secondary"}>
      {active ? "Activo" : "Inactivo"}
    </Badge>
  );
}
