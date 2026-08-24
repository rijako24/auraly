"use client";

import { useParams } from "next/navigation";

import { RolePermissionWorkspace } from "@/components/roles/role-permission-workspace";

export default function RoleDetailPage() {
  const params = useParams();
  return <RolePermissionWorkspace roleId={params.id as string} />;
}
