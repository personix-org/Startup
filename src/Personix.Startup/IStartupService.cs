namespace Personix.Startup;

/// <summary>
/// Readiness latch for an application that needs a warm-up. Background start-up work calls
/// <see cref="MarkAsReady"/> once, and request handling calls <see cref="WaitForReadyAsync"/> so a
/// request arriving during warm-up waits for the cache, the migration, or the connection pool
/// instead of reaching a half-initialised service.
/// </summary>
/// <remarks>
/// The latch is one-way and cannot be revoked once signalled — it models "the application has
/// finished starting", not a fluctuating health state. Use health checks for the latter.
/// <para>
/// Depend on this interface rather than on <see cref="StartupService"/>, so a test can substitute a
/// latch that is ready from the start and never has to wait for real warm-up work.
/// </para>
/// </remarks>
public interface IStartupService
{
    /// <summary>Waits until <see cref="MarkAsReady"/> has been called, then returns immediately on every later call.</summary>
    /// <param name="cancellationToken">Stops the wait early; ignored once readiness has already been signalled.</param>
    /// <returns>A task that completes once the application has signalled readiness.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before <see cref="MarkAsReady"/> was called.
    /// </exception>
    Task WaitForReadyAsync(CancellationToken cancellationToken);

    /// <summary>Signals that start-up has finished, releasing every caller currently in <see cref="WaitForReadyAsync"/> and every future one.</summary>
    /// <remarks>
    /// Implementations are idempotent and safe to call from multiple threads — only the first call
    /// has any effect. Call this only once everything critical has finished; if initialisation
    /// fails, do not call it, so the service never admits traffic while broken.
    /// </remarks>
    void MarkAsReady();
}
