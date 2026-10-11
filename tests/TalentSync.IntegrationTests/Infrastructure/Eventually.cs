namespace TalentSync.IntegrationTests.Infrastructure;

/// <summary>Polls an asynchronous effect until it appears (testing rule: never a fixed delay).</summary>
public static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Returns the first value <paramref name="probe"/> produces that is not null; fails after the timeout.</summary>
    public static async Task<T> GetAsync<T>(Func<Task<T?>> probe, string what, TimeSpan? timeout = null)
        where T : class
    {
        var limit = timeout ?? DefaultTimeout;
        using var cancellation = new CancellationTokenSource(limit);
        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            try
            {
                await Task.Delay(PollInterval, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                Assert.Fail($"Timed out after {limit.TotalSeconds:0} s waiting for {what}.");
            }
        }
    }
}
