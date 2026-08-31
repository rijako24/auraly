import { TenantProvisioningWizard } from "@/app/(dashboard)/dashboard/tenants/new/page";

export default function RegisterPage() {
  return <div className="fixed inset-0 z-50 overflow-y-auto bg-[#f4f9f8] px-4 py-8 sm:px-8"><TenantProvisioningWizard publicMode /></div>;
}
