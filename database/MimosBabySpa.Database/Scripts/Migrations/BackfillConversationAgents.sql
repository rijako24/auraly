-- Backfill the agent responsible for conversations created before AgentId existed.
SET NOCOUNT ON;
SET XACT_ABORT ON;

UPDATE conversation
SET AgentId = resolved.AgentId
FROM dbo.Conversations AS conversation
CROSS APPLY (
    SELECT TOP (1) agent.AgentId
    FROM dbo.Agents AS agent
    WHERE agent.BusinessId = conversation.BusinessId
      AND agent.Kind = N'customer'
    ORDER BY
        CASE WHEN agent.IsActive = 1 THEN 0 ELSE 1 END,
        agent.CreatedAt
) AS resolved
WHERE conversation.AgentId IS NULL;

