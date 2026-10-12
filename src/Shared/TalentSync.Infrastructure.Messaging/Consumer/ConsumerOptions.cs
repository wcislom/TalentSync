namespace TalentSync.Infrastructure.Messaging.Consumer;

/// <summary>Tunables of the handler loop, bound from the <c>Consumer</c> configuration section.</summary>
public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>Wait after the first failed attempt; doubles with each further attempt.</summary>
    public TimeSpan InitialBackoff { get; set; }

    /// <summary>Upper bound of the back-off. A transient outage is waited out at this interval for as long as it lasts.</summary>
    public TimeSpan MaxBackoff { get; set; }

    /// <summary>Attempts for an unclassified exception before the event is parked.</summary>
    public int MaxUnclassifiedAttempts { get; set; }

    public bool IsValid() =>
        InitialBackoff > TimeSpan.Zero && MaxBackoff >= InitialBackoff && MaxUnclassifiedAttempts > 0;
}
