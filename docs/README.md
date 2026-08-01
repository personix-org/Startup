# Personix.Startup

Start-up coordination for .NET services. Background initialisation signals when the application is
ready, and request handling awaits that signal — so a request that arrives during warm-up waits for
the cache, the migration, or the connection pool instead of reaching a half-initialised service.

## Contents

- `StartupService` – static coordinator with two members: `MarkAsReady()` to signal readiness and
  `WaitForReadyAsync(CancellationToken)` to await it.

## Installation

```xml
<PackageReference Include="Personix.Startup" Version="1.0.2" />
```

No service registration is required — the coordinator is static.

## Usage

### 1. Signal readiness once initialisation finishes

```csharp
using Personix.Startup;

public sealed class CacheWarmupWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmUpCacheAsync(stoppingToken);

        StartupService.MarkAsReady();

        // continue with ongoing background work
    }
}
```

Call `MarkAsReady()` **only after everything critical is done**. If initialisation fails, do not call
it — let the host fail or stay degraded rather than admit traffic to a broken service.

### 2. Await readiness — middleware covers every endpoint

```csharp
app.Use(async (context, next) =>
{
    await StartupService.WaitForReadyAsync(context.RequestAborted);
    await next(context);
});
```

Register the middleware **after** health-check endpoints if the orchestrator needs to probe the
service while it is still warming up — otherwise the probe itself blocks.

### 3. Await readiness in a single endpoint

```csharp
app.MapGet("/data", async (CancellationToken ct) =>
{
    await StartupService.WaitForReadyAsync(ct);
    return Results.Ok(data);
});
```

### 4. Bound the wait

An unbounded wait turns a stuck warm-up into hanging requests. Prefer a timeout that answers with
503 instead:

```csharp
app.MapGet("/data", async (CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(30));

    try
    {
        await StartupService.WaitForReadyAsync(timeout.Token);
        return Results.Ok(data);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
```

## API

| Member | Description |
|---|---|
| `MarkAsReady()` | Signals readiness. Idempotent — repeated calls do nothing. |
| `WaitForReadyAsync(CancellationToken)` | Completes once readiness is signalled, or throws `OperationCanceledException` when the token is cancelled first. Completes immediately if readiness was already signalled. |

## Notes

- Readiness is **process-wide and one-way**. Once signalled it cannot be revoked, which is intentional
  — it models "the application has finished starting", not a fluctuating health state. Use health
  checks for the latter.
- Because the state is static, tests that call `MarkAsReady()` affect every later test in the same
  process.

## Licence

MIT — see [LICENSE](LICENSE).
