"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { ArrowLeft, Building2, Users } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { Tenant, Business, AppUser } from "@/types/entities";
import { formatDate } from "@/lib/utils";

// ============ MOCK DATA ============
const MOCK_TENANT: Tenant = {
  tenantId: "tnt-1",
  name: "Mimos Baby Spa",
  email: "admin@mimosbabyspa.com",
  isActive: true,
  createdAt: "2024-01-15T10:00:00Z",
  updatedAt: null,
};

const MOCK_BUSINESSES: Business[] = [
  {
    businessId: "bus-1",
    tenantId: "tnt-1",
    name: "Mimos Baby Spa Centro",
    description: "Spa para bebés en el centro de la ciudad",
    address: "Calle 45 #12-34",
    phone: "+57 300 123 4567",
    email: "centro@mimosbabyspa.com",
    website: "https://mimosbabyspa.com",
    logoUrl: null,
    isActive: true,
    createdAt: "2024-01-20T10:00:00Z",
    updatedAt: null,
  },
  {
    businessId: "bus-2",
    tenantId: "tnt-1",
    name: "Mimos Baby Spa Norte",
    description: "Sucursal norte",
    address: "Cra 50 #80-10",
    phone: "+57 300 987 6543",
    email: "norte@mimosbabyspa.com",
    website: "",
    logoUrl: null,
    isActive: true,
    createdAt: "2024-03-01T10:00:00Z",
    updatedAt: null,
  },
];

const MOCK_USERS: AppUser[] = [
  {
    userId: "usr-1",
    tenantId: "tnt-1",
    username: "admin.mimos",
    email: "admin@mimosbabyspa.com",
    firstName: "Carlos",
    lastName: "García",
    phoneNumber: "+57 310 111 2233",
    avatarUrl: null,
    isActive: true,
    emailConfirmed: true,
    lastLoginAt: "2024-03-15T14:30:00Z",
    createdAt: "2024-01-15T10:00:00Z",
    updatedAt: null,
    fullName: "Carlos García",
  },
  {
    userId: "usr-2",
    tenantId: "tnt-1",
    username: "recepcion.mimos",
    email: "recepcion@mimosbabyspa.com",
    firstName: "María",
    lastName: "López",
    phoneNumber: null,
    avatarUrl: null,
    isActive: true,
    emailConfirmed: true,
    lastLoginAt: "2024-03-14T09:15:00Z",
    createdAt: "2024-02-01T08:00:00Z",
    updatedAt: null,
    fullName: "María López",
  },
];

export default function TenantDetailPage() {
  const params = useParams();
  const id = params.id as string;
  const tenant = MOCK_TENANT;
  const businesses = MOCK_BUSINESSES;
  const users = MOCK_USERS;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link href="/dashboard/tenants">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">
            {tenant.name}
          </h1>
          <p className="text-muted-foreground">Detalle del tenant</p>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <Badge variant={tenant.isActive ? "default" : "secondary"}>
          {tenant.isActive ? "Activo" : "Inactivo"}
        </Badge>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Información del tenant</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <p className="text-sm font-medium text-muted-foreground">
                Nombre
              </p>
              <p>{tenant.name}</p>
            </div>
            <div>
              <p className="text-sm font-medium text-muted-foreground">
                Email
              </p>
              <p>{tenant.email}</p>
            </div>
            <div>
              <p className="text-sm font-medium text-muted-foreground">
                Estado
              </p>
              <p>{tenant.isActive ? "Activo" : "Inactivo"}</p>
            </div>
            <div>
              <p className="text-sm font-medium text-muted-foreground">
                Creado
              </p>
              <p>{formatDate(tenant.createdAt)}</p>
            </div>
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Building2 className="h-5 w-5" />
              Negocios ({businesses.length})
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              Negocios asociados a este tenant
            </p>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Nombre</TableHead>
                  <TableHead>Dirección</TableHead>
                  <TableHead>Estado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {businesses.map((b) => (
                  <TableRow key={b.businessId}>
                    <TableCell className="font-medium">{b.name}</TableCell>
                    <TableCell className="max-w-[200px] truncate">
                      {b.address}
                    </TableCell>
                    <TableCell>
                      <Badge variant={b.isActive ? "default" : "secondary"}>
                        {b.isActive ? "Activo" : "Inactivo"}
                      </Badge>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Users className="h-5 w-5" />
              Usuarios ({users.length})
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              Usuarios pertenecientes a este tenant
            </p>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Nombre</TableHead>
                  <TableHead>Email</TableHead>
                  <TableHead>Estado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {users.map((u) => (
                  <TableRow key={u.userId}>
                    <TableCell className="font-medium">
                      {u.firstName} {u.lastName}
                    </TableCell>
                    <TableCell>{u.email}</TableCell>
                    <TableCell>
                      <Badge variant={u.isActive ? "default" : "secondary"}>
                        {u.isActive ? "Activo" : "Inactivo"}
                      </Badge>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
