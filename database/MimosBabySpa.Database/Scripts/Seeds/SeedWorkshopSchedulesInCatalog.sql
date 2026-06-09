-- =============================================================================
-- SeedWorkshopSchedulesInCatalog.sql
--
-- Configura los servicios de categoria Taller como inscripciones con horario fijo.
-- El horario vive en Services.FixedScheduleLabel.
-- =============================================================================

SET NOCOUNT ON;

;WITH Talleres AS
(
    SELECT
        s.ServiceId,
        CASE
            WHEN s.Description LIKE N'%HORARIO DE INSCRIPCION:%'
                THEN RTRIM(LEFT(s.Description, CHARINDEX(N'HORARIO DE INSCRIPCION:', s.Description) - 1))
            ELSE s.Description
        END AS CleanDescription,
        ROW_NUMBER() OVER (
            PARTITION BY s.BusinessId
            ORDER BY
                CASE
                    WHEN s.ServiceName LIKE N'%3%dia%' OR s.ServiceName LIKE N'%3 d%' THEN 1
                    WHEN s.ServiceName LIKE N'%2%dia%' OR s.ServiceName LIKE N'%2 d%' THEN 2
                    WHEN s.ServiceName LIKE N'%1%dia%' OR s.ServiceName LIKE N'%1 d%' THEN 3
                    ELSE 99
                END,
                s.ServiceName
        ) AS SlotOrder,
        CASE
            WHEN s.ServiceName LIKE N'%3%dia%' OR s.ServiceName LIKE N'%3 d%'
                THEN N'lunes, miercoles y viernes 09:00-10:00'
            WHEN s.ServiceName LIKE N'%2%dia%' OR s.ServiceName LIKE N'%2 d%'
                THEN N'martes y jueves 10:00-11:00'
            WHEN s.ServiceName LIKE N'%1%dia%' OR s.ServiceName LIKE N'%1 d%'
                THEN N'sabado 11:00-12:00'
            ELSE NULL
        END AS NamedSchedule
    FROM dbo.Services s
    INNER JOIN dbo.ServiceCategories sc ON sc.ServiceCategoryId = s.CategoryId
    WHERE s.IsActive = 1
      AND sc.Name = N'Taller'
)
UPDATE s
SET
    s.FulfillmentKind = 1,
    s.FixedScheduleLabel = COALESCE(
        t.NamedSchedule,
        CASE ((t.SlotOrder - 1) % 6) + 1
            WHEN 1 THEN N'lunes, miercoles y viernes 09:00-10:00'
            WHEN 2 THEN N'martes y jueves 10:00-11:00'
            WHEN 3 THEN N'sabado 11:00-12:00'
            WHEN 4 THEN N'lunes, miercoles y viernes 11:00-12:00'
            WHEN 5 THEN N'martes y jueves 15:00-16:00'
            ELSE N'sabado 15:00-16:00'
        END),
    s.Description = t.CleanDescription
FROM dbo.Services s
INNER JOIN Talleres t ON t.ServiceId = s.ServiceId;

PRINT N'Seed de fulfillment de talleres completado.';
