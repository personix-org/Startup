namespace Personix.Startup;

/// <summary>
/// The default <see cref="IStartupService"/>: a one-way readiness latch backed by a
/// <see cref="TaskCompletionSource"/> that belongs to this instance alone.
/// </summary>
/// <remarks>
/// Register it with <c>AddStartupService()</c> and inject <see cref="IStartupService"/>; construct it
/// directly only in tests that want a latch isolated from everything else in the process.
/// <para>
/// The interface members are implemented explicitly, so <see cref="MarkAsReady()"/> and
/// <see cref="WaitForReadyAsync(CancellationToken)"/> reached through the type name are the
/// deprecated static ones, while the instance behind <see cref="IStartupService"/> is the supported
/// surface. Both act on a latch, but not on the same one: the static members share a single
/// process-wide instance, whereas every <c>new StartupService()</c> owns its own.
/// </para>
/// </remarks>
public sealed class StartupService : IStartupService
{
    /// <summary>
    /// The one instance the deprecated static members act on, and the one <c>AddStartupService()</c>
    /// registers while those members still exist.
    /// </summary>
    /// <remarks>
    /// Sharing it is what lets an application migrate one call site at a time: a warm-up worker still
    /// calling the static <see cref="MarkAsReady()"/> releases middleware that already awaits the
    /// injected <see cref="IStartupService"/>. Registering a fresh instance instead would give those
    /// two halves separate latches and hang every request.
    /// </remarks>
    internal static readonly StartupService Shared = new();

    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    Task IStartupService.WaitForReadyAsync(CancellationToken cancellationToken)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    void IStartupService.MarkAsReady()
    {
        _tcs.TrySetResult();
    }

    /// <summary>Waits on the process-wide latch until <see cref="MarkAsReady()"/> has been called.</summary>
    /// <param name="cancellationToken">Stops the wait early; ignored once readiness has already been signalled.</param>
    /// <returns>A task that completes once the application has signalled readiness.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before <see cref="MarkAsReady()"/> was called.
    /// </exception>
    [Obsolete(
        "The static latch is process-wide, so it cannot be substituted in tests and leaks readiness " +
        "between them. Call AddStartupService() and inject IStartupService instead. This member will " +
        "be removed in 3.0.")]
    public static Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        return ((IStartupService)Shared).WaitForReadyAsync(cancellationToken);
    }

    /// <summary>Signals readiness on the process-wide latch, releasing every caller waiting on it.</summary>
    /// <remarks>Idempotent and safe to call from multiple threads — only the first call has any effect.</remarks>
    [Obsolete(
        "The static latch is process-wide, so it cannot be substituted in tests and leaks readiness " +
        "between them. Call AddStartupService() and inject IStartupService instead. This member will " +
        "be removed in 3.0.")]
    public static void MarkAsReady()
    {
        ((IStartupService)Shared).MarkAsReady();
    }
}
