using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Personix.Startup.Tests;

/// <summary>
/// Covers the DI registration. None of these assert readiness, so they stay independent of whatever
/// <see cref="ObsoleteStaticFacadeTests"/> does to the shared instance.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddStartupService_RegistersIStartupService()
    {
        var services = new ServiceCollection();

        services.AddStartupService();

        using var provider = services.BuildServiceProvider();
        provider.GetService<IStartupService>().ShouldNotBeNull();
    }

    [Fact]
    public void AddStartupService_GivesEveryConsumerTheSameInstance()
    {
        var services = new ServiceCollection();
        services.AddStartupService();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IStartupService>();
        var second = provider.GetRequiredService<IStartupService>();

        // A per-consumer latch would mean the middleware waits on one instance while the warm-up
        // worker signals another, and the application hangs on every request.
        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void AddStartupService_CalledTwiceStillRegistersOnlyOneService()
    {
        var services = new ServiceCollection();

        services.AddStartupService();
        services.AddStartupService();

        services.Count(descriptor => descriptor.ServiceType == typeof(IStartupService)).ShouldBe(1);
    }

    [Fact]
    public void AddStartupService_KeepsARegistrationTheApplicationMadeItself()
    {
        // An application that wants an isolated latch - one integration-test host not seeing another
        // host's readiness - registers its own instance. AddStartupService must not overwrite it.
        var ownInstance = new StartupService();
        var services = new ServiceCollection();
        services.AddSingleton<IStartupService>(ownInstance);

        services.AddStartupService();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupService>().ShouldBeSameAs(ownInstance);
    }

    [Fact]
    public void AddStartupService_ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddStartupService();

        returned.ShouldBeSameAs(services);
    }
}
