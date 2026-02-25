# AGENTS.md

## Cursor Cloud specific instructions

### Project overview

This repository contains two main components:

1. **AG AI Hub** (`ag-ai-hub/src/AgAiHub/`) — A .NET 8 ASP.NET Core Web API that serves as a central AI gateway for all AG ONE products. It proxies requests to Azure OpenAI or OpenAI and tracks usage per product via API key auth.
2. **AG ONE SSO Middleware** (`AgOneSsoMiddleware.cs`) — A single-file copy-paste middleware for cross-product SSO authentication using Entra ID JWTs.
3. **Blazor Auth Components** (`blazor-auth-components/`) — Razor component examples for Blazor Server auth integration.

Only the AG AI Hub is a runnable service; the SSO middleware and Blazor components are library/reference code.

### .NET 8 SDK

The .NET 8 SDK is installed at `$HOME/.dotnet`. The `PATH` and `DOTNET_ROOT` are configured in `~/.bashrc`. If `dotnet` is not found, run:
```
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
```

### Building and running

```bash
cd ag-ai-hub/src/AgAiHub
dotnet restore
dotnet build          # 0 warnings expected
dotnet run --launch-profile http   # starts on http://localhost:5093
```

- Swagger UI is available at `http://localhost:5093/swagger/index.html` in Development mode.
- The health endpoint `GET /api/health` requires no authentication.
- All other `/api/` routes require `X-Api-Key` and `X-Product-Code` headers. Test keys are in `appsettings.json` under `AiHub.Products`.

### Important caveats

- **No test suite exists** in this repository. There are no unit or integration tests to run.
- **No linter configuration** is present. The project relies on the C# compiler for static analysis (`dotnet build` with 0 warnings).
- The AI chat/embedding/document endpoints will fail at runtime unless real Azure OpenAI or OpenAI credentials are configured in `appsettings.json` (the defaults are placeholder values). The rest of the API (health, usage, auth middleware) works without external credentials.
- HTTPS redirection is enabled but the `http` launch profile only listens on HTTP; expect a "Failed to determine the https port" warning in logs — this is harmless in development.
