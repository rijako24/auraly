-- ============================================================
-- Migración: 20260218000009_AddSelectedAddOnsToEntityExtractionConfig
-- Ejecutar en SSMS o sqlcmd contra la base talkioai
--
-- Agrega el atributo SelectedAddOns al EntityExtractionConfig de Mimos.
-- Permite que el LLM extraiga los add-ons elegidos por el cliente.
-- ============================================================

PRINT '=== Iniciando migración 20260218000009 ===';
GO

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @ConfigKey INT = 1; -- BusinessConfigurationKey.EntityExtractionConfig

UPDATE [dbo].[BusinessConfigurations]
SET [Value] = N'{
  "BabyAge": {
    "Name": "BabyAge",
    "DisplayName": "Edad del bebé",
    "Description": "Edad del bebé en meses",
    "Type": "Number",
    "IsRequired": false,
    "ValidationPattern": "^\d{1,3}$",
    "Metadata": {
      "min": "0",
      "max": "120"
    }
  },
  "BabyName": {
    "Name": "BabyName",
    "DisplayName": "Nombre del bebé",
    "Description": "Nombre del bebé",
    "Type": "Text",
    "IsRequired": false,
    "Metadata": {
      "minLength": "2",
      "maxLength": "50"
    }
  },
  "SpecialConditions": {
    "Name": "SpecialConditions",
    "DisplayName": "Condiciones especiales",
    "Description": "Condiciones médicas o especiales del bebé",
    "Type": "Text",
    "IsRequired": false,
    "Metadata": {
      "maxLength": "500"
    }
  },
  "SelectedAddOns": {
    "Name": "SelectedAddOns",
    "DisplayName": "Add-ons seleccionados",
    "Description": "Lista de add-ons que el cliente eligió. Nombres exactos del catálogo separados por coma (ej: Fotografía Sencilla, Decoración Premium). Solo incluir si el cliente aceptó add-ons.",
    "Type": "Text",
    "IsRequired": false,
    "Metadata": {
      "maxLength": "500"
    }
  }
}',
    [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId
  AND [Key] = @ConfigKey;

PRINT 'EntityExtractionConfig actualizado con SelectedAddOns.';
GO

-- Registrar en historial de migraciones EF (si aplica)
IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218000009_AddSelectedAddOnsToEntityExtractionConfig')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218000009_AddSelectedAddOnsToEntityExtractionConfig', N'8.0.0');
    PRINT 'Migración registrada en __EFMigrationsHistory.';
END
GO

PRINT '✅ Migración 20260218000009 aplicada correctamente.';
