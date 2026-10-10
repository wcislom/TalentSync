using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace TalentSync.Contracts;

/// <summary>
/// Wire format of <see cref="EventEnvelope"/>, shared by the producer and the consumers so both sides agree on it.
/// Tolerant reader: unknown fields are ignored; a missing required field or malformed JSON is not an envelope.
/// </summary>
public static class EventEnvelopeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    public static string Serialize(EventEnvelope envelope) =>
        JsonSerializer.Serialize(envelope, Options);

    public static bool TryDeserialize(ReadOnlySpan<byte> utf8Json, [NotNullWhen(true)] out EventEnvelope? envelope)
    {
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(utf8Json, Options);
            return envelope is not null;
        }
        catch (JsonException)
        {
            envelope = null;
            return false;
        }
    }
}
