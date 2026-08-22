"use client";

import * as React from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";

import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
const pathLabels: Record<string, string> = {
  dashboard: "Dashboard",
  analytics: "Analítica de ventas",
  services: "Servicios",
  employees: "Empleados",
  reservations: "Reservaciones",
  calendar: "Calendario",
  conversations: "Conversaciones",
  leads: "Leads",
  payments: "Pagos",
  tenants: "Tenants",
  businesses: "Negocios",
  users: "Usuarios",
  roles: "Roles",
  "audit-logs": "Auditoría",
  settings: "Configuración",
  profile: "Perfil",
};

function getLabel(segment: string): string {
  return pathLabels[segment] ?? segment.charAt(0).toUpperCase() + segment.slice(1);
}

interface BreadcrumbNavProps {
  className?: string;
}

export function BreadcrumbNav({ className }: BreadcrumbNavProps) {
  const pathname = usePathname();
  const segments = pathname.split("/").filter(Boolean);

  if (segments.length === 0) {
    return (
      <Breadcrumb className={className}>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbPage>Dashboard</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>
    );
  }

  return (
    <Breadcrumb className={className}>
      <BreadcrumbList>
        {segments.map((segment, index) => {
          const href = "/" + segments.slice(0, index + 1).join("/");
          const label = getLabel(segment);
          const isLast = index === segments.length - 1;

          return (
            <React.Fragment key={href}>
              {index > 0 && <BreadcrumbSeparator />}
              <BreadcrumbItem>
                {isLast ? (
                  <BreadcrumbPage>{label}</BreadcrumbPage>
                ) : (
                  <BreadcrumbLink asChild>
                    <Link href={href}>{label}</Link>
                  </BreadcrumbLink>
                )}
              </BreadcrumbItem>
            </React.Fragment>
          );
        })}
      </BreadcrumbList>
    </Breadcrumb>
  );
}
