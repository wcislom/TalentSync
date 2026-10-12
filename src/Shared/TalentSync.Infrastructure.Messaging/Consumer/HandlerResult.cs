namespace TalentSync.Infrastructure.Messaging.Consumer;

/// <summary>
/// What a worker's handler made of one event. Transient failures are not results: they are exceptions, classified
/// by the skeleton (<see cref="FailureClassifier"/>).
/// </summary>
public abstract record HandlerResult
{
    private HandlerResult()
    {
    }

    public static HandlerResult Done { get; } = new DoneResult();

    public static HandlerResult Permanent(string reason) => new PermanentResult(reason);

    /// <summary>The effect is done; the skeleton writes the inbox row and checkpoints.</summary>
    public sealed record DoneResult : HandlerResult;

    /// <summary>
    /// The event can never be processed; the skeleton parks it and checkpoints. <paramref name="Reason"/> goes to
    /// <c>ParkingLot.Error</c>: a status code and an endpoint or field name, never a response body or PII (invariant 14).
    /// </summary>
    public sealed record PermanentResult(string Reason) : HandlerResult;
}
