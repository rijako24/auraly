"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

import { PageLoading } from "@/components/ui/page-loading";
import { useAuthStore } from "@/stores/auth-store";

export default function CompanyPage() {
  const router = useRouter();
  const tenantId = useAuthStore((state) => state.user?.tenantId);
  const hasHydrated = useAuthStore((state) => state.hasHydrated);

  useEffect(() => {
    if (hasHydrated && tenantId) {
      router.replace(`/dashboard/tenants/${tenantId}?scope=profile`);
    }
  }, [hasHydrated, router, tenantId]);

  return <PageLoading cards={2} />;
}
