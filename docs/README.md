# Personix.Startup

Start-up coordination for .NET services. Background initialisation signals when the application is
ready, and request handling awaits that signal — so a request that arrives during warm-up waits for
the cache, the migration, or the connection pool instead of reaching a half-initialised service.

## Contents

- `IStartupService` – the readiness latch. `MarkAsReady()` signals it, `WaitForReadyAsync(CancellationToken)`
  awaits it. Depend on this, and substitute it in tests.
- `StartupService` – the default implementation, one latch per instance.
- `AddStartupService()` – registers the latch as a singleton.

## Installation

```xml
<PackageReference Include="Personix.Startup" Version="2.0.0" />
```

```csharp
builder.Services.AddStartupService();
```

## Usage

### 1. Signal readiness once initialisation finishes

```csharp
using Personix.Startup;

public sealed class CacheWarmupWorker(IStartupService startupService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WarmUpCacheAsync(stoppingToken);

        startupService.MarkAsReady();

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
    var startupService = context.RequestServices.GetRequiredService<IStartupService>();
    await startupService.WaitForReadyAsync(context.RequestAborted);
    await next(context);
});
```

Register the middleware **after** health-check endpoints if the orchestrator needs to probe the
service while it is still warming up — otherwise the probe itself blocks.

### 3. Await readiness in a single endpoint

```csharp
app.MapGet("/data", async (IStartupService startupService, CancellationToken ct) =>
{
    await startupService.WaitForReadyAsync(ct);
    return Results.Ok(data);
});
```

### 4. Bound the wait

An unbounded wait turns a stuck warm-up into hanging requests. Prefer a timeout that answers with
503 instead:

```csharp
app.MapGet("/data", async (IStartupService startupService, CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(30));

    try
    {
        await startupService.WaitForReadyAsync(timeout.Token);
        return Results.Ok(data);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
```

## Testing

A test that exercises a component depending on `IStartupService` substitutes the latch instead of
waiting for real warm-up work:

```csharp
var startupService = new Mock<IStartupService>();
startupService.Setup(s => s.WaitForReadyAsync(It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

var sut = new OrderController(startupService.Object);
```

To assert that a component signals readiness, verify the call:

```csharp
startupService.Verify(s => s.MarkAsReady(), Times.Once);
```

The real implementation works just as well when the timing itself is under test, and a fresh instance
carries no readiness from any other test:

```csharp
IStartupService startupService = new StartupService();

var pending = startupService.WaitForReadyAsync(CancellationToken.None);
pending.IsCompleted.ShouldBeFalse();

startupService.MarkAsReady();
await pending;
```

Note the `IStartupService` on the left. `StartupService` implements the interface explicitly, so the
latch is reached through the interface — reading `StartupService.MarkAsReady()` off the type name
gets the deprecated static member instead.

## Migrating from 1.x

The static members still compile and still work, so an application can move one call site at a time.
They are marked `[Obsolete]` and will be removed in 3.0.

| 1.x | 2.0 |
|---|---|
| `StartupService.MarkAsReady()` | inject `IStartupService`, call `MarkAsReady()` |
| `StartupService.WaitForReadyAsync(ct)` | inject `IStartupService`, call `WaitForReadyAsync(ct)` |
| no registration | `builder.Services.AddStartupService()` |

`AddStartupService()` registers the same instance the static members act on, so a half-migrated
application stays on one latch. A worker still calling the static `MarkAsReady()` releases middleware
that already awaits the injected `IStartupService`.

## API

| Member | Description |
|---|---|
| `IStartupService.MarkAsReady()` | Signals readiness. Idempotent — repeated calls do nothing. |
| `IStartupService.WaitForReadyAsync(CancellationToken)` | Completes once readiness is signalled, or throws `OperationCanceledException` when the token is cancelled first. Completes immediately if readiness was already signalled. |
| `AddStartupService()` | Registers `IStartupService` as a singleton. Leaves an existing registration alone. |

## Notes

- Readiness is **one-way**. Once signalled it cannot be revoked, which is intentional — it models
  "the application has finished starting", not a fluctuating health state. Use health checks for the
  latter.
- Readiness is scoped to the registered singleton, so a test that needs a latch isolated from the
  rest of the process registers its own with
  `services.AddSingleton<IStartupService>(new StartupService())`.

## Licence

MIT — see [LICENSE](LICENSE).
