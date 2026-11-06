"use client";

import { useParams } from "next/navigation";
import { BusinessesWorkspace } from "@/components/businesses/businesses-workspace";

export default function BusinessDetailPage() {
  const { id } = useParams<{ id: string }>();
  return <BusinessesWorkspace initialMode="view" initialBusinessId={id} />;
}
