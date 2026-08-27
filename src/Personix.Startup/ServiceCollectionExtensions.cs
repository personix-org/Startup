using Microsoft.Extensions.DependencyInjection.Extensions;
using Personix.Startup;

// Extension methods on IServiceCollection live in this namespace by convention, so
// builder.Services.AddStartupService() resolves without an extra using in Program.cs.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="IStartupService"/> in the application's service collection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the readiness latch as a singleton, unless the application already registered its own.</summary>
    /// <param name="services">The collection to add the registration to.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A singleton is the whole point: the warm-up worker and the request pipeline have to signal and
    /// await one latch, not one each.
    /// <para>
    /// While the deprecated static members of <see cref="StartupService"/> still exist, this registers
    /// the same instance they act on, so an application can move one call site at a time without the
    /// two halves ending up on separate latches. An application that wants a latch of its own — one
    /// integration-test host not inheriting another host's readiness — registers
    /// <c>services.AddSingleton&lt;IStartupService&gt;(new StartupService())</c> first; this method
    /// leaves an existing registration alone.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddStartupService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IStartupService>(StartupService.Shared);

        return services;
    }
}
