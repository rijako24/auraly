CREATE TRIGGER [dbo].[TR_FiscalSeries_PreventOverlappingActiveRanges]
ON [dbo].[FiscalSeries]
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM [dbo].[FiscalSeries] AS [candidate]
        INNER JOIN [dbo].[FiscalSeries] AS [other]
            ON [other].[SeriesId] <> [candidate].[SeriesId]
            AND [other].[BusinessId] = [candidate].[BusinessId]
            AND [other].[FiscalAuthorizationId] = [candidate].[FiscalAuthorizationId]
            AND [other].[DocumentType] = [candidate].[DocumentType]
            AND [other].[Prefix] = [candidate].[Prefix]
            AND [other].[IsActive] = 1
            AND [candidate].[RangeStart] <= [other].[RangeEnd]
            AND [other].[RangeStart] <= [candidate].[RangeEnd]
        INNER JOIN [inserted] AS [changed]
            ON [changed].[SeriesId] = [candidate].[SeriesId]
        WHERE [candidate].[IsActive] = 1
    )
    BEGIN
        THROW 51020, 'Las series fiscales activas del mismo negocio, autorización, tipo y prefijo no pueden tener rangos solapados.', 1;
    END;
END;
