namespace StartUp;

public static class StartUpService
{
    private static readonly TaskCompletionSource Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        return Tcs.Task.WaitAsync(cancellationToken);
    }

    public static void MarkAsReady()
    {
        Tcs.TrySetResult();
    }
}
