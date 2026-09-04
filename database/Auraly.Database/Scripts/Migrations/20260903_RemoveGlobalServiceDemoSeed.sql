-- Retira únicamente el contenido demostrativo que el antiguo seed global
-- agregó a negocios existentes. La huella exacta evita borrar contenido creado
-- por usuarios y el aprovisionamiento futuro permanece sin contenido comercial.
SET NOCOUNT ON;

DELETE attachment
FROM dbo.BusinessAttachments attachment
WHERE attachment.BlobPath=N'confirmations/indicaciones-para-tu-visita.pdf'
  AND attachment.Filename=N'Indicaciones-para-tu-visita.pdf'
  AND attachment.Description=N'Indicaciones para la visita';

UPDATE serviceValue
SET CategoryId=NULL,UpdatedAt=SYSUTCDATETIME()
FROM dbo.Services serviceValue
JOIN dbo.ServiceCategories category ON category.ServiceCategoryId=serviceValue.CategoryId
WHERE (category.Name=N'Planes Baby Spa' AND category.Description=N'Experiencias completas de spa para bebes: hidroterapia, masajes y momentos de relajacion o estimulacion segun la edad. Ideal cuando la familia quiere una vivencia principal y personalizada para bienestar, descanso y desarrollo sensorial.')
   OR (category.Name=N'Taller' AND category.Description=N'Encuentros guiados por profesionales para trabajar temas puntuales del desarrollo y el cuidado del bebe, como estimulacion, juego, vinculo, rutinas o preparacion para nuevas etapas.')
   OR (category.Name=N'Clase' AND category.Description=N'Espacios practicos y acompanados para que mama, papa o cuidadores aprendan tecnicas y actividades que pueden repetir en casa, compartiendo con el bebe de forma tranquila y segura.');

DELETE category
FROM dbo.ServiceCategories category
WHERE (
      (category.Name=N'Planes Baby Spa' AND category.Description=N'Experiencias completas de spa para bebes: hidroterapia, masajes y momentos de relajacion o estimulacion segun la edad. Ideal cuando la familia quiere una vivencia principal y personalizada para bienestar, descanso y desarrollo sensorial.')
      OR (category.Name=N'Taller' AND category.Description=N'Encuentros guiados por profesionales para trabajar temas puntuales del desarrollo y el cuidado del bebe, como estimulacion, juego, vinculo, rutinas o preparacion para nuevas etapas.')
      OR (category.Name=N'Clase' AND category.Description=N'Espacios practicos y acompanados para que mama, papa o cuidadores aprendan tecnicas y actividades que pueden repetir en casa, compartiendo con el bebe de forma tranquila y segura.')
  );

PRINT N'Contenido demostrativo global retirado de negocios sin servicios.';
GO
