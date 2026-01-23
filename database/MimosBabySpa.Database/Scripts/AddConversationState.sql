-- Script para agregar campo State a la tabla Conversations
-- Este campo permite estados explícitos de conversación para flujo determinístico

IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[dbo].[Conversations]') 
    AND name = 'State'
)
BEGIN
    ALTER TABLE [dbo].[Conversations]
    ADD [State] INT NOT NULL DEFAULT 0; -- 0 = Idle
    
    PRINT 'Campo State agregado a la tabla Conversations';
END
ELSE
BEGIN
    PRINT 'El campo State ya existe en la tabla Conversations';
END

GO

-- Crear índice para búsquedas por estado si es necesario
-- CREATE INDEX [IX_Conversations_State] ON [dbo].[Conversations] ([State]);
-- GO
