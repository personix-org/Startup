using System.Reflection;
using System.Reflection.Emit;
using Personix.Startup;
using Shouldly;
using Xunit;

// StartupService keeps its readiness latch in a single static field for the whole test process
// (this is intentional - see docs/README.md: "Readiness is process-wide and one-way"). Every test
// below resets that field via reflection so it can start from a known "not ready" state instead of
// depending on execution order. That reset is only safe if test methods never run concurrently with
// each other, so parallelization is switched off for the whole assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Personix.Startup.Tests;

public class StartupServiceTests
{
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(2);

    // `Tcs` is `static readonly` (initonly at the IL level), so plain FieldInfo.SetValue refuses it
    // with a FieldAccessException. Reflection.Emit can still target it directly with a raw `stsfld`,
    // because initonly is enforced by the C# compiler/verifier, not by the JIT. This is a test-only
    // reset shim; production code is untouched and keeps its documented one-way latch behaviour.
    private static readonly Action<TaskCompletionSource> ResetTcsField = CreateTcsFieldSetter();

    private static Action<TaskCompletionSource> CreateTcsFieldSetter()
    {
        var field = typeof(StartupService).GetField("Tcs", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "StartupService no longer declares a private static 'Tcs' field - update this test's reset helper to match the new implementation.");

        var dynamicMethod = new DynamicMethod(
            "ResetStartupServiceTcs",
            typeof(void),
            [typeof(TaskCompletionSource)],
            typeof(StartupService),
            skipVisibility: true);
        var il = dynamicMethod.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);

        return (Action<TaskCompletionSource>)dynamicMethod.CreateDelegate(typeof(Action<TaskCompletionSource>));
    }

    public StartupServiceTests()
    {
        // xUnit creates a fresh instance of this class per [Fact], so resetting here in the
        // constructor guarantees every test starts "not ready" regardless of what earlier tests did.
        ResetTcsField(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    // Awaits `task` but never blocks longer than the safety net, so a mutant that makes
    // StartupService hang forever fails this test in ~2s instead of hanging `dotnet test` forever.
    private static async Task AwaitWithinSafetyNetAsync(Task task)
    {
        var winner = await Task.WhenAny(task, Task.Delay(SafetyNet));
        winner.ShouldBe(task, $"Task did not complete within {SafetyNet} - StartupService likely hung.");
        await task;
    }

    // ---- Not-ready state ------------------------------------------------------------------

    [Fact]
    public void WaitForReadyAsync_DoesNotCompleteBeforeMarkAsReadyIsCalled()
    {
        var waitTask = StartupService.WaitForReadyAsync(CancellationToken.None);

        waitTask.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task WaitForReadyAsync_CompletesSuccessfullyOnceMarkAsReadyIsCalled()
    {
        var waitTask = StartupService.WaitForReadyAsync(CancellationToken.None);
        waitTask.IsCompleted.ShouldBeFalse("sanity check: must not be ready before MarkAsReady runs");

        StartupService.MarkAsReady();

        await AwaitWithinSafetyNetAsync(waitTask);
        waitTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void WaitForReadyAsync_CompletesImmediatelyWhenAlreadyReady()
    {
        StartupService.MarkAsReady();

        var waitTask = StartupService.WaitForReadyAsync(CancellationToken.None);

        // No await at all: this must already be resolved the instant it is returned.
        waitTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    // ---- Cancellation ----------------------------------------------------------------------

    [Fact]
    public async Task WaitForReadyAsync_ThrowsOperationCanceledExceptionWhenTokenCancelsBeforeReady()
    {
        using var cts = new CancellationTokenSource();
        var waitTask = StartupService.WaitForReadyAsync(cts.Token);
        waitTask.IsCompleted.ShouldBeFalse();

        cts.Cancel();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => AwaitWithinSafetyNetAsync(waitTask));
        exception.CancellationToken.ShouldBe(cts.Token);
        waitTask.IsCanceled.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitForReadyAsync_ThrowsWhenGivenAnAlreadyCanceledTokenAndNotReady()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => AwaitWithinSafetyNetAsync(StartupService.WaitForReadyAsync(cts.Token)));
    }

    [Fact]
    public void WaitForReadyAsync_IgnoresCancellationRequestedAfterReadinessWasAlreadySignalled()
    {
        StartupService.MarkAsReady();
        using var cts = new CancellationTokenSource();
        var waitTask = StartupService.WaitForReadyAsync(cts.Token);

        cts.Cancel();

        waitTask.Status.ShouldBe(TaskStatus.RanToCompletion);
    }

    // ---- MarkAsReady idempotency ------------------------------------------------------------

    [Fact]
    public void MarkAsReady_SecondAndThirdCallDoNotThrowAndLatchStaysSuccessful()
    {
        StartupService.MarkAsReady();

        StartupService.MarkAsReady();
        StartupService.MarkAsReady();

        var waitTask = StartupService.WaitForReadyAsync(CancellationToken.None);
        waitTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    // ---- Fan-out to multiple waiters --------------------------------------------------------

    [Fact]
    public async Task WaitForReadyAsync_ReleasesAllWaitersCreatedBeforeASingleMarkAsReadyCall()
    {
        var wait1 = StartupService.WaitForReadyAsync(CancellationToken.None);
        var wait2 = StartupService.WaitForReadyAsync(CancellationToken.None);
        var wait3 = StartupService.WaitForReadyAsync(CancellationToken.None);

        // These must genuinely be pending - otherwise this is not testing fan-out at all.
        wait1.IsCompleted.ShouldBeFalse();
        wait2.IsCompleted.ShouldBeFalse();
        wait3.IsCompleted.ShouldBeFalse();

        StartupService.MarkAsReady();

        await AwaitWithinSafetyNetAsync(Task.WhenAll(wait1, wait2, wait3));

        wait1.IsCompletedSuccessfully.ShouldBeTrue();
        wait2.IsCompletedSuccessfully.ShouldBeTrue();
        wait3.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitForReadyAsync_ConcurrentWaitersAndConcurrentMarkAsReadyCallsAllResolveSuccessfully()
    {
        var waiters = Enumerable.Range(0, 20)
            .Select(_ => StartupService.WaitForReadyAsync(CancellationToken.None))
            .ToArray();

        var markers = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(StartupService.MarkAsReady))
            .ToArray();

        await AwaitWithinSafetyNetAsync(Task.WhenAll(markers));
        await AwaitWithinSafetyNetAsync(Task.WhenAll(waiters));

        waiters.ShouldAllBe(t => t.IsCompletedSuccessfully);
    }
}
