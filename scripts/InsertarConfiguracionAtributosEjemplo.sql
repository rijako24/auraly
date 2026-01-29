-- Script para insertar configuración de atributos de ejemplo para Baby Spa
-- Ejecutar este script para que el sistema pueda extraer información específica del negocio

-- NOTA: Reemplaza 'TU-BUSINESS-ID-AQUI' con el BusinessId real de tu negocio
-- El BusinessId de ejemplo es: 22222222-2222-2222-2222-222222222222

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @ConfigKey INT = 2; -- BusinessConfigurationKey.EntityExtractionConfig

-- Verificar si ya existe configuración
IF EXISTS (
    SELECT 1 
    FROM BusinessConfigurations 
    WHERE BusinessId = @BusinessId 
    AND [Key] = @ConfigKey
)
BEGIN
    -- Actualizar configuración existente
    UPDATE BusinessConfigurations
    SET Value = N'{
  "BabyAge": {
    "Name": "BabyAge",
    "DisplayName": "Edad del bebé",
    "Description": "Edad del bebé en meses",
    "Type": "Number",
    "IsRequired": false,
    "ValidationPattern": "^\\d{1,3}$",
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
  }
}',
        UpdatedAt = GETUTCDATE()
    WHERE BusinessId = @BusinessId 
    AND [Key] = @ConfigKey;
    
    PRINT 'Configuración de atributos actualizada exitosamente.';
END
ELSE
BEGIN
    -- Insertar nueva configuración
    INSERT INTO BusinessConfigurations (
        BusinessConfigurationId,
        BusinessId,
        [Key],
        Value,
        Description,
        CreatedAt,
        UpdatedAt
    )
    VALUES (
        NEWID(),
        @BusinessId,
        @ConfigKey,
        N'{
  "BabyAge": {
    "Name": "BabyAge",
    "DisplayName": "Edad del bebé",
    "Description": "Edad del bebé en meses",
    "Type": "Number",
    "IsRequired": false,
    "ValidationPattern": "^\\d{1,3}$",
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
  }
}',
        'Configuración de atributos para extracción de entidades (Baby Spa)',
        GETUTCDATE(),
        GETUTCDATE()
    );
    
    PRINT 'Configuración de atributos insertada exitosamente.';
END

-- Verificar la configuración insertada
SELECT 
    BusinessConfigurationId,
    BusinessId,
    [Key],
    Description,
    LEFT(Value, 200) AS ValuePreview,
    CreatedAt,
    UpdatedAt
FROM BusinessConfigurations
WHERE BusinessId = @BusinessId 
AND [Key] = @ConfigKey;
