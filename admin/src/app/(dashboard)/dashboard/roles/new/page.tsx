"use client";

import { useSearchParams } from "next/navigation";

import { RolePermissionWorkspace } from "@/components/roles/role-permission-workspace";

export default function NewRolePage() {
  const cloneFromId = useSearchParams().get("clone") ?? undefined;
  return <RolePermissionWorkspace cloneFromId={cloneFromId} />;
}
