# messaging-service

Real-time messaging backend for the DatingApp platform.

## What It Does

- SignalR-based live messaging
- REST fallback endpoints for message operations
- Moderation/safety integration for content checks
- Read receipts, typing indicators, and conversation state flows

## Why It Is Interesting

This repository demonstrates real-time system patterns:
- SignalR hub design and group/match routing
- Hybrid real-time + REST architecture
- Safety classification integration in message pipelines
- Test isolation with in-memory database strategies

## Stack

- .NET 8
- ASP.NET Core Web API + SignalR
- MediatR (commands/queries)
- EF Core 8 + MySQL

## Project Layout

```text
messaging-service/
  Hubs/
  Controllers/
  Commands/
  Queries/
  Services/
  Data/
  Models/
  MessagingService.Tests/
```

## Build and Test

```bash
dotnet restore MessagingService.csproj
dotnet build MessagingService.csproj
dotnet test MessagingService.Tests/MessagingService.Tests.csproj
```

## Run Locally

```bash
dotnet run --project MessagingService.csproj
```

## Notable Areas

- Hub protocol and routing decisions
- Moderation pipeline integration
- Rate limiting / anti-abuse protections
- Cross-service identity usage

## Related Repositories

- `best-koder-org/mobile_dejtingapp`
- `best-koder-org/UserService`
- `best-koder-org/dejting-yarp`

## Status

Active development repository used by current chat flows.
