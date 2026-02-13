# Nwo.StartUp

Shared startup coordination helper for new-world-order services.

## Contents

- `StartUpService` – static service that lets background workers signal readiness and endpoints wait before handling requests.

## Usage

```xml
<PackageReference Include="Nwo.StartUp" Version="1.0.0" />
```

```csharp
using StartUp;

// In a background worker – signal that the app is ready
StartUpService.MarkAsReady();

// In an endpoint – wait until ready before processing
app.MapGet("/data", async (CancellationToken ct) =>
{
    await StartUpService.WaitForReadyAsync(ct);
    // ...
});
```

## Part of the NWO package family

| Package | Description |
|---------|-------------|
| Nwo.Constants | Shared constants |
| Nwo.Options | DI options validation |
| **Nwo.StartUp** | Startup coordination |
| Nwo.Persistence | EF Core / SQLite base |
| Nwo.ServiceDefaults | Aspire service defaults (OTel, Serilog, health checks) |
