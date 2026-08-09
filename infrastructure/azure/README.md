# AURALY Azure environments

Deploy `shared-ai.bicep` once into `RG-AURALY-SHARED`. It creates one
`ai-auraly-shared-*` account in East US 2 with the text and Whisper
deployments. It is shared by DEV and PROD and disables key authentication.

This template creates isolated `RG-AURALY-DEV` and `RG-AURALY-PROD`
application resources. `main.bicep` deliberately does **not** create another
AI account: both environments receive the endpoint and deployment names from
the single shared deployment. It assigns each environment identity
`Cognitive Services OpenAI User` on that account, so the applications do not
need API keys in Azure.

Deployment order is shared AI, DEV, then PROD. Existing legacy AI accounts
must remain until both environments pass smoke tests; retiring them is a
separate, explicit cutover action.

Cost-oriented SKU policy:

- Azure SQL Database: Basic, 5 DTU, 2 GB.
- Function App: Flex Consumption, scale-to-zero, no always-ready instances.
- Web API: F1 in DEV and B1 in PROD.
- Admin: Static Web Apps Free.
- App Configuration: Free, accessed with managed identity.
- Service Bus: Standard because the engine requires sessions. The Standard
  base charge is subscription-scoped and already exists in the current
  subscription; separate namespaces preserve environment isolation.
- Storage: Standard LRS. Capacity and transactions can produce a small
  usage charge and are not part of the Functions compute free grant.
- Application Insights: workspace-based with a 0.1 GB/day ingestion cap.

All new Azure resource names use the AURALY brand. Global names include a
deterministic suffix generated from subscription and environment.

The template creates a user-assigned identity per environment and grants it:

- App Configuration Data Reader.
- Service Bus Data Sender and Receiver.
- Storage Blob Data Owner, Queue Data Contributor and Table Data Contributor.

SQL requires one post-deployment database step: create the environment
identity as a contained user in its database and grant only the permissions
required by the application schema. Shared OpenAI RBAC is part of the template.

Never commit SQL passwords or provider tokens. Pass secure values at deployment
time or from the release pipeline.
