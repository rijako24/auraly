-- =============================================================================
-- Migration 022: Fix email variable validation.pattern (regex)
--
-- Root-cause: seed used over-escaped backslashes in JSON (e.g. [\\\\w.-]).
-- After JSON deserialization, the pattern did not use \w (word chars) and
-- rejected valid emails like richardjacomeg-1@gmail.com despite correct extraction.
--
-- Fix: stored JSON must contain \\w and \\. so C# receives ^[\w.-]+@[\w.-]+\.\w+$
-- =============================================================================

BEGIN TRANSACTION;

-- Legacy broken pattern as stored by 009 (four backslashes before w in T-SQL = 4 chars in JSON text)
DECLARE @BadPattern NVARCHAR(200) = N'^[\\\\w.-]+@[\\\\w.-]+\\\\.\\\\w+$';
DECLARE @GoodPattern NVARCHAR(200) = N'^[\\w.-]+@[\\w.-]+\\.\\w+$';

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = REPLACE([DefinitionJson], @BadPattern, @GoodPattern),
    [UpdatedAt]      = GETUTCDATE()
WHERE CHARINDEX(@BadPattern, [DefinitionJson]) > 0;

DECLARE @Updated INT = @@ROWCOUNT;

IF @Updated = 0
    PRINT '022: No broken email pattern found (already fixed or different flows).';
ELSE
    PRINT '022: Email validation.pattern fixed for ' + CAST(@Updated AS NVARCHAR(10)) + ' flow definition(s).';

COMMIT TRANSACTION;
