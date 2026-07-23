export interface PagedRequest {
  page: number;
  pageSize: number;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
  search?: string;
}

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface ApiError {
  message: string;
  statusCode: number;
  errors?: Record<string, string[]>;
}

export interface AuthUser {
  userId: string;
  tenantId: string;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  avatarUrl: string | null;
  roles: string[];
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  user: AuthUser;
  correlationId: string;
}

/** Response from BFF auth routes (login, register, refresh) - tokens are in HttpOnly cookies */
export interface AuthBffResponse {
  user: AuthUser;
  correlationId?: string;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RefreshTokenRequest {
  accessToken: string;
  refreshToken: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  username: string;
  firstName: string;
  lastName: string;
  tenantId?: string;
  phoneNumber?: string;
}

export interface DashboardStats {
  totalRevenue: number;
  totalReservations: number;
  totalLeads: number;
  totalConversations: number;
  conversionRate: number;
  revenueGrowth: number;
  reservationGrowth: number;
  leadGrowth: number;
}

export interface ChartDataPoint {
  date: string;
  value: number;
  label?: string;
}

export interface TopService {
  serviceId: string;
  serviceName: string;
  totalReservations: number;
  revenue: number;
}

export interface BusinessUsage {
  planName: string;
  planCode: string;
  creditsLimit: number;
  creditsUsed: number;
  creditsUsagePercent: number;
  periodStart: string;
  periodEnd: string;
  status: "Open" | "Exceeded" | "Closed" | number;
}

export interface SubscriptionUsageBreakdown {
  operationType: string | number;
  operationCount: number;
  creditsUsed: number;
  creditsPercent: number;
}

export interface SubscriptionUsageActivity {
  usageId: string;
  operationType: string | number;
  creditsUsed: number;
  createdAt: string;
}

export interface SubscriptionDetails {
  subscriptionId: string;
  planName: string;
  planCode: string;
  monthlyPriceCop: number;
  subscriptionStartedAt: string;
  periodStart: string;
  periodEnd: string;
  autoRenew: boolean;
  status: string | number;
  usageStatus: string | number;
  creditsIncluded: number;
  creditsExtra: number;
  creditsLimit: number;
  creditsUsed: number;
  creditsRemaining: number;
  creditsUsagePercent: number;
  includedAgents: number;
  includedUsers: number;
  includedWorkspaces: number;
  features: string[];
  usageBreakdown: SubscriptionUsageBreakdown[];
  recentUsage: SubscriptionUsageActivity[];
}
