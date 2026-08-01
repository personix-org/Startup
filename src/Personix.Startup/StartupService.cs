namespace Personix.Startup;

/// <summary>
/// Process-wide readiness latch. Background start-up work calls <see cref="MarkAsReady"/> once, and
/// request handling calls <see cref="WaitForReadyAsync"/> so a request that arrives during warm-up
/// waits for the cache, the migration, or the connection pool instead of reaching a half-initialised
/// service.
/// </summary>
/// <remarks>
/// The latch is one-way and cannot be revoked once signalled — it models "the application has
/// finished starting", not a fluctuating health state. Because the state lives in a single static
/// field, it is shared by the whole process: tests that call <see cref="MarkAsReady"/> affect every
/// later test in the same process unless the field is reset between them.
/// </remarks>
public static class StartupService
{
    private static readonly TaskCompletionSource Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits until <see cref="MarkAsReady"/> has been called, then returns immediately on every later call.</summary>
    /// <param name="cancellationToken">Stops the wait early; ignored once readiness has already been signalled.</param>
    /// <returns>A task that completes once the application has signalled readiness.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before <see cref="MarkAsReady"/> was called.
    /// </exception>
    public static Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        return Tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Signals that start-up has finished, releasing every caller currently in <see cref="WaitForReadyAsync"/> and every future one.</summary>
    /// <remarks>
    /// Idempotent and safe to call from multiple threads — only the first call has any effect, later
    /// ones are no-ops. Call this only once everything critical has finished; if initialisation fails,
    /// do not call it, so the service never admits traffic while broken.
    /// </remarks>
    public static void MarkAsReady()
    {
        Tcs.TrySetResult();
    }
}
