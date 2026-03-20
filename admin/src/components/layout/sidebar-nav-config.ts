import type { LucideIcon } from "lucide-react";
import {
  Bot,
  Building2,
  Calendar,
  CalendarDays,
  CreditCard,
  FileSearch,
  LayoutDashboard,
  LayoutGrid,
  MessageSquare,
  Package,
  Settings,
  Shield,
  Store,
  UserCog,
  UserPlus,
  Users,
} from "lucide-react";

export interface NavItem {
  name: string;
  href: string;
  icon: LucideIcon;
  /** Permission required to show this item. If missing, item is always shown for authenticated users. */
  permission?: string;
}

export interface NavSeparator {
  type: "separator";
  label: string;
}

export type NavEntry = NavItem | NavSeparator;

export const navigation: NavEntry[] = [
  { name: "Dashboard", href: "/dashboard", icon: LayoutDashboard, permission: "dashboard.read" },
  { type: "separator", label: "Negocio" },
  { name: "Servicios", href: "/dashboard/services", icon: Package, permission: "services.read" },
  { name: "Empleados", href: "/dashboard/employees", icon: Users, permission: "employees.read" },
  { type: "separator", label: "Operaciones" },
  { name: "Reservaciones", href: "/dashboard/reservations", icon: CalendarDays, permission: "reservations.read" },
  { name: "Calendario", href: "/dashboard/reservations/calendar", icon: Calendar, permission: "reservations.read" },
  { name: "Conversaciones", href: "/dashboard/conversations", icon: MessageSquare, permission: "conversations.read" },
  { name: "Leads", href: "/dashboard/leads", icon: UserPlus, permission: "leads.read" },
  { type: "separator", label: "Finanzas" },
  { name: "Pagos", href: "/dashboard/payments", icon: CreditCard, permission: "payments.read" },
  { type: "separator", label: "Administración" },
  { name: "Tenants", href: "/dashboard/tenants", icon: Building2, permission: "tenants.read" },
  { name: "Negocios", href: "/dashboard/businesses", icon: Store, permission: "businesses.read" },
  { name: "Usuarios", href: "/dashboard/users", icon: UserCog, permission: "users.read" },
  { name: "Roles", href: "/dashboard/roles", icon: Shield, permission: "roles.read" },
  { name: "Auditoría", href: "/dashboard/audit-logs", icon: FileSearch, permission: "audit_logs.read" },
  { type: "separator", label: "AI" },
  { name: "Agents", href: "/dashboard/agents", icon: Bot, permission: "agents.read" },
  { name: "Workspaces", href: "/dashboard/agents/workspaces", icon: LayoutGrid, permission: "agents.read" },
  { type: "separator", label: "Configuración" },
  { name: "Configuración", href: "/dashboard/settings", icon: Settings, permission: "business_config.read" },
];
