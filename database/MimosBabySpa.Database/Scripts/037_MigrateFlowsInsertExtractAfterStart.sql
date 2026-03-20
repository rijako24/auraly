-- =============================================================================
-- Migración manual: inserta nodo Extract (type 8) entre Inicio y el primer paso.
-- Requisito: haber ejecutado 036_FlowModernNodes.sql (catálogo extract/agent).
--
-- Qué hace (por cada fila activa en FlowDefinitions):
--   1. Omite si el JSON ya contiene algún nodo con "type": 8.
--   2. Localiza el nodo Start (type 0); si no hay, omite.
--   3. Cuenta aristas con sourceNodeId = id del Start; si no es exactamente 1, omite
--      (evita romper flujos con varias salidas desde Inicio — revisar a mano).
--   4. Añade nodo extract_flow_entry (o sufijo único si el id ya existe).
--   5. Cambia la arista Start → X a Start → Extract; añade Extract → X.
--
-- USO:
--   1. Abrir script, dejar @DryRun = 1 y ejecutar; revisar mensajes PRINT.
--   2. Poner @DryRun = 0 y ejecutar dentro de una ventana con backup / transacción.
--
-- NO está incluido en PostDeployment: ejecútalo solo cuando quieras migrar datos.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun BIT = 1; -- 0 = aplicar cambios en BD

DECLARE @fid UNIQUEIDENTIFIER;
DECLARE @json NVARCHAR(MAX);
DECLARE @newJson NVARCHAR(MAX);

DECLARE @startId NVARCHAR(256);
DECLARE @edgeKey NVARCHAR(32);
DECLARE @oldTarget NVARCHAR(256);
DECLARE @edgeCount INT;

DECLARE @extractId NVARCHAR(128);
DECLARE @extractNodeJson NVARCHAR(1000);
DECLARE @newEdgeJson NVARCHAR(1000);

DECLARE @updated INT = 0;
DECLARE @skipped INT = 0;

DECLARE c CURSOR LOCAL FAST_FORWARD READ_ONLY FOR
    SELECT FlowDefinitionId, DefinitionJson
    FROM dbo.FlowDefinitions
    WHERE IsActive = 1;

OPEN c;

WHILE 1 = 1
BEGIN
    FETCH NEXT FROM c INTO @fid, @json;

    IF @@FETCH_STATUS <> 0
        BREAK;

    SET @newJson = NULL;
    SET @startId = NULL;
    SET @edgeKey = NULL;
    SET @oldTarget = NULL;
    SET @edgeCount = 0;
    SET @extractId = NULL;

    IF @json IS NULL OR ISJSON(@json) = 0
    BEGIN
        SET @skipped += 1;
        PRINT N'[omitido] ' + CAST(@fid AS NVARCHAR(36)) + N': DefinitionJson vacío o no es JSON.';
        CONTINUE;
    END;

    -- Ya tiene Extract
    IF EXISTS (
        SELECT 1
        FROM OPENJSON(@json, N'$.nodes')
                 WITH (type INT N'$.type') AS n
        WHERE n.type = 8
    )
    BEGIN
        SET @skipped += 1;
        PRINT N'[omitido] ' + CAST(@fid AS NVARCHAR(36)) + N': ya contiene nodo type 8 (Extract).';
        CONTINUE;
    END;

    SELECT TOP (1) @startId = j.id
    FROM OPENJSON(@json, N'$.nodes')
             WITH (
                 id NVARCHAR(256) N'$.id',
                 type INT N'$.type'
                 ) AS j
    WHERE j.type = 0
    ORDER BY j.id;

    IF @startId IS NULL
    BEGIN
        SET @skipped += 1;
        PRINT N'[omitido] ' + CAST(@fid AS NVARCHAR(36)) + N': no hay nodo Start (type 0).';
        CONTINUE;
    END;

    SELECT @edgeCount = COUNT(*)
    FROM OPENJSON(@json, N'$.edges')
             WITH (sourceNodeId NVARCHAR(256) N'$.sourceNodeId') AS e
    WHERE e.sourceNodeId = @startId;

    IF @edgeCount = 0
    BEGIN
        SET @skipped += 1;
        PRINT N'[omitido] ' + CAST(@fid AS NVARCHAR(36)) + N': Start sin aristas salientes.';
        CONTINUE;
    END;

    IF @edgeCount > 1
    BEGIN
        SET @skipped += 1;
        PRINT N'[omitido] ' + CAST(@fid AS NVARCHAR(36)) + N': Start tiene ' + CAST(@edgeCount AS NVARCHAR(10)) +
              N' aristas; migración solo soporta 1. Revisar manualmente.';
        CONTINUE;
    END;

    SELECT TOP (1)
           @edgeKey = o.[key],
           @oldTarget = JSON_VALUE(o.value, N'$.targetNodeId')
    FROM OPENJSON(@json, N'$.edges') AS o
    WHERE JSON_VALUE(o.value, N'$.sourceNodeId') = @startId
    ORDER BY CAST(o.[key] AS INT);

    IF @oldTarget IS NULL OR @oldTarget = N''
    BEGIN
        SET @skipped += 1;
        PRINT N'[omitido] ' + CAST(@fid AS NVARCHAR(36)) + N': arista desde Start sin targetNodeId.';
        CONTINUE;
    END;

    SET @extractId = N'extract_flow_entry';
    IF EXISTS (
        SELECT 1
        FROM OPENJSON(@json, N'$.nodes')
                 WITH (id NVARCHAR(256) N'$.id') AS n
        WHERE n.id = @extractId
    )
        SET @extractId = N'extract_flow_entry_' + REPLACE(CAST(NEWID() AS NVARCHAR(36)), N'-', N'');

    SET @extractNodeJson =
            N'{"id":"' + @extractId + N'","type":8,"label":"Extracción (IA)","config":{"catalogKey":"extract"}}';

    SET @newJson = JSON_MODIFY(@json, N'append $.nodes', JSON_QUERY(@extractNodeJson));

    SET @newJson = JSON_MODIFY(@newJson, N'$.edges[' + @edgeKey + N'].targetNodeId', @extractId);

    -- Arista Extract → primer nodo (salida por defecto del nodo Extract; sin portId).
    SET @newEdgeJson =
            N'{"id":"e_extract_' + REPLACE(CAST(NEWID() AS NVARCHAR(36)), N'-', N'') + N'","sourceNodeId":"' +
            @extractId + N'","targetNodeId":"' + @oldTarget + N'"}';

    SET @newJson = JSON_MODIFY(@newJson, N'append $.edges', JSON_QUERY(@newEdgeJson));

    IF @DryRun = 1
    BEGIN
        SET @updated += 1;
        PRINT N'[dry-run OK] ' + CAST(@fid AS NVARCHAR(36)) + N': Start ''' + @startId + N''' → Extract ''' +
              @extractId + N''' → ''' + @oldTarget + N'''.';
        CONTINUE;
    END;

    UPDATE dbo.FlowDefinitions
    SET DefinitionJson = @newJson,
        UpdatedAt      = GETUTCDATE()
    WHERE FlowDefinitionId = @fid;

    SET @updated += 1;
    PRINT N'[actualizado] ' + CAST(@fid AS NVARCHAR(36)) + N': Extract insertado tras Start.';
END;

CLOSE c;
DEALLOCATE c;

PRINT N'';
PRINT N'Resumen: procesados (actualizables o dry-run OK) = ' + CAST(@updated AS NVARCHAR(20));
PRINT N'           omitidos = ' + CAST(@skipped AS NVARCHAR(20));
IF @DryRun = 1
    PRINT N'Modo DRY RUN: no se escribió en FlowDefinitions. Poner @DryRun = 0 para aplicar.';
GO
