using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Personix.Startup.Tests;

/// <summary>
/// Covers the deprecated static members kept for applications still mid-migration.
/// </summary>
/// <remarks>
/// The static members act on one process-wide instance, and the latch is one-way, so this state
/// cannot be reset between tests. That is why the whole contract is verified by a single test that
/// signals readiness exactly once: it is the only test in the suite allowed to touch the shared
/// instance's readiness, which keeps it independent of execution order.
/// </remarks>
public class ObsoleteStaticFacadeTests
{
    [Fact]
    public async Task ObsoleteStaticMembers_ShareOneLatchWithTheDependencyInjectedInstance()
    {
        var services = new ServiceCollection();
        services.AddStartupService();
        using var provider = services.BuildServiceProvider();
        var injected = provider.GetRequiredService<IStartupService>();

        var injectedWait = injected.WaitForReadyAsync(CancellationToken.None);
#pragma warning disable CS0618 // deliberately exercising the deprecated surface
        var staticWait = StartupService.WaitForReadyAsync(CancellationToken.None);
#pragma warning restore CS0618

        injectedWait.IsCompleted.ShouldBeFalse("sanity check: nothing has signalled readiness yet");
        staticWait.IsCompleted.ShouldBeFalse();

#pragma warning disable CS0618
        StartupService.MarkAsReady();
#pragma warning restore CS0618

        // A half-migrated application signals readiness through the old static call while its
        // middleware already awaits the injected instance. If those were two separate latches, every
        // request would hang forever - so this is the single assertion that matters most here.
        await Task.WhenAll(injectedWait, staticWait).WaitAsync(TimeSpan.FromSeconds(2));
        injectedWait.IsCompletedSuccessfully.ShouldBeTrue();
        staticWait.IsCompletedSuccessfully.ShouldBeTrue();

        injected.WaitForReadyAsync(CancellationToken.None).IsCompletedSuccessfully.ShouldBeTrue(
            "readiness signalled through the static facade must hold for later waiters too");
    }
}
