using Shouldly;
using Xunit;

namespace Personix.Startup.Tests;

/// <summary>
/// Covers the instance latch. Every test builds its own <see cref="StartupService"/>, so nothing here
/// touches process-wide state and the class runs in parallel with the rest of the suite.
/// </summary>
public class StartupServiceTests
{
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(2);

    // The interface members are implemented explicitly, so the latch is reached through
    // IStartupService - which is how a consuming application injects it and how it gets substituted
    // in that application's tests.
    private static IStartupService CreateSut()
    {
        return new StartupService();
    }

    // Awaits `task` but never blocks longer than the safety net, so a mutant that makes the latch
    // hang forever fails this test in ~2s instead of hanging `dotnet test` forever.
    private static async Task AwaitWithinSafetyNetAsync(Task task)
    {
        var winner = await Task.WhenAny(task, Task.Delay(SafetyNet));
        winner.ShouldBe(task, $"Task did not complete within {SafetyNet} - the latch likely hung.");
        await task;
    }

    // ---- Not-ready state ------------------------------------------------------------------

    [Fact]
    public void WaitForReadyAsync_DoesNotCompleteBeforeMarkAsReadyIsCalled()
    {
        var sut = CreateSut();

        var waitTask = sut.WaitForReadyAsync(CancellationToken.None);

        waitTask.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task WaitForReadyAsync_CompletesSuccessfullyOnceMarkAsReadyIsCalled()
    {
        var sut = CreateSut();
        var waitTask = sut.WaitForReadyAsync(CancellationToken.None);
        waitTask.IsCompleted.ShouldBeFalse("sanity check: must not be ready before MarkAsReady runs");

        sut.MarkAsReady();

        await AwaitWithinSafetyNetAsync(waitTask);
        waitTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public void WaitForReadyAsync_CompletesImmediatelyWhenAlreadyReady()
    {
        var sut = CreateSut();
        sut.MarkAsReady();

        var waitTask = sut.WaitForReadyAsync(CancellationToken.None);

        // No await at all: this must already be resolved the instant it is returned.
        waitTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    // ---- Instance isolation -----------------------------------------------------------------
    // The whole point of moving off a static field: one instance going ready must say nothing about
    // any other. Without this, a consuming application's tests leak readiness into one another.

    [Fact]
    public void MarkAsReady_OnOneInstanceLeavesAnotherInstanceUnaffected()
    {
        var first = CreateSut();
        var second = CreateSut();

        first.MarkAsReady();

        first.WaitForReadyAsync(CancellationToken.None).IsCompletedSuccessfully.ShouldBeTrue();
        second.WaitForReadyAsync(CancellationToken.None).IsCompleted.ShouldBeFalse(
            "each instance owns its own latch - readiness must not leak between them");
    }

    // ---- Cancellation ----------------------------------------------------------------------

    [Fact]
    public async Task WaitForReadyAsync_ThrowsOperationCanceledExceptionWhenTokenCancelsBeforeReady()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        var waitTask = sut.WaitForReadyAsync(cts.Token);
        waitTask.IsCompleted.ShouldBeFalse();

        await cts.CancelAsync();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => AwaitWithinSafetyNetAsync(waitTask));
        exception.CancellationToken.ShouldBe(cts.Token);
        waitTask.IsCanceled.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitForReadyAsync_ThrowsWhenGivenAnAlreadyCanceledTokenAndNotReady()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => AwaitWithinSafetyNetAsync(sut.WaitForReadyAsync(cts.Token)));
    }

    [Fact]
    public void WaitForReadyAsync_IgnoresCancellationRequestedAfterReadinessWasAlreadySignalled()
    {
        var sut = CreateSut();
        sut.MarkAsReady();
        using var cts = new CancellationTokenSource();
        var waitTask = sut.WaitForReadyAsync(cts.Token);

        cts.Cancel();

        waitTask.Status.ShouldBe(TaskStatus.RanToCompletion);
    }

    [Fact]
    public async Task WaitForReadyAsync_CancellingOneWaiterLeavesTheOthersPending()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        var cancelled = sut.WaitForReadyAsync(cts.Token);
        var survivor = sut.WaitForReadyAsync(CancellationToken.None);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => AwaitWithinSafetyNetAsync(cancelled));
        survivor.IsCompleted.ShouldBeFalse("one cancelled waiter must not disturb the shared latch");

        sut.MarkAsReady();

        await AwaitWithinSafetyNetAsync(survivor);
    }

    // ---- MarkAsReady idempotency ------------------------------------------------------------

    [Fact]
    public void MarkAsReady_SecondAndThirdCallDoNotThrowAndLatchStaysSuccessful()
    {
        var sut = CreateSut();
        sut.MarkAsReady();

        sut.MarkAsReady();
        sut.MarkAsReady();

        var waitTask = sut.WaitForReadyAsync(CancellationToken.None);
        waitTask.IsCompletedSuccessfully.ShouldBeTrue();
    }

    // ---- Fan-out to multiple waiters --------------------------------------------------------

    [Fact]
    public async Task WaitForReadyAsync_ReleasesAllWaitersCreatedBeforeASingleMarkAsReadyCall()
    {
        var sut = CreateSut();
        var wait1 = sut.WaitForReadyAsync(CancellationToken.None);
        var wait2 = sut.WaitForReadyAsync(CancellationToken.None);
        var wait3 = sut.WaitForReadyAsync(CancellationToken.None);

        // These must genuinely be pending - otherwise this is not testing fan-out at all.
        wait1.IsCompleted.ShouldBeFalse();
        wait2.IsCompleted.ShouldBeFalse();
        wait3.IsCompleted.ShouldBeFalse();

        sut.MarkAsReady();

        await AwaitWithinSafetyNetAsync(Task.WhenAll(wait1, wait2, wait3));

        wait1.IsCompletedSuccessfully.ShouldBeTrue();
        wait2.IsCompletedSuccessfully.ShouldBeTrue();
        wait3.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitForReadyAsync_ConcurrentWaitersAndConcurrentMarkAsReadyCallsAllResolveSuccessfully()
    {
        var sut = CreateSut();
        var waiters = Enumerable.Range(0, 20)
            .Select(_ => sut.WaitForReadyAsync(CancellationToken.None))
            .ToArray();

        var markers = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(sut.MarkAsReady))
            .ToArray();

        await AwaitWithinSafetyNetAsync(Task.WhenAll(markers));
        await AwaitWithinSafetyNetAsync(Task.WhenAll(waiters));

        waiters.ShouldAllBe(t => t.IsCompletedSuccessfully);
    }
}
