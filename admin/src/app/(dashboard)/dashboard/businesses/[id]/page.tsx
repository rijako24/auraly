"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import {
  ArrowLeft,
  ChevronDown,
  ChevronRight,
  MessageSquare,
  Pencil,
} from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  type BusinessConfiguration,
  type Business,
  type BusinessWhatsAppNumber,
  type BusinessAttachment,
  type BusinessResource,
} from "@/types/entities";
import { BusinessConfigurationKeyLabels } from "@/types/enums";
import { formatDate } from "@/lib/utils";
import { cn, getInitials } from "@/lib/utils";

const MOCK_BUSINESS: Business = {
  businessId: "bus-001",
  tenantId: "t-001",
  name: "Mimos Baby Spa",
  description:
    "Spa especializado en bebés con tratamientos de hidroterapia, masajes y estimulación temprana. Ambiente seguro y acogedor.",
  address: "Calle 123 #45-67, Bogotá D.C.",
  phone: "+57 300 123 4567",
  email: "contacto@mimosbabyspa.com",
  website: "https://mimosbabyspa.com",
  logoUrl: null,
  isActive: true,
  createdAt: "2024-01-15T10:00:00Z",
  updatedAt: null,
};

const MOCK_CONFIGURATIONS: BusinessConfiguration[] = [
  {
    businessConfigurationId: "cfg-1",
    businessId: "bus-001",
    key: 0,
    value: '{"tone": "amigable", "style": "cercano"}',
    description: "Personalidad del bot conversacional",
    isActive: true,
    createdAt: "2024-01-15T10:00:00Z",
    updatedAt: null,
  },
  {
    businessConfigurationId: "cfg-2",
    businessId: "bus-001",
    key: 4,
    value: '{"monday": "9:00-18:00", "tuesday": "9:00-18:00"}',
    description: "Horarios de operación",
    isActive: true,
    createdAt: "2024-01-15T10:00:00Z",
    updatedAt: null,
  },
  {
    businessConfigurationId: "cfg-3",
    businessId: "bus-001",
    key: 3,
    value: '{"provider": "wompi", "enabled": true}',
    description: "Configuración de pagos",
    isActive: true,
    createdAt: "2024-01-15T10:00:00Z",
    updatedAt: null,
  },
];

const MOCK_WHATSAPP: BusinessWhatsAppNumber[] = [
  {
    businessWhatsAppNumberId: "wa-1",
    businessId: "bus-001",
    phoneNumber: "+573001234567",
    whatsAppPhoneNumberId: "123456789",
    whatsAppAccessToken: "***",
    isActive: true,
    createdAt: "2024-01-15T10:00:00Z",
  },
];

const MOCK_ATTACHMENTS: BusinessAttachment[] = [
  {
    businessAttachmentId: "att-1",
    businessId: "bus-001",
    blobPath: "businesses/bus-001/catalogo.pdf",
    mediaType: "application/pdf",
    filename: "catalogo.pdf",
    description: "Catálogo de servicios",
    isActive: true,
    createdAt: "2024-01-15T10:00:00Z",
  },
  {
    businessAttachmentId: "att-2",
    businessId: "bus-001",
    blobPath: "businesses/bus-001/imagen-promo.jpg",
    mediaType: "image/jpeg",
    filename: "imagen-promo.jpg",
    description: "Imagen promocional",
    isActive: true,
    createdAt: "2024-01-15T10:00:00Z",
  },
];

const MOCK_RESOURCES: BusinessResource[] = [
  {
    businessResourceId: "res-1",
    businessId: "bus-001",
    resourceName: "Tina Spa Premium",
    quantity: 2,
    createdAt: "2024-01-15T10:00:00Z",
    updatedAt: null,
  },
  {
    businessResourceId: "res-2",
    businessId: "bus-001",
    resourceName: "Toallas bebé",
    quantity: 50,
    createdAt: "2024-01-15T10:00:00Z",
    updatedAt: null,
  },
];

function JsonViewer({ json }: { json: string }) {
  try {
    const parsed = JSON.parse(json);
    return (
      <pre className="rounded-md bg-muted p-4 text-xs overflow-auto max-h-64">
        {JSON.stringify(parsed, null, 2)}
      </pre>
    );
  } catch {
    return <pre className="rounded-md bg-muted p-4 text-xs">{json}</pre>;
  }
}

export default function BusinessDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const [webhookOpen, setWebhookOpen] = useState(false);

  const business = MOCK_BUSINESS;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/businesses">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <Avatar className="h-14 w-14">
          <AvatarFallback className="text-lg">
            {getInitials(business.name)}
          </AvatarFallback>
        </Avatar>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">
            {business.name}
          </h1>
          <p className="text-muted-foreground">{business.description}</p>
        </div>
        <Badge variant={business.isActive ? "default" : "secondary"}>
          {business.isActive ? "Activo" : "Inactivo"}
        </Badge>
      </div>

      <Tabs defaultValue="info" className="space-y-4">
        <TabsList>
          <TabsTrigger value="info">Información</TabsTrigger>
          <TabsTrigger value="config">Configuración</TabsTrigger>
          <TabsTrigger value="whatsapp">WhatsApp</TabsTrigger>
          <TabsTrigger value="attachments">Adjuntos</TabsTrigger>
          <TabsTrigger value="resources">Recursos</TabsTrigger>
        </TabsList>

        <TabsContent value="info" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Datos del negocio</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-4 sm:grid-cols-2">
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Nombre
                  </p>
                  <p>{business.name}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Descripción
                  </p>
                  <p>{business.description}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Dirección
                  </p>
                  <p>{business.address}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Teléfono
                  </p>
                  <p>{business.phone}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Email
                  </p>
                  <p>{business.email}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Sitio web
                  </p>
                  <p>{business.website || "—"}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Estado
                  </p>
                  <p>{business.isActive ? "Activo" : "Inactivo"}</p>
                </div>
                <div>
                  <p className="text-sm font-medium text-muted-foreground">
                    Creado
                  </p>
                  <p>{formatDate(business.createdAt)}</p>
                </div>
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="config" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Configuración del negocio</CardTitle>
              <p className="text-sm text-muted-foreground">
                Parámetros de configuración del bot y servicios
              </p>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Clave</TableHead>
                    <TableHead>Descripción</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead className="text-right">Acciones</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {MOCK_CONFIGURATIONS.map((cfg) => (
                    <TableRow key={cfg.businessConfigurationId}>
                      <TableCell className="font-medium">
                        {BusinessConfigurationKeyLabels[
                          cfg.key as keyof typeof BusinessConfigurationKeyLabels
                        ] ?? cfg.key}
                      </TableCell>
                      <TableCell className="max-w-xs truncate">
                        {cfg.description ?? "—"}
                      </TableCell>
                      <TableCell>
                        <Badge
                          variant={cfg.isActive ? "default" : "secondary"}
                        >
                          {cfg.isActive ? "Activo" : "Inactivo"}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-right">
                        <Button variant="ghost" size="sm">
                          <Pencil className="mr-2 h-4 w-4" />
                          Editar
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                  {MOCK_CONFIGURATIONS.length === 0 && (
                    <TableRow>
                      <TableCell
                        colSpan={4}
                        className="text-center text-muted-foreground"
                      >
                        Sin configuraciones
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="whatsapp" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Números de WhatsApp</CardTitle>
              <p className="text-sm text-muted-foreground">
                Números conectados para conversaciones
              </p>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Número</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead>Creado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {MOCK_WHATSAPP.map((wa) => (
                    <TableRow key={wa.businessWhatsAppNumberId}>
                      <TableCell className="font-medium">
                        {wa.phoneNumber}
                      </TableCell>
                      <TableCell>
                        <Badge
                          variant={wa.isActive ? "default" : "secondary"}
                        >
                          {wa.isActive ? "Activo" : "Inactivo"}
                        </Badge>
                      </TableCell>
                      <TableCell>{formatDate(wa.createdAt)}</TableCell>
                    </TableRow>
                  ))}
                  {MOCK_WHATSAPP.length === 0 && (
                    <TableRow>
                      <TableCell
                        colSpan={3}
                        className="text-center text-muted-foreground"
                      >
                        Sin números WhatsApp configurados
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="attachments" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Adjuntos</CardTitle>
              <p className="text-sm text-muted-foreground">
                Archivos asociados al negocio
              </p>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Archivo</TableHead>
                    <TableHead>Descripción</TableHead>
                    <TableHead>Tipo</TableHead>
                    <TableHead>Creado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {MOCK_ATTACHMENTS.map((att) => (
                    <TableRow key={att.businessAttachmentId}>
                      <TableCell className="font-medium">
                        {att.filename ?? att.blobPath}
                      </TableCell>
                      <TableCell>{att.description ?? "—"}</TableCell>
                      <TableCell>{att.mediaType}</TableCell>
                      <TableCell>{formatDate(att.createdAt)}</TableCell>
                    </TableRow>
                  ))}
                  {MOCK_ATTACHMENTS.length === 0 && (
                    <TableRow>
                      <TableCell
                        colSpan={4}
                        className="text-center text-muted-foreground"
                      >
                        Sin adjuntos
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="resources" className="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle>Recursos</CardTitle>
              <p className="text-sm text-muted-foreground">
                Inventario de recursos del negocio
              </p>
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Recurso</TableHead>
                    <TableHead>Cantidad</TableHead>
                    <TableHead>Creado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {MOCK_RESOURCES.map((res) => (
                    <TableRow key={res.businessResourceId}>
                      <TableCell className="font-medium">
                        {res.resourceName}
                      </TableCell>
                      <TableCell>{res.quantity}</TableCell>
                      <TableCell>{formatDate(res.createdAt)}</TableCell>
                    </TableRow>
                  ))}
                  {MOCK_RESOURCES.length === 0 && (
                    <TableRow>
                      <TableCell
                        colSpan={3}
                        className="text-center text-muted-foreground"
                      >
                        Sin recursos configurados
                      </TableCell>
                    </TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>
    </div>
  );
}
