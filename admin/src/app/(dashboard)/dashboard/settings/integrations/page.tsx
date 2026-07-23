"use client";

import { useEffect, useState } from "react";
import type { ReactNode } from "react";
import Link from "next/link";
import { ArrowLeft, Building2, CalendarDays, CreditCard, PackageSearch, Save } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import {
  useIntegrationSettings,
  useMantisWarehouses,
  useRefreshMantisProduct,
  useUpdateMantisWarehouses,
  useUpdateMantisIntegration,
  useUpdateXionIntegration,
  useUpdateGoogleCalendarIntegration,
  useUpdateWompiIntegration,
} from "@/hooks/use-integrations";
import { useBusinessContextStore } from "@/stores/business-context-store";

export default function IntegrationsSettingsPage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  const { data, isLoading, isError, refetch } = useIntegrationSettings();
  const updateGoogle = useUpdateGoogleCalendarIntegration();
  const updateWompi = useUpdateWompiIntegration();
  const updateMantis = useUpdateMantisIntegration();
  const updateXion = useUpdateXionIntegration();
  const refreshMantis = useRefreshMantisProduct();
  const hasMantisIntegration = data?.mantis?.isConfigured === true;
  const mantisWarehousesQuery = useMantisWarehouses(hasMantisIntegration);
  const updateMantisWarehouses = useUpdateMantisWarehouses();
  const [mantisWarehouses, setMantisWarehouses] = useState<
    NonNullable<typeof mantisWarehousesQuery.data>
  >([]);
  const [mantisQuery, setMantisQuery] = useState("");
  const [mantisResult, setMantisResult] = useState<{
    productsFound: number; productsChanged: number;
  } | null>(null);

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
    mode: "test" as "test" | "production",
    sandboxBaseUrl: "https://sandbox.wompi.co/v1",
    productionBaseUrl: "https://production.wompi.co/v1",
    requestTimeoutSeconds: 30,
    checkoutBaseUrl: "https://checkout.wompi.co/l/",
    privateKey: "",
    publicKey: "",
    eventsSecret: "",
    integritySecret: "",
  });

  const [mantis, setMantis] = useState({
    isEnabled: false,
    baseUrl: "",
    requestTimeoutSeconds: 30,
    currency: "COP",
    authorizationToken: "",
  });
  const [xion, setXion] = useState({
    isEnabled: false,
    baseUrl: "http://api.andinasantander.com:9091/",
    requestTimeoutSeconds: 120,
    currency: "COP",
    sucursalId: 1,
    vendedorId: 1,
    equipoId: 1,
    bodegaId: 1,
    empresaId: 1,
    centroDeCostoId: 1,
    usuarioId: 1,
    rutaId: 0,
    validateStockOnCreate: true,
    orderHistoryDays: 365,
  });
  useEffect(() => {
    if (mantisWarehousesQuery.data) setMantisWarehouses(mantisWarehousesQuery.data);
  }, [mantisWarehousesQuery.data]);

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
      mode: data.wompi.mode,
      sandboxBaseUrl: data.wompi.sandboxBaseUrl,
      productionBaseUrl: data.wompi.productionBaseUrl,
      requestTimeoutSeconds: data.wompi.requestTimeoutSeconds,
      checkoutBaseUrl: data.wompi.checkoutBaseUrl,
      privateKey: "",
      publicKey: "",
      eventsSecret: "",
      integritySecret: "",
    }));
    setMantis({
      isEnabled: data.mantis.isEnabled,
      baseUrl: data.mantis.baseUrl,
      requestTimeoutSeconds: data.mantis.requestTimeoutSeconds,
      currency: data.mantis.currency,
      authorizationToken: "",
    });
    setXion({
      isEnabled: data.xion.isEnabled,
      baseUrl: data.xion.baseUrl,
      requestTimeoutSeconds: data.xion.requestTimeoutSeconds,
      currency: data.xion.currency,
      sucursalId: data.xion.sucursalId,
      vendedorId: data.xion.vendedorId,
      equipoId: data.xion.equipoId,
      bodegaId: data.xion.bodegaId,
      empresaId: data.xion.empresaId,
      centroDeCostoId: data.xion.centroDeCostoId,
      usuarioId: data.xion.usuarioId,
      rutaId: data.xion.rutaId,
      validateStockOnCreate: data.xion.validateStockOnCreate,
      orderHistoryDays: data.xion.orderHistoryDays,
    });  }, [data]);

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
            <Field label="Modo">
              <Select
                value={wompi.mode}
                onValueChange={(mode) =>
                  setWompi({ ...wompi, mode: mode as "test" | "production" })
                }
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="test">Pruebas</SelectItem>
                  <SelectItem value="production">Producción</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field label="Pruebas API">
              <Input
                value={wompi.sandboxBaseUrl}
                onChange={(event) => setWompi({ ...wompi, sandboxBaseUrl: event.target.value })}
              />
            </Field>
            <Field label="Producción API">
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
                  mode: wompi.mode,
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

        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Building2 className="h-4 w-4" />
                  Mantis
                </CardTitle>
                <CardDescription>
                  Conexión de catálogo y pedidos. El host se administra aquí.
                </CardDescription>
              </div>
              <StatusBadge active={data.mantis.isEnabled} />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <ToggleRow
              label="Activo"
              checked={mantis.isEnabled}
              onCheckedChange={(isEnabled) => setMantis({ ...mantis, isEnabled })}
            />
            <Field label="Host de la API">
              <Input
                value={mantis.baseUrl}
                placeholder="http://servidor:puerto/ruta/"
                onChange={(event) => setMantis({ ...mantis, baseUrl: event.target.value })}
              />
            </Field>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Timeout (segundos)">
                <Input type="number" min={1} value={mantis.requestTimeoutSeconds}
                  onChange={(event) => setMantis({ ...mantis, requestTimeoutSeconds: Number(event.target.value) })} />
              </Field>
              <Field label="Moneda">
                <Input value={mantis.currency}
                  onChange={(event) => setMantis({ ...mantis, currency: event.target.value })} />
              </Field>
            </div>
            <SecretField
              label="Token de autorización"
              configured={data.mantis.hasAuthorizationToken}
              value={mantis.authorizationToken}
              onChange={(authorizationToken) => setMantis({ ...mantis, authorizationToken })}
            />
            {data.mantis.lastError && (
              <p className="text-sm text-destructive">{data.mantis.lastError}</p>
            )}
            <Button
              onClick={() => updateMantis.mutate({
                ...mantis,
                authorizationToken: mantis.authorizationToken || null,
              })}
              disabled={updateMantis.isPending || !mantis.baseUrl.trim()}
            >
              <Save className="mr-2 h-4 w-4" />
              Guardar Mantis
            </Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <PackageSearch className="h-4 w-4" />
                  Xion · Andina Santander
                </CardTitle>
                <CardDescription>
                  Parámetros operativos de DISTRIBUCIONES ANDINA SANTANDER. No se exponen en la configuración del agente.
                </CardDescription>
              </div>
              <StatusBadge active={data.xion.isEnabled} />
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <ToggleRow
              label="Activo"
              checked={xion.isEnabled}
              onCheckedChange={(isEnabled) => setXion({ ...xion, isEnabled })}
            />
            <Field label="Host de la API">
              <Input value={xion.baseUrl}
                onChange={(event) => setXion({ ...xion, baseUrl: event.target.value })} />
            </Field>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Timeout (segundos)">
                <Input type="number" min={1} value={xion.requestTimeoutSeconds}
                  onChange={(event) => setXion({ ...xion, requestTimeoutSeconds: Number(event.target.value) })} />
              </Field>
              <Field label="Moneda">
                <Input value={xion.currency}
                  onChange={(event) => setXion({ ...xion, currency: event.target.value })} />
              </Field>
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Sucursal · DISTRIBUCIONES ANDINA SANTANDER">
                <Input type="number" min={1} value={xion.sucursalId}
                  onChange={(event) => setXion({ ...xion, sucursalId: Number(event.target.value) })} />
              </Field>
              <Field label="Vendedor · VENTAS">
                <Input type="number" min={1} value={xion.vendedorId}
                  onChange={(event) => setXion({ ...xion, vendedorId: Number(event.target.value) })} />
              </Field>
              <Field label="Equipo · EQUIPO01">
                <Input type="number" min={1} value={xion.equipoId}
                  onChange={(event) => setXion({ ...xion, equipoId: Number(event.target.value) })} />
              </Field>
              <Field label="Bodega · BODEGA VENTAS">
                <Input type="number" min={1} value={xion.bodegaId}
                  onChange={(event) => setXion({ ...xion, bodegaId: Number(event.target.value) })} />
              </Field>
              <Field label="Empresa">
                <Input type="number" min={1} value={xion.empresaId}
                  onChange={(event) => setXion({ ...xion, empresaId: Number(event.target.value) })} />
              </Field>
              <Field label="Centro de costo">
                <Input type="number" min={1} value={xion.centroDeCostoId}
                  onChange={(event) => setXion({ ...xion, centroDeCostoId: Number(event.target.value) })} />
              </Field>
              <Field label="Usuario">
                <Input type="number" min={1} value={xion.usuarioId}
                  onChange={(event) => setXion({ ...xion, usuarioId: Number(event.target.value) })} />
              </Field>
              <Field label="Ruta (0 = todas)">
                <Input type="number" min={0} value={xion.rutaId}
                  onChange={(event) => setXion({ ...xion, rutaId: Number(event.target.value) })} />
              </Field>
            </div>
            <ToggleRow
              label="Validar existencia al crear el pedido"
              checked={xion.validateStockOnCreate}
              onCheckedChange={(validateStockOnCreate) => setXion({ ...xion, validateStockOnCreate })}
            />
            <Field label="Días de historial de pedidos">
              <Input type="number" min={1} max={3650} value={xion.orderHistoryDays}
                onChange={(event) => setXion({ ...xion, orderHistoryDays: Number(event.target.value) })} />
            </Field>
            {data.xion.lastError && (
              <p className="text-sm text-destructive">{data.xion.lastError}</p>
            )}
            <Button
              onClick={() => updateXion.mutate(xion)}
              disabled={updateXion.isPending || !xion.baseUrl.trim()}
            >
              <Save className="mr-2 h-4 w-4" />
              Guardar Xion
            </Button>
          </CardContent>
        </Card>
        {hasMantisIntegration && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Building2 className="h-4 w-4" />
              Bodega Mantis por número
            </CardTitle>
            <CardDescription>
              Cada número receptor usa únicamente su bodega. Sin esta configuración el carrito se bloquea para evitar sumar inventario de otras bodegas.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div>
              <h3 className="text-sm font-semibold">Canales y bodegas</h3>
              <p className="text-xs text-muted-foreground">Configura una bodega por cada número receptor.</p>
            </div>
            {mantisWarehouses.map((channel, index) => (
              <div className="space-y-3 rounded-md border p-3" key={channel.businessWhatsAppNumberId}>
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <p className="text-sm font-medium">{channel.phoneNumber}</p>
                    <p className="text-xs text-muted-foreground">{channel.whatsAppPhoneNumberId}</p>
                  </div>
                  <Switch
                    checked={channel.isActive}
                    onCheckedChange={(isActive) => setMantisWarehouses((current) =>
                      current.map((item, itemIndex) => itemIndex === index ? { ...item, isActive } : item)
                    )}
                  />
                </div>
                <div className="grid gap-3 sm:grid-cols-2">
                  <Field label="Código de bodega">
                    <Input value={channel.warehouseCode ?? ""} placeholder="Ej. 2"
                      onChange={(event) => setMantisWarehouses((current) =>
                        current.map((item, itemIndex) => itemIndex === index
                          ? { ...item, warehouseCode: event.target.value } : item))} />
                  </Field>
                  <Field label="Nombre de bodega">
                    <Input value={channel.warehouseName ?? ""} placeholder="Ej. SAN MARTIN"
                      onChange={(event) => setMantisWarehouses((current) =>
                        current.map((item, itemIndex) => itemIndex === index
                          ? { ...item, warehouseName: event.target.value } : item))} />
                  </Field>
                </div>
              </div>
            ))}
            {!mantisWarehousesQuery.isLoading && mantisWarehouses.length === 0 && (
              <p className="text-sm text-muted-foreground">No hay números de WhatsApp activos.</p>
            )}
            <Button onClick={() => updateMantisWarehouses.mutate(mantisWarehouses)}
              disabled={updateMantisWarehouses.isPending || mantisWarehouses.length === 0
                || mantisWarehouses.some((channel) => channel.isActive && !channel.warehouseCode?.trim())}>
              <Save className="mr-2 h-4 w-4" />
              Guardar bodegas
            </Button>
            <div className="space-y-4 border-t pt-4">
              <div>
                <h3 className="flex items-center gap-2 text-sm font-semibold">
                  <PackageSearch className="h-4 w-4" />
                  Catálogo
                </h3>
                <p className="text-xs text-muted-foreground">
              Busca un nombre o código en Mantis y actualiza solamente su identidad local.
              El precio y la existencia siempre se consultan en vivo al vender.
                </p>
              </div>
            <Field label="Nombre o código del producto">
              <Input
                value={mantisQuery}
                placeholder="Ej. jamonada Cunni Chef"
                onChange={(event) => {
                  setMantisQuery(event.target.value);
                  setMantisResult(null);
                }}
                onKeyDown={async (event) => {
                  if (event.key !== "Enter" || !mantisQuery.trim()) return;
                  setMantisResult(await refreshMantis.mutateAsync(mantisQuery.trim()));
                }}
              />
            </Field>
            {mantisResult && (
              <p className="text-sm text-muted-foreground">
                Encontrados: {mantisResult.productsFound}. Identidades actualizadas:{" "}
                {mantisResult.productsChanged}.
              </p>
            )}
            {refreshMantis.isError && (
              <p className="text-sm text-destructive">
                No fue posible actualizar el producto. Verifica la conexión Mantis.
              </p>
            )}
            <Button
              onClick={async () =>
                setMantisResult(await refreshMantis.mutateAsync(mantisQuery.trim()))
              }
              disabled={refreshMantis.isPending || !mantisQuery.trim()}
            >
              <PackageSearch className="mr-2 h-4 w-4" />
              Buscar y actualizar producto
            </Button>
            </div>
          </CardContent>
        </Card>
        )}
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
