import type { LucideIcon } from "lucide-react";
import {
  BadgePercent,
  BarChart3,
  Building2,
  Calendar,
  CalendarDays,
  ClipboardList,
  CircleDollarSign,
  Scale,
  CreditCard,
  ContactRound,
  Library,
  FileSearch,
  FileText,
  FileKey2,
  Gem,
  Landmark,
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
  Truck,
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

export interface NavGroup {
  label: string;
  items: NavItem[];
}

export const navigation: NavEntry[] = [
  { name: "Hoy", href: "/dashboard", icon: TrendingUp, permission: "sales.reports.read" },
  { type: "separator", label: "Comercial" },
  { name: "Punto de venta", href: "/pos", icon: ReceiptText, permission: "sales.create" },
  { name: "Pedidos", href: "/dashboard/orders", icon: ShoppingCart, permission: "orders.read" },
  { name: "Servicios y facturación", href: "/dashboard/service-invoices", icon: FileText, permission: "service-invoices.read" },
  { name: "Rutas comerciales", href: "/dashboard/routes", icon: Route, permission: "routes.read" },
  { type: "separator", label: "Reportes" },
  { name: "Ventas", href: "/dashboard/reports/sales", icon: BarChart3, permission: "sales.reports.read" },
  { name: "Vendedores", href: "/dashboard/reports/sellers", icon: Users, permission: "sales.reports.read" },
  { name: "Clientes y cobertura", href: "/dashboard/reports/customers", icon: ContactRound, permission: "sales.reports.read" },
  { name: "Visitas comerciales", href: "/dashboard/reports/visits", icon: Route, permission: "sales.reports.read" },
  { name: "Impacto proveedores", href: "/dashboard/reports/supplier-impact", icon: Truck, permission: "sales.reports.read" },
  { type: "separator", label: "Catálogo" },
  { name: "Servicios", href: "/dashboard/services", icon: Package, permission: "services.read" },
  { name: "Productos", href: "/dashboard/products", icon: Package, permission: "catalog.read" },
  { name: "Terceros", href: "/dashboard/parties", icon: ContactRound, permission: "parties.read" },
  { name: "Precios y rentabilidad", href: "/dashboard/products/pricing", icon: TrendingUp, permission: "pricing.read" },
  { name: "Canal de precios", href: "/dashboard/products/price-segments", icon: BadgePercent, permission: "pricing.segments.read" },
  { name: "Promociones", href: "/dashboard/promotions", icon: BadgePercent, permission: "promotions.read" },
  { type: "separator", label: "Compras e inventario" },
  { name: "Inventario", href: "/dashboard/inventory", icon: Package, permission: "inventory.read" },
  { name: "Órdenes de compra", href: "/dashboard/purchasing/purchase-orders", icon: ClipboardList, permission: "purchasing.purchase-orders.read" },
  { name: "Recepciones de compra", href: "/dashboard/purchasing/goods-receipts", icon: PackagePlus, permission: "purchasing.goods-receipts.read" },
  { name: "Devoluciones a proveedores", href: "/dashboard/purchasing/purchase-returns", icon: Undo2, permission: "purchasing.purchase-returns.read" },
  { type: "separator", label: "Logística" },
  { name: "Despachos", href: "/dashboard/dispatches", icon: Truck, permission: "dispatches.read" },
  { name: "Mis entregas", href: "/dashboard/deliveries", icon: Route, permission: "dispatches.delivery.execute" },
  { type: "separator", label: "Atención y crecimiento" },
  { name: "Agente IA", href: "/dashboard/agents", icon: Sparkles, permission: "agents.read" },
  { name: "Canales de atención", href: "/dashboard/channels", icon: Radio, permission: "agents.read" },
  { name: "Bandeja de contactos", href: "/dashboard/agents/inbound-contacts", icon: UserCog, permission: "agents.read" },
  { name: "Conversaciones", href: "/dashboard/conversations", icon: MessageSquare, permission: "conversations.read" },
  { name: "Leads", href: "/dashboard/leads", icon: UserPlus, permission: "leads.read" },
  { name: "Campañas", href: "/dashboard/campaigns", icon: Megaphone, permission: "campaigns.read" },
  { name: "Reservaciones", href: "/dashboard/reservations", icon: CalendarDays, permission: "reservations.read" },
  { name: "Calendario", href: "/dashboard/reservations/calendar", icon: Calendar, permission: "reservations.read" },
  { type: "separator", label: "Finanzas" },
  { name: "Contabilidad", href: "/dashboard/accounting", icon: Library, permission: "accounting.read" },
  { name: "Notas crédito de venta", href: "/dashboard/sales-returns", icon: Undo2, permission: "sales.returns.read" },
  { name: "Notas débito de venta", href: "/dashboard/sales-debit-notes", icon: ReceiptText, permission: "sales.debit-notes.read" },
  { name: "Cierres de sesión", href: "/dashboard/cash-differences", icon: Scale, permission: "work-sessions.differences.read" },
  { name: "Suscripción", href: "/dashboard/subscription", icon: Gem, permission: "dashboard.read" },
  { name: "Pagos", href: "/dashboard/payments", icon: CreditCard, permission: "payments.read" },
  { name: "Cuentas por pagar", href: "/dashboard/payables", icon: Landmark, permission: "payables.read" },
  { name: "Cuentas por cobrar", href: "/dashboard/receivables", icon: CircleDollarSign, permission: "receivables.read" },
  { name: "Gastos", href: "/dashboard/expenses", icon: ReceiptText, permission: "expenses.read" },
  { name: "Nómina", href: "/dashboard/payroll", icon: Users, permission: "payroll.read" },
  { type: "separator", label: "Administración" },
  { name: "Empresas", href: "/dashboard/tenants", icon: Building2, permission: "tenants.read" },
  { name: "Sedes", href: "/dashboard/businesses", icon: Store, permission: "businesses.read" },
  { name: "Roles", href: "/dashboard/roles", icon: Shield, permission: "roles.read" },
  { name: "Auditoría", href: "/dashboard/audit-logs", icon: FileSearch, permission: "audit_logs.read" },
  { type: "separator", label: "Configuración" },
  { name: "Maestros", href: "/dashboard/settings/masters", icon: Library, permission: "masters.geography.read" },
  { name: "DIAN", href: "/dashboard/settings/fiscal", icon: FileKey2, permission: "fiscal.configuration.read" },
  { name: "Configuración", href: "/dashboard/settings", icon: Settings, permission: "business_config.read" },
];

export function authorizedNavigationItems(permissions: readonly string[]): NavItem[] {
  const granted = new Set(permissions);
  return navigation.filter((entry): entry is NavItem =>
    "href" in entry && (!entry.permission || granted.has(entry.permission)));
}

export function authorizedNavigationGroups(permissions: readonly string[]): NavGroup[] {
  const granted = new Set(permissions);
  const groups: NavGroup[] = [];
  let currentGroup: NavGroup | undefined;

  for (const entry of navigation) {
    if ("type" in entry) {
      currentGroup = { label: entry.label, items: [] };
      groups.push(currentGroup);
      continue;
    }

    if (entry.permission && !granted.has(entry.permission)) continue;
    if (!currentGroup) {
      currentGroup = { label: "Principal", items: [] };
      groups.push(currentGroup);
    }
    currentGroup.items.push(entry);
  }

  return groups.filter((group) => group.items.length > 0);
}
