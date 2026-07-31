CREATE TABLE [dbo].[BusinessProcessingCursors]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [LastAssignedSequence] BIGINT NOT NULL CONSTRAINT [DF_BusinessProcessingCursors_LastAssigned] DEFAULT (0),
    [LastCompletedSequence] BIGINT NOT NULL CONSTRAINT [DF_BusinessProcessingCursors_LastCompleted] DEFAULT (0),
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_BusinessProcessingCursors] PRIMARY KEY CLUSTERED ([BusinessId]),
    CONSTRAINT [FK_BusinessProcessingCursors_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [CK_BusinessProcessingCursors_Sequences]
        CHECK ([LastAssignedSequence] >= [LastCompletedSequence] AND [LastCompletedSequence] >= 0)
);

