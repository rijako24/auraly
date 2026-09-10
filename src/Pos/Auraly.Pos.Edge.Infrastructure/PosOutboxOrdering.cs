namespace Auraly.Pos.Edge.Infrastructure;

public static class PosOutboxOrdering
{
    // Ordering is owned by each WorkSession, not by the whole SQLite database.
    // Its opening precedes its documents and its closure follows all of them.
    // Independent sessions continue synchronizing when another session retries.
    public const string NoBlockingPriorRowSql = """
        NOT EXISTS
        (
          SELECT 1 FROM Outbox prior
          WHERE prior.Status<>'Uploaded'
            AND
            (
              (
                (prior.WorkSessionId IS NULL OR current.WorkSessionId IS NULL)
                AND prior.LocalSequence<current.LocalSequence
              )
              OR
              (
                prior.WorkSessionId=current.WorkSessionId
                AND
                (
                  (prior.Type='work-session.opened'
                   AND current.Type<>'work-session.opened')
                  OR (current.Type='work-session.closed'
                      AND prior.Type<>'work-session.closed')
                  OR (prior.Type<>'work-session.closed'
                      AND current.Type<>'work-session.opened'
                      AND prior.LocalSequence<current.LocalSequence)
                )
              )
            )
        )
        """;

    public static bool Blocks(
        string priorStatus,
        long? priorSequence,
        string priorType,
        Guid? priorWorkSessionId,
        long? currentSequence,
        string currentType,
        Guid? currentWorkSessionId) =>
        !string.Equals(priorStatus, PosOutboxStatus.Uploaded, StringComparison.Ordinal) &&
        BlocksWithinScope(
            priorSequence,
            priorType,
            priorWorkSessionId,
            currentSequence,
            currentType,
            currentWorkSessionId);

    private static bool BlocksWithinScope(
        long? priorSequence,
        string priorType,
        Guid? priorWorkSessionId,
        long? currentSequence,
        string currentType,
        Guid? currentWorkSessionId)
    {
        if (priorWorkSessionId is null || currentWorkSessionId is null)
            return priorSequence < currentSequence;
        if (priorWorkSessionId != currentWorkSessionId) return false;
        if (string.Equals(
                priorType,
                PosOutboxMessageTypes.WorkSessionOpened,
                StringComparison.Ordinal) &&
            !string.Equals(
                currentType,
                PosOutboxMessageTypes.WorkSessionOpened,
                StringComparison.Ordinal))
            return true;
        if (string.Equals(
                currentType,
                PosOutboxMessageTypes.WorkSessionClosure,
                StringComparison.Ordinal) &&
            !string.Equals(
                priorType,
                PosOutboxMessageTypes.WorkSessionClosure,
                StringComparison.Ordinal))
            return true;
        if (string.Equals(
                priorType,
                PosOutboxMessageTypes.WorkSessionClosure,
                StringComparison.Ordinal) ||
            string.Equals(
                currentType,
                PosOutboxMessageTypes.WorkSessionOpened,
                StringComparison.Ordinal))
            return false;
        return priorSequence < currentSequence;
    }
}
