import type { LucideIcon } from "lucide-react";
import {
  BadgePercent,
  BarChart3,
  Building2,
  Calendar,
  CalendarDays,
  CircleDollarSign,
  CreditCard,
  ContactRound,
  Library,
  FileSearch,
  Gem,
  Landmark,
  LayoutDashboard,
  MessageSquare,
  Megaphone,
  Radio,
  ReceiptText,
  Route,
  Package,
  PackagePlus,
  Settings,
  Shield,
  ShoppingCart,
  Sparkles,
  Store,
  TrendingUp,
  Undo2,
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
  { name: "Analytics", href: "/dashboard/analytics", icon: BarChart3, permission: "dashboard.read" },
  { type: "separator", label: "Negocio" },
  { name: "Servicios", href: "/dashboard/services", icon: Package, permission: "services.read" },
  { name: "Productos", href: "/dashboard/products", icon: Package, permission: "catalog.read" },
  { name: "Terceros", href: "/dashboard/parties", icon: ContactRound, permission: "parties.read" },
  { name: "Rutas comerciales", href: "/dashboard/routes", icon: Route, permission: "routes.read" },
  { name: "Precios y rentabilidad", href: "/dashboard/products/pricing", icon: TrendingUp, permission: "pricing.read" },
  { name: "Promociones", href: "/dashboard/promotions", icon: BadgePercent, permission: "promotions.read" },
  { name: "Empleados", href: "/dashboard/employees", icon: Users, permission: "employees.read" },
  { type: "separator", label: "Operaciones" },
  { name: "Punto de venta", href: "/pos", icon: ReceiptText, permission: "sales.create" },
  { name: "Inventario", href: "/dashboard/inventory", icon: Package, permission: "inventory.read" },
  { name: "Recepción de mercancía", href: "/dashboard/purchasing/goods-receipts", icon: PackagePlus, permission: "purchasing.goods-receipts.read" },
  { name: "Devoluciones a proveedores", href: "/dashboard/purchasing/purchase-returns", icon: Undo2, permission: "purchasing.purchase-returns.read" },
  { name: "Devoluciones de venta", href: "/dashboard/sales-returns", icon: Undo2, permission: "sales.returns.read" },
  { name: "Reservaciones", href: "/dashboard/reservations", icon: CalendarDays, permission: "reservations.read" },
  { name: "Calendario", href: "/dashboard/reservations/calendar", icon: Calendar, permission: "reservations.read" },
  { name: "Agente IA", href: "/dashboard/agents", icon: Sparkles, permission: "agents.read" },
  { name: "Canales", href: "/dashboard/channels", icon: Radio, permission: "business_config.read" },
  { name: "Contactos inbound", href: "/dashboard/agents/inbound-contacts", icon: UserCog, permission: "agents.read" },
  { name: "Conversaciones", href: "/dashboard/conversations", icon: MessageSquare, permission: "conversations.read" },
  { name: "Leads", href: "/dashboard/leads", icon: UserPlus, permission: "leads.read" },
  { name: "Campanas", href: "/dashboard/campaigns", icon: Megaphone, permission: "campaigns.read" },
  { type: "separator", label: "Finanzas" },
  { name: "Suscripcion", href: "/dashboard/subscription", icon: Gem, permission: "dashboard.read" },
  { name: "Pedidos", href: "/dashboard/orders", icon: ShoppingCart, permission: "orders.read" },
  { name: "Pagos", href: "/dashboard/payments", icon: CreditCard, permission: "payments.read" },
  { name: "Cuentas por pagar", href: "/dashboard/payables", icon: Landmark, permission: "payables.read" },
  { name: "Cuentas por cobrar", href: "/dashboard/receivables", icon: CircleDollarSign, permission: "receivables.read" },
  { type: "separator", label: "Administracion" },
  { name: "Tenants", href: "/dashboard/tenants", icon: Building2, permission: "tenants.read" },
  { name: "Negocios", href: "/dashboard/businesses", icon: Store, permission: "businesses.read" },
  { name: "Usuarios", href: "/dashboard/users", icon: UserCog, permission: "users.read" },
  { name: "Roles", href: "/dashboard/roles", icon: Shield, permission: "roles.read" },
  { name: "Auditoria", href: "/dashboard/audit-logs", icon: FileSearch, permission: "audit_logs.read" },
  { type: "separator", label: "Configuracion" },
  { name: "Maestros", href: "/dashboard/settings/masters", icon: Library, permission: "masters.geography.read" },
  { name: "Configuracion", href: "/dashboard/settings", icon: Settings, permission: "business_config.read" },
];
