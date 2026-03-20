"use client";

import { useParams } from "next/navigation";

import { AgentWorkspace } from "@/components/agents/agent-workspace";
import { PageError } from "@/components/ui/page-error";
import { PageLoading } from "@/components/ui/page-loading";
import { useAgentDetail } from "@/hooks/use-agents";

export default function AgentWorkspacePage() {
  const params = useParams();
  const agentId = typeof params.agentId === "string" ? params.agentId : "";
  const { data: agent, isLoading, isError, refetch } = useAgentDetail(agentId || null);

  if (!agentId) return null;
  if (isLoading) return <PageLoading cards={0} />;
  if (isError || !agent) return <PageError onRetry={() => refetch()} />;

  return <AgentWorkspace agentId={agentId} agentName={agent.name} />;
}
