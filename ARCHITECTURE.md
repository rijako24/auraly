# Project Architecture

This project is an Azure Functions based AI Bot.

Tech stack:
- .NET 8 Isolated Azure Functions
- Azure OpenAI / Azure AI SDK
- Dependency Injection
- Application Insights
- Managed Identity for auth

Main flow:
- HTTP Trigger receives message
- Message goes to Application layer
- AI Service processes with Azure OpenAI
- Response is returned to client

Design principles:
- No business logic in Functions
- All AI calls go through services
- Configuration via appsettings + environment variables
