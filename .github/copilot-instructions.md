# messaging-service
.NET 8 SignalR messaging hub for the DatingApp.
## Build & Test
```bash
dotnet restore MessagingService.csproj && dotnet build && dotnet test MessagingService.Tests/MessagingService.Tests.csproj
```
## Architecture
- SignalR hubs: Hubs/MessagingHub.cs (groups-based) + Hubs/MessagingHub.Spec.cs (match-based)
- CQRS via MediatR: Commands/ and Queries/
- EF Core 8 with MySQL
- Safety agent: Services/SafetyAgentService.cs (LLM message classifier)
- Tests use InMemoryDatabase + Moq
## Rules
- All new code must have unit tests
- Use InMemoryDatabase for test isolation
