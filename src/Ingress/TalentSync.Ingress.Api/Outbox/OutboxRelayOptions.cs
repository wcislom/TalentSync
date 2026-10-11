namespace TalentSync.Ingress.Api.Outbox;

/// <summary>Tunables of <see cref="OutboxRelay"/>, bound from the <c>OutboxRelay</c> configuration section.</summary>
internal sealed class OutboxRelayOptions
{
    public const string SectionName = "OutboxRelay";

    /// <summary>Rows locked and published per transaction.</summary>
    public int BatchSize { get; set; }

    /// <summary>Wait after a batch that was not full: the outbox is drained, poll again later.</summary>
    public TimeSpan PollInterval { get; set; }

    /// <summary>Upper bound of the exponential back-off after a failed batch (SQL or Event Hubs unavailable).</summary>
    public TimeSpan MaxBackoff { get; set; }

    public bool IsValid() =>
        BatchSize > 0 && PollInterval > TimeSpan.Zero && MaxBackoff >= PollInterval;
}
