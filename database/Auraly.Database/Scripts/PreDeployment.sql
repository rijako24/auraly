/*
  Existing-state migrations are owned by Publish-AuralyReleasePipeline.ps1 and
  run before DeployReport. Keep the DACPAC predeployment phase mechanical so a
  calculated plan never races schema changes or rebinds retired columns.
*/
PRINT 'Pre-deployment script ejecutado correctamente.';
GO
